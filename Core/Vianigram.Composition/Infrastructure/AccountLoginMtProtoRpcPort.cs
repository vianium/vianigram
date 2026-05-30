// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Composition.Configuration;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using AccountAuthKeyRecord = Vianigram.Account.Ports.Outbound.AuthKeyRecord;
// DcOptionRecord lives in Vianigram.Storage.Ports.Stubs but a blanket
// `using Vianigram.Storage.Ports.Stubs;` collides with this file's
// IAuthKeyStore reference (the Storage namespace ships its own stub
// of the same name). Alias only the type we actually need here.
using DcOptionRecord = Vianigram.Storage.Ports.Stubs.DcOptionRecord;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Account-only MTProto client for unauthenticated login flows.
    ///
    /// The main app transport can use a pooled channel for updates, chats and
    /// media. Login is different: it stays on a single connection with its own
    /// initConnection state, DC migration loop and reconnect cycle. This port
    /// keeps that shape while preserving the Account hex boundary: Account
    /// depends only on IMtProtoRpcPort/IMtProtoDcProvider.
    /// </summary>
    public sealed class AccountLoginMtProtoRpcPort
        : IMtProtoRpcPort
        , IMtProtoDcProvider
        , IPhoneLoginDcPreferencePort
        , ILoginConnectionWarmupPort
        , IQrLoginMigrationPort
        , IDisposable
    {
        private const uint InvokeWithLayerCtor = 0xda9b0d0d;
        private const uint InitConnectionCtor = 0xc1cd5ea9;
        private const int TelegramLayer = 214;
        private const int LoginPoolSize = 1;
        private const int MaxRpcAttempts = 5;
        private const int IncorrectServerSaltErrorCode = 48;
        private const int IncorrectServerSaltRetryCount = 3;
        // Per-RPC native channel timeout. The native MtProtoChannel doesn't
        // surface its own deadline, so without this a missing/garbled wire
        // response would hang the await forever. 12s is enough for a normal
        // round-trip on a warm channel; cold handshakes happen during
        // EnsureConnectedAsync, not here.
        private static readonly TimeSpan NativeRpcTimeout = TimeSpan.FromSeconds(12);
        // TDLib×Android hybrid (see docs/network/auth-key-bootstrap.md):
        // staggered race against the same DC is degenerate — Telegram
        // serves DC#2 from an anycast cluster, so the 2nd and 3rd
        // candidate hit the same backend that didn't answer the 1st.
        // We keep ONE in-flight handshake per DC and walk endpoints
        // sequentially. The race scaffold below stays so the call sites
        // (PrewarmAuthKeyAsync, ConnectCoreAsync.race-then-sequential)
        // don't have to fork — MaxAuthKeyRaceCandidates = 1 collapses
        // the loop to a single attempt and the sequential fallback
        // immediately below picks up IP#2..N with exponential backoff.
        private static readonly TimeSpan AuthKeyRaceStagger = TimeSpan.FromMilliseconds(3000);
        private const int MaxAuthKeyRaceCandidates = 1;

        // Wall deadline for the FULL bootstrap (race + sequential fallback)
        // against a single DC. With 3 sequential attempts × 10 s DH timeout
        // + backoff ~3 s, we comfortably fit under 45 s and still leave the
        // inter-DC race wrapper (RunHybridDcBootstrapAsync) room to keep
        // both DCs running until one wins.
        private static readonly TimeSpan AuthKeyRaceWallDeadline = TimeSpan.FromSeconds(45);

        // Anonymous QR bootstrap also tries DC#1 in parallel with the
        // preferred DC#2. RunHybridDcBootstrapAsync launches both DCs as
        // sibling tasks and returns the first usable auth_key, cancelling
        // the loser. Empty array disables the parallel leg and falls back
        // to "DC#2 only", matching the conservative TDLib bootstrap.
        private static readonly int[] AnonymousQrFallbackDcIds = new[] { 1 };

        // Sequential bootstrap parameters (TDLib-style intra-DC walk).
        // After the race winner (currently always the first endpoint
        // because MaxRaceCandidates = 1) fails, we retry up to N more
        // distinct endpoints of the same DC, sleeping between attempts
        // so concurrent SYNs don't pile up on the emulator's TCP stack.
        // 3 retries × 10 s DH + ~1.5 s avg backoff ≈ 35 s — within the
        // wall deadline.
        private const int IntraDcSequentialRetries = 3;
        private static readonly TimeSpan InterEndpointBackoffInitial = TimeSpan.FromMilliseconds(500);
        private static readonly TimeSpan InterEndpointBackoffMax = TimeSpan.FromMilliseconds(2000);

        // Per-handshake DH timeout lives in TelegramDcOptions
        // .AuthKeyGenerationTimeout (16 s). TDLib uses 10 s — we keep
        // 16 s for slow networks but the wall cap above prevents
        // pathological pile-ups.

        // After N consecutive DH timeouts on the SAME DC we flip the
        // direct-dial framing from Intermediate (0xEEEEEEEE greeting —
        // a very distinctive 4-byte DPI signature) to Abridged
        // (0xEF — single byte, indistinguishable from random traffic
        // on the first packet). The native MTProto runtime config
        // (Vianigram.MTProto.MtProtoRuntime.SetDirectDialFraming)
        // exposes the switch; both AuthKeyGenerator and the
        // post-handshake EncryptedSession read it.
        private const int MaxConsecutiveDhTimeoutsBeforeFramingSwitch = 2;

        // Failure fast-track: after this many candidates have failed with
        // a transport-level error suggesting the local network has no
        // route to Telegram (HOST_UNREACHABLE / NETWORK_UNREACHABLE /
        // CONN_REFUSED), we bail rather than burn the per-candidate
        // deadline against more unreachable endpoints. With race
        // collapsed to 1 candidate this also short-circuits the
        // intra-DC sequential walk.
        private const int AuthKeyRaceHardFailureBailout = 3;

        // Account typed-method constructor ids.
        private const uint CtorBoolTrue = 0x997275b5u;
        private const uint CtorBoolFalse = 0xbc799737u;
        private const uint CtorInputUserSelf = 0xf7c1b13fu;
        private const uint CtorAuthExportLoginToken = 0xb7e085feu;
        private const uint CtorAuthImportLoginToken = 0x95ac5ce4u;
        private const uint CtorAuthLogOut = 0x3e72ba19u;
        // Cross-DC auth migration: see TlEncoder for the TL definitions.
        private const uint CtorAuthExportAuthorization = 0xe5bfffcdu;
        private const uint CtorAuthImportAuthorization = 0xa57a7dadu;
        private const uint CtorAuthExportedAuthorization = 0xb434e2b8u;
        private const uint CtorAuthLoginToken = 0x629f1980u;
        private const uint CtorAuthLoginTokenMigrateTo = 0x068e9916u;
        private const uint CtorAuthLoginTokenSuccess = 0x390d5c5eu;
        private const uint CtorUsersGetFullUser = 0xb60f5918u;
        private const uint CtorAccountUpdateProfile = 0x78515775u;
        private const uint CtorAccountCheckUsername = 0x2714d86cu;
        private const uint CtorUserA = 0x83314fcau;
        private const uint CtorUserB = 0x83314faeu;
        private const uint CtorUser214 = 0x020b1422u;
        private const uint CtorUserEmpty = 0xd3bc4b7au;

        private readonly IAuthKeyGeneratorPort _keyGen;
        private readonly IAuthKeyStore _authKeyStore;
        private readonly IPreferredDcStore _preferredDcStore;
        private readonly object _connectGate = new object();
        private readonly SemaphoreSlim _callLock = new SemaphoreSlim(1, 1);

        // Tracks DCs with a prewarm currently in flight. The TCS lets
        // both back-to-back PrewarmAuthKeyAsync callers deduplicate
        // (joining the existing task) AND lets ConnectCoreAsync await an
        // in-flight prewarm before kicking off its own DH handshake —
        // the migrate path that follows auth.loginTokenMigrateTo can
        // otherwise race the prewarm and end up doing two parallel
        // 12 s handshakes against the same DC.
        private readonly object _prewarmGate = new object();
        private readonly System.Collections.Generic.Dictionary<int, System.Threading.Tasks.TaskCompletionSource<bool>> _prewarmTasks =
            new System.Collections.Generic.Dictionary<int, System.Threading.Tasks.TaskCompletionSource<bool>>();
        private readonly Dictionary<int, ulong> _freshAnonymousPrewarmKeyIds =
            new Dictionary<int, ulong>();
        private readonly Dictionary<int, AccountAuthKeyRecord> _freshAnonymousPrewarmRecords =
            new Dictionary<int, AccountAuthKeyRecord>();

        // Keys that the server explicitly rejected with an MTProto
        // AUTH_KEY_* error during this process lifetime. Once an id lands
        // here we refuse to reuse the cached record under that id even if
        // it still loads from the store as "usable", since the server has
        // already proved it won't authorise it. The set is in-memory only
        // — on next launch the persistent store load wins again, which
        // is the right thing if e.g. the user's clock was wrong: the
        // first RPC will fail, the id lands here again, and the next
        // ConnectCore regenerates. Persisting this set would require a
        // schema bump; the current in-memory shape is the secure
        // conservative default.
        private readonly HashSet<ulong> _serverRejectedAuthKeyIds =
            new HashSet<ulong>();

        // Tracks whether the cached auth_key for a DC has already been
        // reused at least once in this process. The QR-anonymous reuse
        // policy permits a single reuse of an opaque cached key (we have
        // no proof the server still trusts it) per process — after that
        // the cycle becomes "reuse, verify by RPC, then it's trusted for
        // the rest of the session". The dictionary value is the UTC
        // timestamp of the first reuse; we currently only need presence
        // but the timestamp keeps the option open to expire it later.
        private readonly Dictionary<int, DateTime> _anonymousCacheReuseAt =
            new Dictionary<int, DateTime>();

        private Vianigram.MTProto.MtProtoChannel _channel;
        private Task<bool> _connectTask;
        private int _connectTaskDcId;
        private bool _connectTaskForceFreshKey;
        private int _connectGeneration;
        private int _currentDcId;
        private bool _connectionInitialized;
        private bool _disposed;

        // Tracks the auth_key the live channel is currently encrypting
        // frames with. Captured every time we transition to a new
        // _channel, cleared when the channel closes. Used by the QR
        // login handler to persist the truly-authorized key on
        // auth.loginTokenSuccess (the persistent store can drift from
        // this if a logout DeleteAsync wiped the slot or a concurrent
        // prewarm wrote a different key — see TryGetCurrentChannelAuthKey).
        private byte[] _currentChannelAuthKeyBytes;
        private ulong _currentChannelAuthKeyId;
        private long _currentChannelServerSalt;
        private int _currentChannelServerTimeOffset;
        private bool _qrAnonymousFreshKeyPrepared;
        private bool _qrForceFreshOnNextConnect;
        private int _qrForceFreshDcId;

        // One-shot guard for help.getConfig refresh after first
        // connection init. Set inside MarkConnectionInitialized via
        // Interlocked so we never dispatch the refresh twice in the
        // same process — the persisted dc_options list is good for the
        // entire lifetime of this launch, and a second call would just
        // burn an RPC round trip.
        private int _helpGetConfigDispatched;

        public AccountLoginMtProtoRpcPort(
            IAuthKeyGeneratorPort keyGen,
            IAuthKeyStore authKeyStore,
            int initialDcId,
            IPreferredDcStore preferredDcStore = null)
        {
            if (keyGen == null) throw new ArgumentNullException("keyGen");
            if (authKeyStore == null) throw new ArgumentNullException("authKeyStore");
            _keyGen = keyGen;
            _authKeyStore = authKeyStore;
            _preferredDcStore = preferredDcStore;
            _currentDcId = initialDcId > 0 ? initialDcId : TelegramAppConfig.ActiveDcId;
        }

        public int CurrentDcId
        {
            get
            {
                lock (_connectGate)
                {
                    return _currentDcId > 0 ? _currentDcId : TelegramAppConfig.ActiveDcId;
                }
            }
        }

        public int PreferDcForPhone(string phoneE164)
        {
            int hintedDcId = TelegramDcOptions.GuessLoginDcIdForPhone(phoneE164);
            if (hintedDcId <= 0)
            {
                return 0;
            }

            Vianigram.MTProto.MtProtoChannel channelToClose = null;
            int previousDcId;
            lock (_connectGate)
            {
                previousDcId = _currentDcId > 0 ? _currentDcId : TelegramAppConfig.ActiveDcId;
                if (_connectTask != null && !_connectTask.IsCompleted)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "phone login DC hint ignored while connect in-flight: phone=" +
                        phoneE164 + " hinted_dc=" + hintedDcId +
                        " current_dc=" + previousDcId);
                    return 0;
                }

                if (previousDcId != hintedDcId)
                {
                    channelToClose = _channel;
                    _channel = null;
                    _connectionInitialized = false;
                    _currentDcId = hintedDcId;
                }
            }

            if (channelToClose != null)
            {
                try { channelToClose.Close(); }
                catch (Exception) { }
            }

            RememberLoginDcHint(hintedDcId, "phone-prefix");
            EarlyLog.Write(
                "Account.LoginMTProto",
                "phone login DC hint: phone=" + phoneE164 +
                " dc=" + hintedDcId +
                " previous=" + previousDcId +
                (previousDcId == hintedDcId ? " (already-selected)" : " (switched)"));
            return hintedDcId;
        }

        /// <summary>
        /// Follow a server-issued <c>auth.loginTokenMigrateTo</c> by
        /// retargeting the unauthenticated channel to <paramref name="dcId"/>.
        /// Mirrors the in-flight guard / channel close logic of
        /// <see cref="PreferDcForPhone"/>; the next RPC will open a fresh
        /// channel (and generate or load the auth_key) on the new DC.
        /// </summary>
        public bool SwitchDcForQrMigration(int dcId)
        {
            if (dcId <= 0) return false;

            Vianigram.MTProto.MtProtoChannel channelToClose = null;
            int previousDcId;
            lock (_connectGate)
            {
                previousDcId = _currentDcId > 0 ? _currentDcId : TelegramAppConfig.ActiveDcId;
                if (_connectTask != null && !_connectTask.IsCompleted)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "QR migrate ignored while connect in-flight: target=" + dcId +
                        " current=" + previousDcId);
                    return false;
                }

                if (previousDcId != dcId)
                {
                    channelToClose = _channel;
                    _channel = null;
                    _connectionInitialized = false;
                    _currentDcId = dcId;
                    _qrForceFreshOnNextConnect = true;
                    _qrForceFreshDcId = dcId;
                }
            }

            if (channelToClose != null)
            {
                try { channelToClose.Close(); }
                catch (Exception) { }
            }

            RememberLoginDcHint(dcId, "qr-migrate");
            EarlyLog.Write(
                "Account.LoginMTProto",
                "QR migrate DC switch: " + previousDcId + " -> " + dcId +
                (previousDcId == dcId ? " (already-selected)" : " (switched)"));
            return true;
        }

        /// <summary>
        /// Restore the QR-login transport to the anonymous login DC after a
        /// failed post-migrate import. This keeps the refresh loop from
        /// getting stuck exporting fresh QR tokens from the failed migrate DC.
        /// </summary>
        public void ResetQrMigrationAfterFailure()
        {
            int previousDcId = CurrentDcId;
            int anonymousDcId = TelegramAppConfig.ActiveDcId;

            CloseCurrentChannel();
            lock (_connectGate)
            {
                _currentDcId = anonymousDcId;
                _connectionInitialized = false;
                _qrAnonymousFreshKeyPrepared = false;
                _qrForceFreshOnNextConnect = false;
                _qrForceFreshDcId = 0;
                _connectTask = null;
                _connectTaskDcId = 0;
                _connectTaskForceFreshKey = false;
                unchecked { _connectGeneration++; }
            }

            RememberLoginDcHint(anonymousDcId, "qr-migrate-reset");
            EarlyLog.Write(
                "Account.LoginMTProto",
                "QR migrate reset after failed import: " +
                previousDcId + " -> " + anonymousDcId);
        }

        /// <summary>
        /// Returns the auth_key the live channel for <paramref name="dcId"/>
        /// is currently using, or null if no channel is open for that DC.
        /// See the interface doc for why this is needed (store-vs-channel
        /// drift after logout + prewarm regeneration).
        /// </summary>
        public AccountAuthKeyRecord TryGetCurrentChannelAuthKey(int dcId)
        {
            if (dcId <= 0) return null;
            lock (_connectGate)
            {
                if (_channel == null || _currentDcId != dcId) return null;
                if (_currentChannelAuthKeyBytes == null ||
                    _currentChannelAuthKeyBytes.Length == 0 ||
                    _currentChannelAuthKeyId == 0)
                {
                    return null;
                }
                return new AccountAuthKeyRecord
                {
                    AuthKey = (byte[])_currentChannelAuthKeyBytes.Clone(),
                    AuthKeyId = _currentChannelAuthKeyId,
                    ServerSalt = _currentChannelServerSalt,
                    ServerTimeOffset = _currentChannelServerTimeOffset
                };
            }
        }

        /// <summary>
        /// Pre-warm the auth_key cache for <paramref name="dcId"/> without
        /// changing the current channel or DC. The QR-login VM fires this
        /// shortly after the page opens so that the most likely migrate
        /// destination already has a generated auth_key by the time the
        /// user scans. Skips immediately if a usable key is already
        /// cached, or if a prewarm for the same DC is already in flight
        /// (so back-to-back navigations don't double-handshake), or if a
        /// channel is already open for the DC (the in-memory key is the
        /// truth and a new keygen would diverge from it — the exact
        /// scenario that produced AUTH_KEY_UNREGISTERED on next launch).
        /// </summary>
        public async Task PrewarmAuthKeyAsync(int dcId, CancellationToken ct)
        {
            if (dcId <= 0) return;
            // Renamed from `forceFreshForAnonymousQr`. The flag now means
            // "the caller is an anonymous QR session" (so we should mark
            // any usable key for the QR start path) — NOT "always
            // regenerate". The actual regenerate decision is made below
            // via ShouldForceFreshAuthKey, which is evidence-based.
            bool callerWantsAnonymousQr = IsAnonymousQrContext();

            // Hard guard: if the unauthenticated channel is already open
            // for this DC, the store MUST stay in sync with that
            // channel's in-memory key. A prewarm here would generate a
            // different auth_key, save it, and silently corrupt the
            // store — the server would later authorise the channel's
            // key (because that's what encrypts the export call) and
            // we'd persist the prewarm's unauthorised key by mistake.
            lock (_connectGate)
            {
                if (_channel != null && _currentDcId == dcId)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " skipped (channel already open for this DC)");
                    return;
                }
            }

            System.Threading.Tasks.TaskCompletionSource<bool> tcs;
            System.Threading.Tasks.TaskCompletionSource<bool> joinExisting = null;
            lock (_prewarmGate)
            {
                System.Threading.Tasks.TaskCompletionSource<bool> existingTcs;
                if (_prewarmTasks.TryGetValue(dcId, out existingTcs) && !existingTcs.Task.IsCompleted)
                {
                    joinExisting = existingTcs;
                    tcs = null;
                }
                else
                {
                    tcs = new System.Threading.Tasks.TaskCompletionSource<bool>();
                    _prewarmTasks[dcId] = tcs;
                }
            }

            if (joinExisting != null)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "Prewarm DC#" + dcId + " joining in-flight task");
                // Reuse the running prewarm — both callers see the
                // same completion signal.
                try { await joinExisting.Task.ConfigureAwait(false); }
                catch { }
                return;
            }

            try
            {
                AccountAuthKeyRecord existing = null;
                try
                {
                    existing = await _authKeyStore.LoadAsync(dcId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " load threw: " +
                        ex.GetType().Name + ": " + ex.Message);
                }

                bool forceFresh = ShouldForceFreshAuthKey(existing, callerRequestedFresh: false);

                if (!forceFresh && existing != null)
                {
                    // Cache-first reuse: the record is structurally valid
                    // and not on the in-session server-rejection list, so
                    // we trust it for now. The first wire RPC will
                    // implicitly verify; if the server rejects with an
                    // AUTH_KEY_* error, MarkAuthKeyServerRejected lands
                    // the id in the blacklist and the next prewarm /
                    // connect falls into the regenerate branch below.
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId +
                        " skipped (cached key trusted: keyId=0x" +
                        existing.AuthKeyId.ToString("x16") + ")");

                    if (callerWantsAnonymousQr)
                    {
                        // Make the cached key visible to the QR start
                        // path under the same "fresh prewarm" marker the
                        // old force-regen flow used, so that path can
                        // skip its own EnsureConnectedAsync(forceFresh:true).
                        MarkAnonymousCacheReuse(dcId);
                        try
                        {
                            MarkFreshAnonymousPrewarm(dcId, existing);
                        }
                        catch (Exception ex)
                        {
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "Prewarm DC#" + dcId +
                                " mark-fresh-from-cache threw: " +
                                ex.GetType().Name + ": " + ex.Message);
                        }
                    }

                    ClearAuthKeyRecord(existing);
                    return;
                }

                if (existing != null)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId +
                        " regenerating (force-fresh policy triggered: " +
                        (IsUsableAuthKeyRecord(existing)
                            ? "server-rejected or caller-requested"
                            : "structurally unusable") +
                        ", keyId=0x" + existing.AuthKeyId.ToString("x16") + ")");
                    try
                    {
                        await _authKeyStore.DeleteAsync(dcId, ct).ConfigureAwait(false);
                    }
                    catch
                    {
                    }
                    // Stale / corrupt cache entry — clear before regenerating.
                    ClearAuthKeyRecord(existing);
                }

                TelegramDcEndpoint[] endpoints = GetLoginConnectionPlan(dcId);
                if (endpoints.Length == 0)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " skipped (no endpoint plan)");
                    return;
                }

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "Prewarm DC#" + dcId + " race begin candidates=" +
                    Math.Min(MaxAuthKeyRaceCandidates, endpoints.Length) +
                    " plan=" + TelegramDcOptions.DescribePlan(endpoints));
                var sw = Stopwatch.StartNew();

                Result<AccountAuthKeyRecord, AccountError> keyResult;
                AuthKeyRaceResult raceResult = null;
                try
                {
                    raceResult = await GenerateAuthKeyWithEndpointRaceAsync(endpoints, ct)
                        .ConfigureAwait(false);
                    keyResult = raceResult != null
                        ? raceResult.KeyResult
                        : Result<AccountAuthKeyRecord, AccountError>.Fail(
                            AccountError.NetworkError("no auth_key race result"));
                }
                catch (OperationCanceledException)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " cancelled");
                    return;
                }
                catch (Exception ex)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " threw: " +
                        ex.GetType().Name + ": " + ex.Message);
                    return;
                }
                sw.Stop();

                if (keyResult.IsFail || keyResult.Value == null ||
                    !IsUsableAuthKeyRecord(keyResult.Value))
                {
                    string detail = keyResult.IsFail && keyResult.Error != null
                        ? keyResult.Error.ToString()
                        : "no key";
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " FAILED elapsed=" + sw.ElapsedMilliseconds +
                        "ms detail=" + detail);
                    return;
                }

                if (raceResult != null && raceResult.Endpoint != null)
                {
                    TelegramDcOptions.ReportEndpointSuccess(raceResult.Endpoint);
                    RememberLoginEndpoint(raceResult.Endpoint, "prewarm");
                }

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "Prewarm DC#" + dcId +
                    " race winner=" + (raceResult != null && raceResult.Endpoint != null ? raceResult.Endpoint.ToString() : "(unknown)") +
                    " winner_ms=" + (raceResult != null ? raceResult.ElapsedMs : sw.ElapsedMilliseconds) +
                    " total_ms=" + sw.ElapsedMilliseconds);

                try
                {
                    await _authKeyStore.SaveAsync(dcId, keyResult.Value, ct).ConfigureAwait(false);
                    if (callerWantsAnonymousQr)
                    {
                        MarkFreshAnonymousPrewarm(dcId, keyResult.Value);
                    }
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " saved elapsed=" +
                        sw.ElapsedMilliseconds + "ms keyId=0x" +
                        keyResult.Value.AuthKeyId.ToString("x16"));
                }
                catch (Exception ex)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " save threw: " +
                        ex.GetType().Name + ": " + ex.Message);
                }
                finally
                {
                    ClearAuthKeyRecord(keyResult.Value);
                }
            }
            finally
            {
                // Release any ConnectCoreAsync waiter — even on
                // failure / cancellation, since the waiter will fall
                // back to its own keygen when the cache is still empty.
                lock (_prewarmGate)
                {
                    System.Threading.Tasks.TaskCompletionSource<bool> current;
                    if (_prewarmTasks.TryGetValue(dcId, out current) &&
                        object.ReferenceEquals(current, tcs))
                    {
                        _prewarmTasks.Remove(dcId);
                    }
                }
                try { tcs.TrySetResult(true); } catch { }
            }
        }

        public async Task WarmUpAsync(CancellationToken ct)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                EarlyLog.Write("Account.LoginMTProto", "warm-up begin");
                bool opened = await EnsureConnectedAsync(CurrentDcId, false, ct).ConfigureAwait(false);
                sw.Stop();
                if (opened)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "warm-up complete elapsed=" +
                        sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        "ms dc=" + CurrentDcId + " initialized=False");
                }
                else
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "warm-up failed elapsed=" +
                        sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
                }
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "warm-up cancelled elapsed=" +
                    sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "warm-up threw elapsed=" +
                    sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "ms " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        async Task<Result<byte[], MtProtoRpcError>> IMtProtoRpcPort.CallAsync(
            byte[] requestBytes,
            CancellationToken ct)
        {
            bool isLogout = IsRequestCtor(requestBytes, CtorAuthLogOut);
            CallOutcome outcome;
            try
            {
                outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            }
            finally
            {
                if (isLogout)
                {
                    ResetAfterLogout();
                }
            }

            if (outcome.Ok)
            {
                return Result<byte[], MtProtoRpcError>.Ok(outcome.Bytes);
            }

            var err = new MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], MtProtoRpcError>.Fail(err);
        }

        async Task<Result<QrTokenResponse, AccountError>> IMtProtoRpcPort.AuthExportLoginTokenAsync(
            int apiId,
            string apiHash,
            CancellationToken ct)
        {
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAuthExportLoginToken)
                .WriteInt32(apiId)
                .WriteString(apiHash ?? string.Empty)
                .WriteVector<long>(new long[0], WriteLong)
                .ToArray();

            bool qrStartReady = await EnsureFreshAnonymousQrStartAsync(ct).ConfigureAwait(false);
            if (!qrStartReady)
            {
                return Result<QrTokenResponse, AccountError>.Fail(
                    AccountError.NetworkError(
                        "login connection could not be opened: all Telegram bootstrap endpoints timed out or were unreachable"));
            }

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                // SESSION_PASSWORD_NEEDED can come back as an rpc error on
                // exportLoginToken once the other device approves the QR
                // for an account that has 2FA enabled. Surface as
                // TwoFaRequired so the handler triggers the SRP flow.
                string m = outcome.Message ?? string.Empty;
                if (m.IndexOf("SESSION_PASSWORD_NEEDED", StringComparison.Ordinal) >= 0)
                {
                    var dto = new QrTokenResponse();
                    dto.Kind = QrPollKind.TwoFaRequired;
                    return Result<QrTokenResponse, AccountError>.Ok(dto);
                }
                return Result<QrTokenResponse, AccountError>.Fail(MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorAuthLoginToken)
                {
                    var dto = new QrTokenResponse();
                    dto.Kind = QrPollKind.Pending;
                    dto.ExpiresUnixSeconds = r.ReadInt32();
                    dto.Token = r.ReadBytes();
                    return Result<QrTokenResponse, AccountError>.Ok(dto);
                }
                if (ctor == CtorAuthLoginTokenMigrateTo)
                {
                    var dto = new QrTokenResponse();
                    dto.Kind = QrPollKind.MigrateTo;
                    dto.MigrateDcId = r.ReadInt32();
                    dto.MigrateToken = r.ReadBytes();
                    return Result<QrTokenResponse, AccountError>.Ok(dto);
                }
                if (ctor == CtorAuthLoginTokenSuccess)
                {
                    var dto = new QrTokenResponse();
                    dto.Kind = QrPollKind.Accepted;
                    int bodyLen = outcome.Bytes.Length - 4;
                    if (bodyLen > 0)
                    {
                        var auth = new byte[bodyLen];
                        Buffer.BlockCopy(outcome.Bytes, 4, auth, 0, bodyLen);
                        dto.AuthorizationBytes = auth;
                    }
                    return Result<QrTokenResponse, AccountError>.Ok(dto);
                }
                return Result<QrTokenResponse, AccountError>.Fail(
                    AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<QrTokenResponse, AccountError>.Fail(
                    AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        private async Task<bool> EnsureFreshAnonymousQrStartAsync(CancellationToken ct)
        {
            int targetDcId;
            bool hasOpenChannel;
            lock (_connectGate)
            {
                if (_qrAnonymousFreshKeyPrepared)
                {
                    return true;
                }

                hasOpenChannel = _channel != null;
                targetDcId = _currentDcId > 0 ? _currentDcId : TelegramAppConfig.ActiveDcId;
            }

            long persistedUserId = 0L;
            try
            {
                if (_preferredDcStore != null)
                {
                    persistedUserId = _preferredDcStore.GetUserId();
                }
            }
            catch
            {
                persistedUserId = 0L;
            }

            if (persistedUserId > 0L)
            {
                lock (_connectGate)
                {
                    _qrAnonymousFreshKeyPrepared = true;
                }
                return true;
            }

            // Cache-first QR start. Previously this path force-regenerated
            // the auth_key for every anonymous QR session, which pushed
            // the cold-start handshake against 9 racing endpoints and
            // routinely consumed 25-35 s of wall time before falling back
            // to the same cached key that was already on disk. New
            // policy: pass forceFreshKey: false and let ConnectCoreAsync
            // run its load-from-store path; ShouldForceFreshAuthKey
            // there decides whether the cached key is trustworthy. If a
            // future wire RPC returns AUTH_KEY_*, MarkAuthKeyServerRejected
            // blacklists the id and the next ConnectCore call regenerates.
            bool hasFreshPrewarm = HasFreshAnonymousPrewarm(targetDcId);
            EarlyLog.Write(
                "Account.LoginMTProto",
                "QR anonymous start: " +
                (hasFreshPrewarm
                    ? "using fresh prewarm for DC#"
                    : "deferring to cache-first policy for DC#") +
                targetDcId +
                (hasOpenChannel ? " (dropping stale channel)" : string.Empty));
            // forceFreshKey: false — passes positional to keep WP 8.1
            // C# 5 compiler happy (no named-then-positional). See
            // ShouldForceFreshAuthKey for what controls regeneration
            // inside ConnectCoreAsync.
            bool opened = await EnsureConnectedAsync(
                targetDcId,
                false,
                ct).ConfigureAwait(false);
            if (!opened && targetDcId == TelegramAppConfig.ActiveDcId)
            {
                opened = await TryOpenAnonymousQrFallbackAsync(targetDcId, ct).ConfigureAwait(false);
                if (opened)
                {
                    targetDcId = CurrentDcId;
                }
            }

            if (opened)
            {
                AccountAuthKeyRecord currentKey = TryGetCurrentChannelAuthKey(targetDcId);
                if (currentKey != null)
                {
                    try
                    {
                        MarkFreshAnonymousPrewarm(targetDcId, currentKey);
                    }
                    finally
                    {
                        ClearAuthKeyRecord(currentKey);
                    }
                }
                else
                {
                    ulong keyId = GetCurrentChannelAuthKeyId(targetDcId);
                    if (keyId != 0UL)
                    {
                        MarkFreshAnonymousPrewarm(targetDcId, keyId);
                    }
                }
                lock (_connectGate)
                {
                    _qrAnonymousFreshKeyPrepared = true;
                }
            }

            return opened;
        }

        private async Task<bool> TryOpenAnonymousQrFallbackAsync(int failedDcId, CancellationToken ct)
        {
            for (int i = 0; i < AnonymousQrFallbackDcIds.Length; i++)
            {
                int fallbackDcId = AnonymousQrFallbackDcIds[i];
                if (fallbackDcId <= 0 || fallbackDcId == failedDcId)
                {
                    continue;
                }

                if (!SelectAnonymousQrDc(fallbackDcId, "qr-anonymous-fallback"))
                {
                    continue;
                }

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "QR anonymous fallback: DC#" + failedDcId +
                    " failed; trying DC#" + fallbackDcId);

                bool opened = await EnsureConnectedAsync(fallbackDcId, false, ct).ConfigureAwait(false);
                if (opened)
                {
                    RememberLoginDcHint(fallbackDcId, "qr-anonymous-fallback");
                    return true;
                }

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "QR anonymous fallback DC#" + fallbackDcId + " failed");
            }

            SelectAnonymousQrDc(failedDcId, "qr-anonymous-fallback-reset");
            return false;
        }

        private bool SelectAnonymousQrDc(int dcId, string reason)
        {
            if (dcId <= 0) return false;

            Vianigram.MTProto.MtProtoChannel channelToClose = null;
            int previousDcId;
            lock (_connectGate)
            {
                previousDcId = _currentDcId > 0 ? _currentDcId : TelegramAppConfig.ActiveDcId;
                if (_connectTask != null && !_connectTask.IsCompleted)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "anonymous QR DC select ignored while connect in-flight: target=" +
                        dcId + " current=" + previousDcId +
                        " reason=" + (reason ?? string.Empty));
                    return false;
                }

                if (previousDcId != dcId)
                {
                    channelToClose = _channel;
                    _channel = null;
                    _connectionInitialized = false;
                    _currentDcId = dcId;
                    _qrAnonymousFreshKeyPrepared = false;
                    _qrForceFreshOnNextConnect = false;
                    _qrForceFreshDcId = 0;
                    _connectTask = null;
                    _connectTaskDcId = 0;
                    _connectTaskForceFreshKey = false;
                    unchecked { _connectGeneration++; }
                }
            }

            if (channelToClose != null)
            {
                try { channelToClose.Close(); }
                catch (Exception) { }
            }

            EarlyLog.Write(
                "Account.LoginMTProto",
                "anonymous QR DC select: " + previousDcId + " -> " + dcId +
                " reason=" + (reason ?? string.Empty) +
                (previousDcId == dcId ? " (already-selected)" : " (switched)"));
            return true;
        }

        private bool IsAnonymousQrContext()
        {
            try
            {
                return _preferredDcStore == null || _preferredDcStore.GetUserId() <= 0L;
            }
            catch
            {
                return true;
            }
        }

        // Evidence-based force-fresh policy. Replaces the old rule
        // "anonymous QR session => always regenerate" with one that
        // regenerates ONLY when there is a concrete signal that the
        // cached key is unsafe to reuse. The change is the difference
        // between a sub-second warm start and a 30+ second cold race
        // through dead endpoints; the security argument is in
        // docs/security/auth-key-reuse-policy.md.
        //
        // forceFresh = true if any of:
        //   (a) no record cached for the DC, OR
        //   (b) the record fails IsUsableAuthKeyRecord (length/id/zero
        //       checks), OR
        //   (c) the record's key-id was server-rejected (AUTH_KEY_*) in
        //       this process lifetime, OR
        //   (d) caller explicitly requested a regenerate (e.g. the
        //       post-timeout retry path).
        // Otherwise the cached key is reused. The anonymous QR flow then
        // verifies the key with the very first encrypted RPC; if the
        // server rejects, the id lands in _serverRejectedAuthKeyIds and
        // the next ConnectCore call will fall into branch (c).
        private bool ShouldForceFreshAuthKey(AccountAuthKeyRecord existing, bool callerRequestedFresh)
        {
            if (callerRequestedFresh)
            {
                return true;
            }

            if (existing == null)
            {
                return true;
            }

            if (!IsUsableAuthKeyRecord(existing))
            {
                return true;
            }

            lock (_prewarmGate)
            {
                if (_serverRejectedAuthKeyIds.Contains(existing.AuthKeyId))
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "auth_key reuse denied: keyId=0x" +
                        existing.AuthKeyId.ToString("x16") +
                        " was server-rejected this session");
                    return true;
                }
            }

            return false;
        }

        // Records that the server has explicitly rejected this auth_key
        // (e.g. AUTH_KEY_UNREGISTERED, AUTH_KEY_INVALID,
        // AUTH_KEY_DUPLICATED, AUTH_KEY_PERM_EMPTY). Any future
        // ShouldForceFreshAuthKey check that sees this id will force
        // a regenerate. Call this from the RPC error path whenever the
        // wire response signals the key itself is bad — NOT for
        // unrelated network errors. Caller is responsible for ALSO
        // deleting the persistent record where appropriate.
        internal void MarkAuthKeyServerRejected(ulong authKeyId, string reason)
        {
            if (authKeyId == 0UL) return;
            lock (_prewarmGate)
            {
                _serverRejectedAuthKeyIds.Add(authKeyId);
            }
            EarlyLog.Write(
                "Account.LoginMTProto",
                "auth_key blacklist: keyId=0x" + authKeyId.ToString("x16") +
                " reason=" + (reason ?? "(unspecified)"));
        }

        // Records that the anonymous reuse policy granted reuse of the
        // cached key for this DC. Currently informational — feeds the
        // telemetry / log narrative — but ready to back a TTL policy
        // when the persistent trust-state migration lands.
        private void MarkAnonymousCacheReuse(int dcId)
        {
            if (dcId <= 0) return;
            lock (_prewarmGate)
            {
                _anonymousCacheReuseAt[dcId] = DateTime.UtcNow;
            }
        }

        private bool HasFreshAnonymousPrewarm(int dcId)
        {
            lock (_prewarmGate)
            {
                return _freshAnonymousPrewarmKeyIds.ContainsKey(dcId);
            }
        }

        private bool HasPrewarmInFlight(int dcId)
        {
            lock (_prewarmGate)
            {
                System.Threading.Tasks.TaskCompletionSource<bool> tcs;
                return _prewarmTasks.TryGetValue(dcId, out tcs) && !tcs.Task.IsCompleted;
            }
        }

        private ulong GetFreshAnonymousPrewarmKeyId(int dcId)
        {
            lock (_prewarmGate)
            {
                ulong keyId;
                return _freshAnonymousPrewarmKeyIds.TryGetValue(dcId, out keyId) ? keyId : 0UL;
            }
        }

        private AccountAuthKeyRecord CloneFreshAnonymousPrewarm(int dcId)
        {
            lock (_prewarmGate)
            {
                AccountAuthKeyRecord record;
                if (!_freshAnonymousPrewarmRecords.TryGetValue(dcId, out record))
                {
                    return null;
                }

                return CloneAuthKeyRecord(record);
            }
        }

        private void MarkFreshAnonymousPrewarm(int dcId, ulong authKeyId)
        {
            if (dcId <= 0 || authKeyId == 0UL) return;
            lock (_prewarmGate)
            {
                _freshAnonymousPrewarmKeyIds[dcId] = authKeyId;
            }
        }

        private void MarkFreshAnonymousPrewarm(int dcId, AccountAuthKeyRecord record)
        {
            if (dcId <= 0 || !IsUsableAuthKeyRecord(record)) return;
            AccountAuthKeyRecord clone = CloneAuthKeyRecord(record);
            lock (_prewarmGate)
            {
                AccountAuthKeyRecord old;
                if (_freshAnonymousPrewarmRecords.TryGetValue(dcId, out old))
                {
                    ClearAuthKeyRecord(old);
                }
                _freshAnonymousPrewarmRecords[dcId] = clone;
                _freshAnonymousPrewarmKeyIds[dcId] = record.AuthKeyId;
            }
        }

        private void ClearFreshAnonymousPrewarm(int dcId)
        {
            lock (_prewarmGate)
            {
                _freshAnonymousPrewarmKeyIds.Remove(dcId);
                AccountAuthKeyRecord old;
                if (_freshAnonymousPrewarmRecords.TryGetValue(dcId, out old))
                {
                    ClearAuthKeyRecord(old);
                    _freshAnonymousPrewarmRecords.Remove(dcId);
                }
            }
        }

        private ulong GetCurrentChannelAuthKeyId(int dcId)
        {
            lock (_connectGate)
            {
                if (_channel == null || _currentDcId != dcId) return 0UL;
                return _currentChannelAuthKeyId;
            }
        }

        async Task<Result<QrPollResponse, AccountError>> IMtProtoRpcPort.AuthImportLoginTokenAsync(
            byte[] token,
            CancellationToken ct)
        {
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAuthImportLoginToken)
                .WriteBytes(token ?? new byte[0])
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                string m = outcome.Message ?? string.Empty;
                if (m.IndexOf("SESSION_PASSWORD_NEEDED", StringComparison.Ordinal) >= 0)
                {
                    var dto = new QrPollResponse();
                    dto.Kind = QrPollKind.TwoFaRequired;
                    return Result<QrPollResponse, AccountError>.Ok(dto);
                }
                if (m.IndexOf("AUTH_TOKEN_EXPIRED", StringComparison.Ordinal) >= 0 ||
                    m.IndexOf("AUTH_TOKEN_INVALID", StringComparison.Ordinal) >= 0)
                {
                    var dto = new QrPollResponse();
                    dto.Kind = QrPollKind.Expired;
                    return Result<QrPollResponse, AccountError>.Ok(dto);
                }
                return Result<QrPollResponse, AccountError>.Fail(MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                var poll = new QrPollResponse();
                if (ctor == CtorAuthLoginTokenSuccess)
                {
                    poll.Kind = QrPollKind.Accepted;
                    // Slice off the leading loginTokenSuccess constructor and
                    // pass the embedded auth.authorization sub-tree to the
                    // handler so it can decode the user id with the shared
                    // TlDecoder.DecodeAuthorization projector.
                    int bodyLen = outcome.Bytes.Length - 4;
                    if (bodyLen > 0)
                    {
                        var auth = new byte[bodyLen];
                        Buffer.BlockCopy(outcome.Bytes, 4, auth, 0, bodyLen);
                        poll.AuthorizationBytes = auth;
                    }
                    return Result<QrPollResponse, AccountError>.Ok(poll);
                }
                if (ctor == CtorAuthLoginToken)
                {
                    poll.Kind = QrPollKind.Pending;
                    return Result<QrPollResponse, AccountError>.Ok(poll);
                }
                if (ctor == CtorAuthLoginTokenMigrateTo)
                {
                    poll.Kind = QrPollKind.MigrateTo;
                    poll.MigrateDcId = r.ReadInt32();
                    poll.MigrateToken = r.ReadBytes();
                    return Result<QrPollResponse, AccountError>.Ok(poll);
                }
                return Result<QrPollResponse, AccountError>.Fail(
                    AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<QrPollResponse, AccountError>.Fail(
                    AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<UserFullResponse, AccountError>> IMtProtoRpcPort.UsersGetFullUserAsync(
            InputUserSelf self,
            CancellationToken ct)
        {
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorUsersGetFullUser)
                .WriteUInt32(CtorInputUserSelf)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<UserFullResponse, AccountError>.Fail(MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                r.ReadUInt32();
                UserFullResponse dto = TryProjectSelfFromUserSlice(outcome.Bytes);
                if (dto == null)
                {
                    return Result<UserFullResponse, AccountError>.Fail(
                        AccountError.Unknown("users.userFull: could not locate self user record"));
                }
                return Result<UserFullResponse, AccountError>.Ok(dto);
            }
            catch (Exception ex)
            {
                return Result<UserFullResponse, AccountError>.Fail(
                    AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Unit, AccountError>> IMtProtoRpcPort.AccountUpdateProfileAsync(
            string firstName,
            string lastName,
            string about,
            CancellationToken ct)
        {
            int flags = 0;
            if (firstName != null) flags |= 1 << 0;
            if (lastName != null) flags |= 1 << 1;
            if (about != null) flags |= 1 << 2;

            var b = new TlByteBuilder()
                .WriteUInt32(CtorAccountUpdateProfile)
                .WriteInt32(flags);
            if (firstName != null) b.WriteString(firstName);
            if (lastName != null) b.WriteString(lastName);
            if (about != null) b.WriteString(about);

            CallOutcome outcome = await CallInternalAsync(b.ToArray(), ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<Unit, AccountError>.Fail(MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorUserA || ctor == CtorUserB || ctor == CtorUserEmpty)
                {
                    return Result<Unit, AccountError>.Ok(Unit.Value);
                }
                return Result<Unit, AccountError>.Fail(
                    AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<bool, AccountError>> IMtProtoRpcPort.AccountCheckUsernameAsync(
            string username,
            CancellationToken ct)
        {
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAccountCheckUsername)
                .WriteString(username ?? string.Empty)
                .ToArray();

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return Result<bool, AccountError>.Fail(MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor == CtorBoolTrue) return Result<bool, AccountError>.Ok(true);
                if (ctor == CtorBoolFalse) return Result<bool, AccountError>.Ok(false);
                return Result<bool, AccountError>.Fail(
                    AccountError.Unknown("unexpected response ctor 0x" + ctor.ToString("x8")));
            }
            catch (Exception ex)
            {
                return Result<bool, AccountError>.Fail(
                    AccountError.Unknown("decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<ExportedAuthorizationResponse, AccountError>>
            IMtProtoRpcPort.AuthExportAuthorizationAsync(int targetDcId, CancellationToken ct)
        {
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAuthExportAuthorization)
                .WriteInt32(targetDcId)
                .ToArray();

            EarlyLog.Write(
                "Account.LoginMTProto",
                "auth.exportAuthorization begin targetDc=" + targetDcId);

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "auth.exportAuthorization FAILED kind=" + (outcome.Kind ?? "(none)") +
                    " msg=\"" + (outcome.Message ?? string.Empty) + "\"");
                return Result<ExportedAuthorizationResponse, AccountError>.Fail(MapOutcomeToAccountError(outcome));
            }

            try
            {
                var r = new TlByteReader(outcome.Bytes);
                uint ctor = r.ReadUInt32();
                if (ctor != CtorAuthExportedAuthorization)
                {
                    return Result<ExportedAuthorizationResponse, AccountError>.Fail(
                        AccountError.Unknown(
                            "auth.exportAuthorization unexpected ctor 0x" + ctor.ToString("x8")));
                }
                long id = r.ReadInt64();
                byte[] bytes = r.ReadBytes();
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "auth.exportAuthorization OK targetDc=" + targetDcId +
                    " id=" + id + " blobLen=" + (bytes == null ? 0 : bytes.Length));
                return Result<ExportedAuthorizationResponse, AccountError>.Ok(
                    new ExportedAuthorizationResponse { Id = id, Bytes = bytes ?? new byte[0] });
            }
            catch (Exception ex)
            {
                return Result<ExportedAuthorizationResponse, AccountError>.Fail(
                    AccountError.Unknown("auth.exportAuthorization decode failed: " + ex.Message, ex));
            }
        }

        async Task<Result<Unit, AccountError>>
            IMtProtoRpcPort.AuthImportAuthorizationAsync(long id, byte[] bytes, CancellationToken ct)
        {
            byte[] req = new TlByteBuilder()
                .WriteUInt32(CtorAuthImportAuthorization)
                .WriteInt64(id)
                .WriteBytes(bytes ?? new byte[0])
                .ToArray();

            EarlyLog.Write(
                "Account.LoginMTProto",
                "auth.importAuthorization begin id=" + id +
                " blobLen=" + (bytes == null ? 0 : bytes.Length));

            CallOutcome outcome = await CallInternalAsync(req, ct).ConfigureAwait(false);
            if (!outcome.Ok)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "auth.importAuthorization FAILED kind=" + (outcome.Kind ?? "(none)") +
                    " msg=\"" + (outcome.Message ?? string.Empty) + "\"");
                return Result<Unit, AccountError>.Fail(MapOutcomeToAccountError(outcome));
            }

            // The server returns auth.authorization on success. We don't need
            // to decode the user — the side effect (peer DC session is now
            // authorised) is the success signal.
            EarlyLog.Write(
                "Account.LoginMTProto",
                "auth.importAuthorization OK id=" + id +
                " responseCtor=0x" + PeekCtor(outcome.Bytes).ToString("x8"));
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _callLock.Dispose();
            CloseCurrentChannel();
        }

        private async Task<CallOutcome> CallInternalAsync(byte[] requestBytes, CancellationToken ct)
        {
            if (requestBytes == null) throw new ArgumentNullException("requestBytes");

            await _callLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await CallInternalUnlockedAsync(requestBytes, ct).ConfigureAwait(false);
            }
            finally
            {
                _callLock.Release();
            }
        }

        private async Task<CallOutcome> CallInternalUnlockedAsync(byte[] requestBytes, CancellationToken ct)
        {
            bool retriedConnectionNotInited = false;
            bool retriedTransientSameKey = false;
            bool retriedTransientFreshKey = false;
            bool retriedAuthKey = false;
            bool retriedAuthRestart = false;
            int saltRetries = 0;

            uint topCtor = PeekCtor(requestBytes);
            EarlyLog.Write(
                "Account.LoginMTProto",
                "CallInternal begin ctor=0x" + topCtor.ToString("x8") +
                " size=" + (requestBytes != null ? requestBytes.Length : 0) +
                " maxAttempts=" + MaxRpcAttempts +
                " dc=" + CurrentDcId +
                " connInited=" + IsConnectionInitialized());

            for (int attempt = 0; attempt < MaxRpcAttempts; attempt++)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "attempt #" + (attempt + 1) + " ensure-connect dc=" + CurrentDcId +
                    " forceFresh=False");
                bool connected = await EnsureConnectedAsync(CurrentDcId, false, ct).ConfigureAwait(false);
                if (!connected)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "attempt #" + (attempt + 1) + " ensure-connect FAILED");
                    return CallOutcome.Fail("Network", -1, "login connection could not be opened", 0);
                }

                Vianigram.MTProto.MtProtoChannel chan = GetCurrentChannel();
                if (chan == null)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "attempt #" + (attempt + 1) + " channel went null post-connect; retrying");
                    CloseCurrentChannel();
                    continue;
                }

                bool wrappedForInit = !IsConnectionInitialized();
                byte[] requestToSend = wrappedForInit
                    ? WrapConnectionInit(requestBytes)
                    : requestBytes;
                if (wrappedForInit)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "wrapping first RPC in initConnection ctor=0x" + PeekCtor(requestBytes).ToString("x8") +
                        " innerSize=" + requestBytes.Length +
                        " wrappedSize=" + requestToSend.Length +
                        " layer=" + TelegramLayer +
                        " apiId=" + TelegramAppConfig.ApiId);
                }

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "attempt #" + (attempt + 1) + " native CallAsync begin size=" + requestToSend.Length +
                    " timeoutMs=" + ((int)NativeRpcTimeout.TotalMilliseconds));
                var rpcSw = Stopwatch.StartNew();

                Vianigram.MTProto.RpcResult result = null;
                Exception caughtEx = null;
                try
                {
                    // Race the native RPC against an explicit per-call deadline.
                    // The native channel doesn't honor a CancellationToken on the
                    // way through; without this guard a missing or malformed wire
                    // response wedges the await forever (we observed this with
                    // initConnection-wrapped first RPCs).
                    Task<Vianigram.MTProto.RpcResult> nativeTask =
                        chan.CallAsync(requestToSend).AsTask(ct);
                    Task timeoutTask = Task.Delay(NativeRpcTimeout, ct);
                    Task completed = await Task.WhenAny(nativeTask, timeoutTask).ConfigureAwait(false);

                    if (!object.ReferenceEquals(completed, nativeTask))
                    {
                        rpcSw.Stop();
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "attempt #" + (attempt + 1) + " native CallAsync TIMED OUT after " +
                            rpcSw.ElapsedMilliseconds + "ms (no wire response)");

                        // Discard the channel — its state is suspect (we may have
                        // a half-written request or a stuck receive loop).
                        CloseCurrentChannel();
                        if (!retriedTransientFreshKey)
                        {
                            retriedTransientFreshKey = true;
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "retrying after timeout with forceFreshKey=True");
                            bool opened = await EnsureConnectedAsync(CurrentDcId, true, ct).ConfigureAwait(false);
                            if (opened) continue;
                        }
                        return CallOutcome.Fail(
                            "Network",
                            -1,
                            "MTProto channel timeout (" +
                            ((int)NativeRpcTimeout.TotalSeconds) +
                            "s, no wire response)",
                            0);
                    }

                    result = await nativeTask.ConfigureAwait(false);
                    rpcSw.Stop();
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "attempt #" + (attempt + 1) + " native CallAsync returned elapsed=" +
                        rpcSw.ElapsedMilliseconds + "ms" +
                        " success=" + (result != null ? result.Success.ToString() : "(null)"));
                }
                catch (OperationCanceledException)
                {
                    rpcSw.Stop();
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "attempt #" + (attempt + 1) + " native CallAsync cancelled elapsed=" +
                        rpcSw.ElapsedMilliseconds + "ms");
                    throw;
                }
                catch (Exception ex)
                {
                    rpcSw.Stop();
                    caughtEx = ex;
                }

                // C# 5 disallows `await` inside a catch — drain the exception
                // path here instead.
                if (caughtEx != null)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "attempt #" + (attempt + 1) + " native CallAsync threw elapsed=" +
                        rpcSw.ElapsedMilliseconds + "ms " +
                        caughtEx.GetType().Name + ": " + caughtEx.Message);
                    CloseCurrentChannel();
                    if (!retriedTransientFreshKey)
                    {
                        retriedTransientFreshKey = true;
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "retrying after exception with forceFreshKey=True");
                        bool opened = await EnsureConnectedAsync(CurrentDcId, true, ct).ConfigureAwait(false);
                        if (opened) continue;
                    }
                    return CallOutcome.Fail("Network", -1, caughtEx.GetType().Name + ": " + caughtEx.Message, 0);
                }

                if (result == null)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "attempt #" + (attempt + 1) + " native CallAsync returned NULL");
                    return CallOutcome.Fail("Unknown", -1, "Native CallAsync returned null.", 0);
                }

                if (result.Success)
                {
                    byte[] body = result.ResultBytes;
                    if (body == null) body = new byte[0];

                    // Inflate gzip_packed envelopes before passing the
                    // bytes to typed decoders (see GzipResponseDecoder for
                    // the rationale — same path the main adapter uses).
                    int beforeLen = body.Length;
                    body = GzipResponseDecoder.MaybeInflate(body);
                    if (body.Length != beforeLen)
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "gzip_packed inflated " + beforeLen + " -> " + body.Length + " bytes");
                    }

                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "attempt #" + (attempt + 1) + " success bytes=" + body.Length +
                        " responseCtor=0x" + PeekCtor(body).ToString("x8"));
                    if (wrappedForInit)
                    {
                        MarkConnectionInitialized();
                        EarlyLog.Write("Account.LoginMTProto", "connection marked initialized");
                    }
                    return CallOutcome.Success(body);
                }

                CallOutcome failure = CallOutcome.Fail(
                    result.ErrorKind ?? "Unknown",
                    result.ErrorCode,
                    result.ErrorMessage ?? string.Empty,
                    result.ErrorParameter);

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "attempt #" + (attempt + 1) + " failure kind=" + (failure.Kind ?? "(none)") +
                    " code=" + failure.Code +
                    " param=" + failure.Parameter +
                    " msg=\"" + (failure.Message ?? string.Empty) + "\"");

                if (IsIncorrectServerSalt(failure) && saltRetries < IncorrectServerSaltRetryCount)
                {
                    saltRetries++;
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "incorrect_server_salt retry #" + saltRetries + "/" + IncorrectServerSaltRetryCount);
                    continue;
                }

                int migrateToDcId;
                if (TryGetMigrationDcId(failure, out migrateToDcId))
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "DC migration required: " + CurrentDcId + " → " + migrateToDcId);
                    CloseCurrentChannel();
                    bool migrated = await EnsureConnectedAsync(migrateToDcId, false, ct).ConfigureAwait(false);
                    if (migrated)
                    {
                        RememberLoginDcHint(migrateToDcId, "server-migrate");
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "migrated to DC#" + migrateToDcId + ", retrying call");
                        continue;
                    }
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "migration to DC#" + migrateToDcId + " failed; surfacing failure");
                    return failure;
                }

                if (IsConnectionNotInited(failure) && !retriedConnectionNotInited)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "CONNECTION_NOT_INITED — retrying with init wrap");
                    ResetConnectionInitialized();
                    retriedConnectionNotInited = true;
                    continue;
                }

                if (IsAuthRestart(failure) && !retriedAuthRestart)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "AUTH_RESTART — reopening channel with same key");
                    CloseCurrentChannel();
                    retriedAuthRestart = true;
                    bool reopened = await EnsureConnectedAsync(CurrentDcId, false, ct).ConfigureAwait(false);
                    if (reopened) continue;
                    return failure;
                }

                if (IsAuthKeyInvalid(failure) && !retriedAuthKey)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "auth_key invalid/session revoked — regenerating key");

                    // Blacklist the rejected key id so the cache-first
                    // policy refuses to reuse it from the persistent
                    // store on the very next ConnectCore call. Without
                    // this, the regenerate path below would race for a
                    // new key, the store load on subsequent connects
                    // would happily resurrect the just-rejected key
                    // (because IsUsableAuthKeyRecord only checks
                    // structure), and the cycle would repeat until the
                    // store was explicitly cleared.
                    ulong rejectedKeyId;
                    lock (_connectGate)
                    {
                        rejectedKeyId = _currentChannelAuthKeyId;
                    }
                    if (rejectedKeyId != 0UL)
                    {
                        // CallOutcome is a value struct, so we cannot test
                        // it against null. The Message string itself may
                        // be empty (default(CallOutcome) leaves it null),
                        // hence the IsNullOrEmpty guard.
                        string failureDetail = string.IsNullOrEmpty(failure.Message)
                            ? "(no detail)"
                            : failure.Message;
                        MarkAuthKeyServerRejected(
                            rejectedKeyId,
                            "AUTH_KEY_* RPC failure: " + failureDetail);
                    }

                    // Also remove the bad key from the persistent store
                    // so the next cold-start load returns null and the
                    // cache-first policy falls through to a fresh race.
                    try
                    {
                        await _authKeyStore.DeleteAsync(CurrentDcId, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key store delete after AUTH_KEY_* threw: " +
                            ex.GetType().Name + ": " + ex.Message);
                    }

                    CloseCurrentChannel();
                    retriedAuthKey = true;
                    bool reopened = await EnsureConnectedAsync(CurrentDcId, true, ct).ConfigureAwait(false);
                    if (reopened) continue;
                    return failure;
                }

                if (IsTransientChannelExit(failure))
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "transient channel exit (" + failure.Message + ")");
                    CloseCurrentChannel();
                    if (!retriedTransientSameKey)
                    {
                        retriedTransientSameKey = true;
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "retrying transient with same key");
                        bool reopened = await EnsureConnectedAsync(CurrentDcId, false, ct).ConfigureAwait(false);
                        if (reopened) continue;
                    }
                    if (!retriedTransientFreshKey)
                    {
                        retriedTransientFreshKey = true;
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "retrying transient with fresh key");
                        bool reopened = await EnsureConnectedAsync(CurrentDcId, true, ct).ConfigureAwait(false);
                        if (reopened) continue;
                    }
                    return failure;
                }

                if (wrappedForInit && !IsConnectionNotInited(failure))
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "init wrap accepted (server returned non-NOT_INITED error)");
                    MarkConnectionInitialized();
                }
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "surfacing failure to caller (no retry path matched)");
                return failure;
            }

            EarlyLog.Write(
                "Account.LoginMTProto",
                "retry budget exhausted (" + MaxRpcAttempts + " attempts)");
            return CallOutcome.Fail("Network", -1, "login RPC retry budget exhausted", 0);
        }

        private Task<bool> EnsureConnectedAsync(int targetDcId, bool forceFreshKey, CancellationToken ct)
        {
            if (targetDcId <= 0)
            {
                targetDcId = TelegramAppConfig.ActiveDcId;
            }

            lock (_connectGate)
            {
                if (!forceFreshKey &&
                    _qrForceFreshOnNextConnect &&
                    _qrForceFreshDcId == targetDcId &&
                    _channel == null)
                {
                    if (HasPrewarmInFlight(targetDcId) || HasFreshAnonymousPrewarm(targetDcId))
                    {
                        _qrForceFreshOnNextConnect = false;
                        _qrForceFreshDcId = 0;
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "QR migrate: using fresh prewarm for DC#" + targetDcId);
                    }
                    else
                    {
                        forceFreshKey = true;
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "QR migrate: forcing fresh auth_key for DC#" + targetDcId);
                    }
                }

                if (!forceFreshKey && _channel != null && _currentDcId == targetDcId)
                {
                    return TaskFromBool(true);
                }

                if (_connectTask != null &&
                    !_connectTask.IsCompleted &&
                    _connectTaskDcId == targetDcId &&
                    _connectTaskForceFreshKey == forceFreshKey)
                {
                    return _connectTask;
                }

                _connectTaskDcId = targetDcId;
                _connectTaskForceFreshKey = forceFreshKey;
                int generation = ++_connectGeneration;
                _connectTask = ConnectCoreAsync(targetDcId, forceFreshKey, generation, ct);
                return _connectTask;
            }
        }

        private async Task<bool> ConnectCoreAsync(
            int targetDcId,
            bool forceFreshKey,
            int generation,
            CancellationToken ct)
        {
            AccountAuthKeyRecord key = null;
            var totalSw = Stopwatch.StartNew();
            string keySource = "cache";
            try
            {
                TelegramDcEndpoint[] endpoints = GetLoginConnectionPlan(targetDcId);
                if (endpoints.Length == 0)
                {
                    return false;
                }

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "opening DC#" + targetDcId +
                    " plan=" + TelegramDcOptions.DescribePlan(endpoints) +
                    " pool=" + LoginPoolSize +
                    " forceFresh=" + forceFreshKey.ToString());

                // Coordinate with any in-flight PrewarmAuthKeyAsync for
                // this DC. Without this the QR-login MigrateTo path can
                // race the prewarm started after the first QR render and end up
                // doing two parallel 12 s DH handshakes against the
                // same DC. Awaiting the prewarm first means the auth_key
                // store will already be populated when we hit the load
                // path below — saving up to 10 s of wall time when the
                // user scans soon after the page opens.
                if (!forceFreshKey)
                {
                    System.Threading.Tasks.TaskCompletionSource<bool> inflightPrewarm = null;
                    lock (_prewarmGate)
                    {
                        System.Threading.Tasks.TaskCompletionSource<bool> tcs;
                        if (_prewarmTasks.TryGetValue(targetDcId, out tcs) && !tcs.Task.IsCompleted)
                        {
                            inflightPrewarm = tcs;
                        }
                    }
                    if (inflightPrewarm != null)
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "Connect DC#" + targetDcId + " awaiting in-flight prewarm");
                        var awaitSw = Stopwatch.StartNew();
                        try
                        {
                            await inflightPrewarm.Task.ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                            // Prewarm faulted — fall through to our own keygen.
                        }
                        awaitSw.Stop();
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "Connect DC#" + targetDcId + " prewarm-await elapsed=" +
                            awaitSw.ElapsedMilliseconds + "ms");
                    }
                }

                if (!forceFreshKey)
                {
                    var loadSw = Stopwatch.StartNew();
                    try
                    {
                        key = await _authKeyStore.LoadAsync(targetDcId, ct).ConfigureAwait(false);
                        loadSw.Stop();
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key load DC#" + targetDcId + " elapsed=" + loadSw.ElapsedMilliseconds +
                            "ms result=" + (key == null ? "null (no cache)" : "found"));
                    }
                    catch (Exception ex)
                    {
                        loadSw.Stop();
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key load DC#" + targetDcId + " THREW elapsed=" + loadSw.ElapsedMilliseconds +
                            "ms " + ex.GetType().Name + ": " + ex.Message);
                        key = null;
                    }

                    if (key != null && ShouldForceFreshAuthKey(key, callerRequestedFresh: false))
                    {
                        // ShouldForceFreshAuthKey returns true for any of:
                        // structurally unusable record, or a key id that
                        // was server-rejected (AUTH_KEY_*) during this
                        // process. Either way we delete and regenerate;
                        // letting a known-bad key flow into the open
                        // path just produces an immediate AUTH_KEY_*
                        // round trip and another regenerate.
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key DC#" + targetDcId + " cached but force-fresh policy triggered" +
                            " (keyLen=" + (key.AuthKey == null ? -1 : key.AuthKey.Length) +
                            " keyId=" + (key.AuthKeyId == 0 ? "0 (invalid)" : "0x" + key.AuthKeyId.ToString("x16")) +
                            "); deleting and regenerating");
                        try
                        {
                            await _authKeyStore.DeleteAsync(targetDcId, ct).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                        }
                        ClearAuthKeyRecord(key);
                        key = null;
                    }
                    else if (key != null)
                    {
                        ulong trustedPrewarmKeyId = GetFreshAnonymousPrewarmKeyId(targetDcId);
                        if (trustedPrewarmKeyId != 0UL && key.AuthKeyId != trustedPrewarmKeyId)
                        {
                            AccountAuthKeyRecord trustedPrewarm = CloneFreshAnonymousPrewarm(targetDcId);
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "auth_key DC#" + targetDcId +
                                " cache keyId=0x" + key.AuthKeyId.ToString("x16") +
                                " differs from fresh prewarm keyId=0x" +
                                trustedPrewarmKeyId.ToString("x16") +
                                (trustedPrewarm != null ? "; restoring prewarm" : "; regenerating"));
                            try
                            {
                                await _authKeyStore.DeleteAsync(targetDcId, ct).ConfigureAwait(false);
                            }
                            catch
                            {
                            }
                            ClearAuthKeyRecord(key);
                            key = null;
                            if (trustedPrewarm != null && IsUsableAuthKeyRecord(trustedPrewarm))
                            {
                                try
                                {
                                    await _authKeyStore.SaveAsync(targetDcId, trustedPrewarm, ct).ConfigureAwait(false);
                                    key = trustedPrewarm;
                                    keySource = "prewarm";
                                }
                                catch
                                {
                                    ClearAuthKeyRecord(trustedPrewarm);
                                    ClearFreshAnonymousPrewarm(targetDcId);
                                }
                            }
                            else
                            {
                                ClearFreshAnonymousPrewarm(targetDcId);
                            }
                        }
                    }

                    if (key != null)
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key DC#" + targetDcId + " cache HIT keyId=0x" + key.AuthKeyId.ToString("x16") +
                            " salt=" + key.ServerSalt.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " time_offset=" + key.ServerTimeOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                // TDLib×Android hybrid intra-DC walk: after the race winner
                // (always endpoints[0] now that MaxRaceCandidates=1) fails,
                // we retry the next IPs sequentially with exponential
                // backoff so SYN packets don't pile up on the TCP stack.
                int dhAttemptCount = 0;
                int openAttemptCount = 0;
                TimeSpan currentBackoff = InterEndpointBackoffInitial;
                int consecutiveDhTimeouts = 0;
                for (int i = 0; i < endpoints.Length; i++)
                {
                    TelegramDcEndpoint endpoint = endpoints[i];
                    long keyElapsedMs = 0;
                    long channelOpenElapsedMs = 0;

                    if (i > 0)
                    {
                        // Cap intra-DC walk by EITHER fresh DH attempts
                        // OR cached-key open attempts. With cache HIT the
                        // DH path never runs, so dhAttemptCount stays at
                        // 0 and would otherwise let the walk burn all
                        // endpoints (~5 s each) before falling back to
                        // the next DC. openAttemptCount caps that.
                        if (dhAttemptCount >= IntraDcSequentialRetries ||
                            openAttemptCount >= IntraDcSequentialRetries)
                        {
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "intra-DC sequential walk exhausted DC#" + targetDcId +
                                " after dh=" + dhAttemptCount +
                                " open=" + openAttemptCount + " attempts; aborting");
                            break;
                        }

                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "intra-DC backoff DC#" + targetDcId +
                            " before endpoint=" + endpoint.ToString() +
                            " delay_ms=" + ((int)currentBackoff.TotalMilliseconds));
                        await Task.Delay(currentBackoff, ct).ConfigureAwait(false);

                        TimeSpan nextBackoff = TimeSpan.FromMilliseconds(currentBackoff.TotalMilliseconds * 2);
                        currentBackoff = nextBackoff > InterEndpointBackoffMax
                            ? InterEndpointBackoffMax
                            : nextBackoff;
                    }

                    try
                    {
                        if (key == null)
                        {
                            if (i == 0 && endpoints.Length > 1)
                            {
                                keySource = "generated-race";
                                EarlyLog.Write(
                                    "Account.LoginMTProto",
                                    "auth_key DC#" + targetDcId +
                                    " race begin candidates=" +
                                    Math.Min(MaxAuthKeyRaceCandidates, endpoints.Length) +
                                    " plan=" + TelegramDcOptions.DescribePlan(endpoints));
                                var raceSw = Stopwatch.StartNew();
                                AuthKeyRaceResult race = await GenerateAuthKeyWithEndpointRaceAsync(endpoints, ct)
                                    .ConfigureAwait(false);
                                raceSw.Stop();
                                dhAttemptCount += race != null
                                    ? Math.Max(1, race.AttemptedCount)
                                    : Math.Min(MaxAuthKeyRaceCandidates, endpoints.Length);
                                if (race == null ||
                                    race.KeyResult.IsFail ||
                                    race.KeyResult.Value == null ||
                                    !IsUsableAuthKeyRecord(race.KeyResult.Value))
                                {
                                    string detail = race != null && race.KeyResult.IsFail && race.KeyResult.Error != null
                                        ? race.KeyResult.Error.ToString()
                                        : "no key returned";
                                    EarlyLog.Write(
                                        "Account.LoginMTProto",
                                        "auth_key race FAILED for DC#" + targetDcId +
                                        " elapsed=" + raceSw.ElapsedMilliseconds + "ms: " + detail);
                                    if (race != null && race.KeyResult.IsFail &&
                                        IsDhTimeout(race.KeyResult.Error, race.ElapsedMs))
                                    {
                                        consecutiveDhTimeouts++;
                                        TryEnableAbridgedFramingIfNeeded(targetDcId, consecutiveDhTimeouts);
                                    }
                                    int attempted = race != null ? race.AttemptedCount : Math.Min(MaxAuthKeyRaceCandidates, endpoints.Length);
                                    if (race != null && race.AbortWithoutSequentialFallback)
                                    {
                                        EarlyLog.Write(
                                            "Account.LoginMTProto",
                                            "auth_key race abort for DC#" + targetDcId +
                                            "; skipping sequential fallback");
                                        return false;
                                    }
                                    if (attempted >= endpoints.Length)
                                    {
                                        EarlyLog.Write(
                                            "Account.LoginMTProto",
                                            "auth_key race attempted full plan for DC#" + targetDcId +
                                            "; no sequential fallback remains");
                                        return false;
                                    }
                                    if (attempted > 0)
                                    {
                                        i = Math.Min(endpoints.Length - 1, attempted - 1);
                                    }
                                    continue;
                                }

                                consecutiveDhTimeouts = 0;
                                key = race.KeyResult.Value;
                                keyElapsedMs = race.ElapsedMs;
                                endpoint = race.Endpoint ?? endpoint;
                                int winnerIndex = FindEndpointIndex(endpoints, endpoint);
                                if (winnerIndex >= 0)
                                {
                                    i = winnerIndex;
                                }

                                EarlyLog.Write(
                                    "Account.LoginMTProto",
                                    "auth_key DC#" + targetDcId + " race winner=" + endpoint.ToString() +
                                    " winner_ms=" + keyElapsedMs +
                                    " total_ms=" + raceSw.ElapsedMilliseconds +
                                    " keyId=0x" + key.AuthKeyId.ToString("x16"));

                                var raceSaveSw = Stopwatch.StartNew();
                                try
                                {
                                    await _authKeyStore.SaveAsync(targetDcId, key, ct).ConfigureAwait(false);
                                    raceSaveSw.Stop();
                                    EarlyLog.Write(
                                        "Account.LoginMTProto",
                                        "auth_key DC#" + targetDcId + " saved elapsed=" + raceSaveSw.ElapsedMilliseconds + "ms");
                                }
                                catch (Exception ex)
                                {
                                    raceSaveSw.Stop();
                                    EarlyLog.Write(
                                        "Account.LoginMTProto",
                                        "auth_key save FAILED for DC#" + targetDcId + " elapsed=" + raceSaveSw.ElapsedMilliseconds +
                                        "ms " + ex.GetType().Name + ": " + ex.Message);
                                    return false;
                                }
                            }

                            if (key == null)
                            {
                            keySource = "generated";
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "auth_key DC#" + targetDcId +
                                " generation begin (DH handshake against " + endpoint.ToString() + ")");
                            var keySw = Stopwatch.StartNew();
                            var keyResult = await GenerateAuthKeyWithDeadlineAsync(endpoint, ct).ConfigureAwait(false);
                            keySw.Stop();
                            keyElapsedMs = keySw.ElapsedMilliseconds;
                            dhAttemptCount++;
                            if (keyResult.IsFail || keyResult.Value == null || !IsUsableAuthKeyRecord(keyResult.Value))
                            {
                                string detail = keyResult.IsFail && keyResult.Error != null
                                    ? keyResult.Error.ToString()
                                    : "no key returned";
                                EarlyLog.Write(
                                    "Account.LoginMTProto",
                                    "auth_key generation FAILED for DC#" + targetDcId +
                                    " endpoint=" + endpoint.ToString() +
                                    " elapsed=" + keyElapsedMs + "ms: " + detail);
                                TelegramDcOptions.ReportEndpointFailure(endpoint, detail);
                                if (IsDhTimeout(keyResult.Error, keyElapsedMs))
                                {
                                    consecutiveDhTimeouts++;
                                    TryEnableAbridgedFramingIfNeeded(targetDcId, consecutiveDhTimeouts);
                                }
                                continue;
                            }

                            consecutiveDhTimeouts = 0;
                            key = keyResult.Value;
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "auth_key DC#" + targetDcId + " generated elapsed=" + keyElapsedMs +
                                "ms keyId=0x" + key.AuthKeyId.ToString("x16") +
                                " salt=" + key.ServerSalt.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                " time_offset=" + key.ServerTimeOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                " endpoint=" + endpoint.ToString());

                            var saveSw = Stopwatch.StartNew();
                            try
                            {
                                await _authKeyStore.SaveAsync(targetDcId, key, ct).ConfigureAwait(false);
                                saveSw.Stop();
                                EarlyLog.Write(
                                    "Account.LoginMTProto",
                                    "auth_key DC#" + targetDcId + " saved elapsed=" + saveSw.ElapsedMilliseconds + "ms");
                            }
                            catch (Exception ex)
                            {
                                saveSw.Stop();
                                EarlyLog.Write(
                                    "Account.LoginMTProto",
                                    "auth_key save FAILED for DC#" + targetDcId + " elapsed=" + saveSw.ElapsedMilliseconds +
                                    "ms " + ex.GetType().Name + ": " + ex.Message);
                                return false;
                            }
                            }
                        }

                        TimeSpan openBudget = string.Equals(keySource, "cache", StringComparison.Ordinal)
                            ? TelegramDcOptions.CachedOpenTimeout
                            : TelegramDcOptions.ChannelOpenTimeout;
                        var openSw = Stopwatch.StartNew();
                        var opened = await OpenLoginChannelWithDeadlineAsync(endpoint, key, openBudget, ct)
                            .ConfigureAwait(false);
                        openSw.Stop();
                        channelOpenElapsedMs = openSw.ElapsedMilliseconds;
                        openAttemptCount++;

                        if (opened == null)
                        {
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "open timed out/null for DC#" + targetDcId +
                                " endpoint=" + endpoint.ToString() +
                                " elapsed=" + channelOpenElapsedMs.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                "ms budget=" + ((int)openBudget.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
                            TelegramDcOptions.ReportEndpointFailure(endpoint);
                            continue;
                        }

                        Vianigram.MTProto.MtProtoChannel oldChannel = null;
                        lock (_connectGate)
                        {
                            oldChannel = _channel;
                            _channel = opened;
                            _currentDcId = targetDcId;
                            _connectionInitialized = false;

                            // Track the channel's auth_key in C#-side state
                            // so the QR login handler can recover the
                            // truly-authorized key on loginTokenSuccess
                            // even if the persistent store has drifted.
                            // Copy the bytes BEFORE the surrounding finally
                            // calls ClearAuthKeyRecord(key) (which zeros
                            // the array we'd otherwise alias).
                            if (_currentChannelAuthKeyBytes != null)
                            {
                                Array.Clear(
                                    _currentChannelAuthKeyBytes,
                                    0,
                                    _currentChannelAuthKeyBytes.Length);
                            }
                            _currentChannelAuthKeyBytes = key.AuthKey != null
                                ? (byte[])key.AuthKey.Clone()
                                : null;
                            _currentChannelAuthKeyId = key.AuthKeyId;
                            _currentChannelServerSalt = key.ServerSalt;
                            _currentChannelServerTimeOffset = key.ServerTimeOffset;
                            if (_qrForceFreshOnNextConnect && _qrForceFreshDcId == targetDcId)
                            {
                                _qrForceFreshOnNextConnect = false;
                                _qrForceFreshDcId = 0;
                            }
                        }

                        if (oldChannel != null && !object.ReferenceEquals(oldChannel, opened))
                        {
                            try { oldChannel.Close(); }
                            catch (Exception) { }
                        }

                        TelegramDcOptions.ReportEndpointSuccess(endpoint);
                        RememberLoginEndpoint(endpoint, "open");
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "opened DC#" + targetDcId + " " + endpoint.ToString() +
                            " auth_key_id=0x" +
                            key.AuthKeyId.ToString("x16") +
                            " pool=" + opened.PoolSize +
                            " key_source=" + keySource +
                            " salt=" + key.ServerSalt.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " time_offset=" + key.ServerTimeOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " key_ms=" + keyElapsedMs.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " open_ms=" + channelOpenElapsedMs.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " total_ms=" + totalSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        TelegramDcOptions.ReportEndpointFailure(endpoint, ex.GetType().Name + ": " + ex.Message);
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "open failed for DC#" + targetDcId +
                            " endpoint=" + endpoint.ToString() + ": " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }

                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "all endpoints failed for DC#" + targetDcId +
                    " total_ms=" + totalSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "open failed for DC#" + targetDcId + ": " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                ClearAuthKeyRecord(key);
                lock (_connectGate)
                {
                    if (_connectGeneration == generation)
                    {
                        _connectTask = null;
                        _connectTaskDcId = 0;
                        _connectTaskForceFreshKey = false;
                    }
                }
            }
        }

        private async Task<Result<AccountAuthKeyRecord, AccountError>> GenerateAuthKeyWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            CancellationToken ct)
        {
            return await GenerateAuthKeyWithDeadlineAsync(endpoint, ct, ct).ConfigureAwait(false);
        }

        private async Task<Result<AccountAuthKeyRecord, AccountError>> GenerateAuthKeyWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            CancellationToken attemptCt,
            CancellationToken callerCt)
        {
            CancellationTokenSource attemptCts = CancellationTokenSource.CreateLinkedTokenSource(attemptCt);
            Task<Result<AccountAuthKeyRecord, AccountError>> keyTask =
                _keyGen.GenerateForDcAsync(endpoint.Host, endpoint.Port, endpoint.DcId, attemptCts.Token);
            Task timeoutTask = Task.Delay(TelegramDcOptions.AuthKeyGenerationTimeout, callerCt);
            Task completed = await Task.WhenAny(keyTask, timeoutTask).ConfigureAwait(false);
            if (!object.ReferenceEquals(completed, keyTask))
            {
                if (callerCt.IsCancellationRequested)
                {
                    try { attemptCts.Cancel(); }
                    catch { }
                    ObserveFault(keyTask);
                    DisposeWhenComplete(keyTask, attemptCts);
                    throw new OperationCanceledException(callerCt);
                }

                try { attemptCts.Cancel(); }
                catch { }

                // Grace period: let the adapter finalize so we can pull
                // the last handshake-step diagnostic out of the
                // OperationCanceledException (Exception.Data["last_step"])
                // that AuthKeyGeneratorAdapter stamps before its finally
                // closes the native connection. Without this we surface
                // "timed out" with no clue whether req_pq_multi got sent,
                // resPQ never arrived, or a later step stalled.
                string lastStep = null;
                try
                {
                    Task grace = Task.Delay(TimeSpan.FromMilliseconds(250), callerCt);
                    await Task.WhenAny(keyTask, grace).ConfigureAwait(false);
                }
                catch { /* grace best-effort */ }
                // Cancelled tasks (the common path here — attemptCts
                // cancelled the adapter) have IsCanceled=true and
                // Exception=null. Their OCE carries Data["last_step"];
                // pull it out via a non-throwing await wrapped in
                // try/catch. IsFaulted is the rare path for adapter
                // exceptions that aren't cancellations.
                if (keyTask.IsCompleted)
                {
                    if (keyTask.IsCanceled || keyTask.IsFaulted)
                    {
                        try
                        {
                            await keyTask.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException oce)
                        {
                            if (oce != null && oce.Data != null && oce.Data.Contains("last_step"))
                            {
                                object v = oce.Data["last_step"];
                                if (v != null) lastStep = v.ToString();
                            }
                        }
                        catch (Exception ex)
                        {
                            // Faulted (non-cancel) path — usually an
                            // unexpected adapter bug. Treat Data the
                            // same way.
                            if (ex != null && ex.Data != null && ex.Data.Contains("last_step"))
                            {
                                object v = ex.Data["last_step"];
                                if (v != null) lastStep = v.ToString();
                            }
                        }
                    }
                    else
                    {
                        // Completed with a Result<>.Fail — the detail
                        // already carries "| last_step=..." from the
                        // adapter's catch (Exception) path. The
                        // calling site sees the full message via the
                        // returned Fail; no extra extraction here.
                    }
                }

                ObserveFault(keyTask);
                DisposeWhenComplete(keyTask, attemptCts);
                string timeoutDetail = "auth_key generation timed out against " + endpoint.ToString();
                if (!string.IsNullOrEmpty(lastStep))
                {
                    timeoutDetail += " | last_step=" + lastStep;
                }
                return Result<AccountAuthKeyRecord, AccountError>.Fail(
                    AccountError.NetworkError(timeoutDetail));
            }

            try
            {
                return await keyTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                if (callerCt.IsCancellationRequested)
                {
                    throw;
                }

                return Result<AccountAuthKeyRecord, AccountError>.Fail(
                    AccountError.NetworkError("auth_key generation cancelled against " + endpoint.ToString()));
            }
            finally
            {
                attemptCts.Dispose();
            }
        }

        private async Task<AuthKeyRaceResult> GenerateAuthKeyWithEndpointRaceAsync(
            TelegramDcEndpoint[] endpoints,
            CancellationToken ct)
        {
            if (endpoints == null || endpoints.Length == 0)
            {
                return null;
            }

            int candidateCount = Math.Min(MaxAuthKeyRaceCandidates, endpoints.Length);
            if (candidateCount <= 1)
            {
                return await GenerateAuthKeyCandidateAsync(endpoints[0], 1, ct, ct).ConfigureAwait(false);
            }

            using (var raceCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                // Wall deadline. Worst-case before this was
                //   stagger * (candidateCount - 1) + AuthKeyGenerationTimeout
                //   = 750ms * 8 + 16s = 22s
                // even when EVERY candidate was dead. The cap must still
                // exceed observed successful DC#1 handshakes (~12s), or the
                // race aborts valid winners and the QR migrate path loops.
                try { raceCts.CancelAfter(AuthKeyRaceWallDeadline); }
                catch (ObjectDisposedException) { }

                List<Task<AuthKeyRaceResult>> running = new List<Task<AuthKeyRaceResult>>();
                AuthKeyRaceResult lastResult = null;
                int nextIndex = 0;
                int hardFailureCount = 0;

                running.Add(GenerateAuthKeyCandidateAsync(endpoints[nextIndex], nextIndex + 1, raceCts.Token, ct));
                nextIndex++;

                while (running.Count > 0 || nextIndex < candidateCount)
                {
                    // Wall-deadline early exit: raceCts fired but the
                    // caller's ct is still healthy. Return the most
                    // informative failure we collected; ConnectCoreAsync
                    // will iterate endpoints (cooldown-sorted) on its
                    // sequential fallback path.
                    if (raceCts.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key race wall-deadline reached (" +
                            ((int)AuthKeyRaceWallDeadline.TotalMilliseconds) +
                            "ms) — bailing with " + running.Count +
                            " in-flight, " + (candidateCount - nextIndex) + " unlaunched");
                        ObserveDetachedAuthKeyCandidates(running);
                        return BuildRaceAbortResult(
                            lastResult,
                            nextIndex,
                            "auth_key race wall-deadline reached after " +
                            ((int)AuthKeyRaceWallDeadline.TotalMilliseconds) + "ms");
                    }

                    // Hard-failure fast-track. If the local network has
                    // shown N consecutive "no route" errors, every
                    // remaining candidate will eventually time out the
                    // same way — burning ~16s per endpoint is pure waste.
                    // Bail to the caller's recovery path immediately.
                    if (hardFailureCount >= AuthKeyRaceHardFailureBailout)
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key race hard-failure fast-track: " +
                            hardFailureCount +
                            " unreachable failures observed — bailing");
                        try { raceCts.Cancel(); } catch { }
                        ObserveDetachedAuthKeyCandidates(running);
                        return BuildRaceAbortResult(
                            lastResult,
                            nextIndex,
                            "auth_key race hard-failure fast-track after " +
                            hardFailureCount + " unreachable failures");
                    }

                    if (running.Count == 0)
                    {
                        running.Add(GenerateAuthKeyCandidateAsync(
                            endpoints[nextIndex],
                            nextIndex + 1,
                            raceCts.Token,
                            ct));
                        nextIndex++;
                        continue;
                    }

                    Task delayTask = null;
                    int extra = 0;
                    if (nextIndex < candidateCount)
                    {
                        delayTask = Task.Delay(AuthKeyRaceStagger, ct);
                        extra = 1;
                    }

                    Task[] waiters = new Task[running.Count + extra];
                    for (int i = 0; i < running.Count; i++)
                    {
                        waiters[i] = running[i];
                    }
                    if (delayTask != null)
                    {
                        waiters[waiters.Length - 1] = delayTask;
                    }

                    Task completed = await Task.WhenAny(waiters).ConfigureAwait(false);
                    if (object.ReferenceEquals(completed, delayTask))
                    {
                        if (ct.IsCancellationRequested)
                        {
                            throw new OperationCanceledException(ct);
                        }

                        running.Add(GenerateAuthKeyCandidateAsync(
                            endpoints[nextIndex],
                            nextIndex + 1,
                            raceCts.Token,
                            ct));
                        nextIndex++;
                        continue;
                    }

                    var candidateTask = completed as Task<AuthKeyRaceResult>;
                    if (candidateTask == null)
                    {
                        continue;
                    }

                    running.Remove(candidateTask);
                    AuthKeyRaceResult candidate;
                    try
                    {
                        candidate = await candidateTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        if (ct.IsCancellationRequested)
                        {
                            throw;
                        }
                        continue;
                    }

                    if (candidate == null)
                    {
                        continue;
                    }

                    candidate.AttemptedCount = Math.Max(candidate.AttemptedCount, nextIndex);
                    lastResult = candidate;
                    if (candidate.KeyResult.IsOk &&
                        candidate.KeyResult.Value != null &&
                        IsUsableAuthKeyRecord(candidate.KeyResult.Value))
                    {
                        try { raceCts.Cancel(); }
                        catch { }
                        ObserveDetachedAuthKeyCandidates(running);
                        return candidate;
                    }

                    string reason = candidate.KeyResult.IsFail && candidate.KeyResult.Error != null
                        ? candidate.KeyResult.Error.ToString()
                        : null;
                    TelegramDcOptions.ReportEndpointFailure(candidate.Endpoint, reason);
                    if (candidate.KeyResult.IsFail &&
                        IsHardNetworkFailure(candidate.KeyResult.Error))
                    {
                        hardFailureCount++;
                    }
                }

                return lastResult;
            }
        }

        // Detects transport-level errors that signal the local network
        // simply does not have a path to Telegram. These are the failures
        // that justify the race fast-track: continuing the staggered
        // schedule against more endpoints of the same family won't help
        // because none of them will be reachable either. Matches by
        // substring on the error message so it works against both
        // managed-side socket exceptions and the native MTProto wrapper's
        // text-formatted errors. Does NOT include "timed out" — a slow
        // network is still potentially routable, and we let it consume
        // its per-candidate deadline (the wall cap covers the overall
        // worst case).
        private static bool IsHardNetworkFailure(AccountError error)
        {
            if (error == null) return false;
            string message = error.Message ?? string.Empty;
            if (message.Length == 0) return false;

            return
                message.IndexOf("unreachable", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("WSAEHOSTUNREACH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("WSAENETUNREACH", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("0x80072751", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("0x80072743", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("WSAECONNREFUSED", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("connection refused", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("no such host", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("name or service not known", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Did the DH handshake fail because the server never replied
        // (or its replies were dropped on the way back)? That is the
        // hallmark of DPI silently dropping recognisable MTProto
        // signatures: TCP opens fine, we write req_pq_multi, then
        // nothing — the per-handshake deadline expires and the
        // adapter surfaces "timed out". This is the signal that
        // switching to Abridged framing might unstick the next
        // attempt — Intermediate's 0xEEEEEEEE greeting is a 4-byte
        // marker, while Abridged's 0xEF is a single byte that blends
        // into random TCP payload.
        private static bool IsDhTimeout(AccountError error, long elapsedMs)
        {
            if (error == null) return false;
            string message = error.Message ?? string.Empty;
            if (message.Length == 0) return false;

            return
                message.IndexOf("timed out", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("HandshakeFailed", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Direct-dial framing fallback. Default greeting is Intermediate
        // (0xEEEEEEEE) — DPI fingerprintable. After enough consecutive
        // DH timeouts on the same DC we flip to Abridged (single 0xEF)
        // for subsequent attempts. The native runtime config
        // (Vianigram.MTProto.MtProtoRuntime) applies the change to all
        // *new* MtProtoConnection / MtProtoChannel objects; in-flight
        // sockets keep their original framing.
        private static int _abridgedFramingEnabledForDcMask;
        private static void TryEnableAbridgedFramingIfNeeded(int dcId, int consecutiveTimeouts)
        {
            if (consecutiveTimeouts < MaxConsecutiveDhTimeoutsBeforeFramingSwitch)
            {
                return;
            }

            int dcBit = dcId > 0 && dcId < 32 ? (1 << dcId) : 0;
            if (dcBit == 0) return;

            // SetBitOrAtomic returns the PREVIOUS value of the mask.
            // If our bit was already set, another caller beat us to
            // the framing flip for this DC; nothing to do.
            int prev;
            int updated;
            do
            {
                prev = System.Threading.Interlocked.CompareExchange(ref _abridgedFramingEnabledForDcMask, 0, 0);
                if ((prev & dcBit) != 0) return;
                updated = prev | dcBit;
            }
            while (System.Threading.Interlocked.CompareExchange(ref _abridgedFramingEnabledForDcMask, updated, prev) != prev);

            try
            {
                // 1 = Abridged framing (0xEF greeting, single byte).
                // 0 = Intermediate (0xEEEEEEEE, default).
                Vianigram.MTProto.MtProtoRuntime.SetDirectDialFraming(1);
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "framing switch to Abridged for DC#" + dcId +
                    " after " + consecutiveTimeouts + " consecutive DH timeouts");
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "framing switch FAILED for DC#" + dcId + ": " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private TelegramDcEndpoint[] GetLoginConnectionPlan(int dcId)
        {
            string preferredHost = null;
            int preferredPort = 0;
            ILoginEndpointPreferenceStore endpointStore =
                _preferredDcStore as ILoginEndpointPreferenceStore;
            if (endpointStore != null &&
                endpointStore.TryGetLoginEndpoint(dcId, out preferredHost, out preferredPort))
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "login endpoint preference: dc=" + dcId +
                    " endpoint=" + preferredHost + ":" +
                    preferredPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            return TelegramDcOptions.GetConnectionPlan(
                dcId,
                TelegramAppConfig.UseTestEnvironment,
                preferredHost,
                preferredPort);
        }

        private void RememberLoginEndpoint(TelegramDcEndpoint endpoint, string reason)
        {
            if (endpoint == null || endpoint.DcId <= 0 || string.IsNullOrEmpty(endpoint.Host) || endpoint.Port <= 0)
            {
                return;
            }

            ILoginEndpointPreferenceStore endpointStore =
                _preferredDcStore as ILoginEndpointPreferenceStore;
            if (endpointStore == null)
            {
                return;
            }

            try
            {
                endpointStore.SetLoginEndpoint(endpoint.DcId, endpoint.Host, endpoint.Port);
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "login endpoint preference saved: dc=" + endpoint.DcId +
                    " endpoint=" + endpoint.ToString() +
                    " reason=" + (reason ?? string.Empty));
            }
            catch
            {
            }
        }

        private async Task<AuthKeyRaceResult> GenerateAuthKeyCandidateAsync(
            TelegramDcEndpoint endpoint,
            int attemptedCount,
            CancellationToken attemptCt,
            CancellationToken callerCt)
        {
            var sw = Stopwatch.StartNew();
            EarlyLog.Write(
                "Account.LoginMTProto",
                "auth_key race candidate #" + attemptedCount +
                " begin endpoint=" + endpoint.ToString());
            try
            {
                Result<AccountAuthKeyRecord, AccountError> result =
                    await GenerateAuthKeyWithDeadlineAsync(endpoint, attemptCt, callerCt).ConfigureAwait(false);
                sw.Stop();
                if (result.IsOk && result.Value != null && IsUsableAuthKeyRecord(result.Value))
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "auth_key race candidate #" + attemptedCount +
                        " OK endpoint=" + endpoint.ToString() +
                        " elapsed=" + sw.ElapsedMilliseconds + "ms keyId=0x" +
                        result.Value.AuthKeyId.ToString("x16"));
                }
                else
                {
                    string detail = result.IsFail && result.Error != null
                        ? result.Error.ToString()
                        : "no key returned";
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "auth_key race candidate #" + attemptedCount +
                        " FAILED endpoint=" + endpoint.ToString() +
                        " elapsed=" + sw.ElapsedMilliseconds + "ms: " + detail);
                }
                return new AuthKeyRaceResult
                {
                    Endpoint = endpoint,
                    KeyResult = result,
                    ElapsedMs = sw.ElapsedMilliseconds,
                    AttemptedCount = attemptedCount
                };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "auth_key race candidate #" + attemptedCount +
                    " THREW endpoint=" + endpoint.ToString() +
                    " elapsed=" + sw.ElapsedMilliseconds + "ms " +
                    ex.GetType().Name + ": " + ex.Message);
                return new AuthKeyRaceResult
                {
                    Endpoint = endpoint,
                    KeyResult = Result<AccountAuthKeyRecord, AccountError>.Fail(
                        AccountError.NetworkError(ex.GetType().Name + ": " + ex.Message)),
                    ElapsedMs = sw.ElapsedMilliseconds,
                    AttemptedCount = attemptedCount
                };
            }
        }

        private static AuthKeyRaceResult BuildRaceAbortResult(
            AuthKeyRaceResult lastResult,
            int attemptedCount,
            string detail)
        {
            if (lastResult != null)
            {
                lastResult.AttemptedCount = Math.Max(lastResult.AttemptedCount, attemptedCount);
                lastResult.AbortWithoutSequentialFallback = true;
                return lastResult;
            }

            return new AuthKeyRaceResult
            {
                Endpoint = null,
                KeyResult = Result<AccountAuthKeyRecord, AccountError>.Fail(
                    AccountError.NetworkError(detail ?? "auth_key race aborted")),
                ElapsedMs = 0,
                AttemptedCount = attemptedCount,
                AbortWithoutSequentialFallback = true
            };
        }

        private static void ObserveDetachedAuthKeyCandidates(List<Task<AuthKeyRaceResult>> tasks)
        {
            if (tasks == null) return;
            for (int i = 0; i < tasks.Count; i++)
            {
                Task<AuthKeyRaceResult> task = tasks[i];
                if (task == null) continue;
                task.ContinueWith(
                    delegate(Task<AuthKeyRaceResult> t)
                    {
                        if (t.IsFaulted)
                        {
                            var ignored = t.Exception;
                            return;
                        }
                        if (t.IsCanceled)
                        {
                            return;
                        }

                        AuthKeyRaceResult result = t.Result;
                        if (result != null &&
                            result.KeyResult.IsOk &&
                            result.KeyResult.Value != null)
                        {
                            ClearAuthKeyRecord(result.KeyResult.Value);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        private static int FindEndpointIndex(TelegramDcEndpoint[] endpoints, TelegramDcEndpoint endpoint)
        {
            if (endpoints == null || endpoint == null) return -1;
            for (int i = 0; i < endpoints.Length; i++)
            {
                TelegramDcEndpoint candidate = endpoints[i];
                if (candidate != null &&
                    string.Equals(candidate.Host, endpoint.Host, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Port == endpoint.Port)
                {
                    return i;
                }
            }
            return -1;
        }

        private static async Task<Vianigram.MTProto.MtProtoChannel> OpenLoginChannelWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            AccountAuthKeyRecord key,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Task<Vianigram.MTProto.MtProtoChannel> openTask = Vianigram.MTProto.MtProtoChannel
                .OpenWithPoolSizeAsync(
                    endpoint.Host,
                    endpoint.Port,
                    key.AuthKey,
                    key.AuthKeyId,
                    key.ServerSalt,
                    key.ServerTimeOffset,
                    LoginPoolSize)
                .AsTask(ct);
            Task timeoutTask = Task.Delay(timeout, ct);
            Task completed = await Task.WhenAny(openTask, timeoutTask).ConfigureAwait(false);
            if (!object.ReferenceEquals(completed, openTask))
            {
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                ObserveDetachedOpen(openTask);
                return null;
            }

            return await openTask.ConfigureAwait(false);
        }

        private static void ObserveFault(Task task)
        {
            if (task == null) return;
            task.ContinueWith(
                delegate(Task t)
                {
                    var ignored = t.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void DisposeWhenComplete(Task task, CancellationTokenSource cts)
        {
            if (cts == null) return;
            if (task == null)
            {
                cts.Dispose();
                return;
            }

            task.ContinueWith(
                delegate
                {
                    try { cts.Dispose(); }
                    catch { }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void ObserveDetachedOpen(Task<Vianigram.MTProto.MtProtoChannel> task)
        {
            if (task == null) return;
            task.ContinueWith(
                delegate(Task<Vianigram.MTProto.MtProtoChannel> t)
                {
                    if (t.IsFaulted)
                    {
                        var ignored = t.Exception;
                        return;
                    }

                    if (t.IsCanceled)
                    {
                        return;
                    }

                    Vianigram.MTProto.MtProtoChannel channel = t.Result;
                    if (channel != null)
                    {
                        try { channel.Close(); }
                        catch (Exception) { }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private Vianigram.MTProto.MtProtoChannel GetCurrentChannel()
        {
            lock (_connectGate)
            {
                return _channel;
            }
        }

        private void CloseCurrentChannel()
        {
            Vianigram.MTProto.MtProtoChannel old = null;
            lock (_connectGate)
            {
                old = _channel;
                _channel = null;
                _connectionInitialized = false;
                if (_currentChannelAuthKeyBytes != null)
                {
                    Array.Clear(
                        _currentChannelAuthKeyBytes,
                        0,
                        _currentChannelAuthKeyBytes.Length);
                    _currentChannelAuthKeyBytes = null;
                }
                _currentChannelAuthKeyId = 0;
                _currentChannelServerSalt = 0;
                _currentChannelServerTimeOffset = 0;
            }

            if (old != null)
            {
                try { old.Close(); }
                catch (Exception) { }
            }
        }

        private void ResetAfterLogout()
        {
            int loggedOutDcId = CurrentDcId;
            CloseCurrentChannel();
            lock (_connectGate)
            {
                _currentDcId = TelegramAppConfig.ActiveDcId;
                _connectionInitialized = false;
                _qrAnonymousFreshKeyPrepared = false;
                _qrForceFreshOnNextConnect = false;
                _qrForceFreshDcId = 0;
                _connectTask = null;
                _connectTaskDcId = 0;
                _connectTaskForceFreshKey = false;
                unchecked { _connectGeneration++; }
            }
            ClearFreshAnonymousPrewarm(loggedOutDcId);

            EarlyLog.Write(
                "Account.LoginMTProto",
                "logout reset: login channel cleared; next QR starts anonymous on DC#" +
                TelegramAppConfig.ActiveDcId);
        }

        private bool IsConnectionInitialized()
        {
            lock (_connectGate)
            {
                return _connectionInitialized;
            }
        }

        private void MarkConnectionInitialized()
        {
            lock (_connectGate)
            {
                _connectionInitialized = true;
            }

            // First successful connInited per process is the right moment
            // to refresh dc_options. The channel is open, the auth_key
            // is verified, the user's first poll has already gone out so
            // we won't compete with the QR first paint. Fire-and-forget
            // — failures are logged but don't affect anything.
            if (Interlocked.Exchange(ref _helpGetConfigDispatched, 1) == 0)
            {
                Task.Run(async delegate
                {
                    try
                    {
                        // Small delay so the QR first poll completes and
                        // the user sees the QR before any extra wire
                        // chatter. Three seconds is enough that the
                        // refresh happens AFTER the first scan-wait
                        // poll on a healthy network, and before the
                        // user has had time to pick up their phone.
                        await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                        if (IsAnonymousQrContext())
                        {
                            Interlocked.Exchange(ref _helpGetConfigDispatched, 0);
                            EarlyLog.Write(
                                "Account.LoginMTProto",
                                "help.getConfig skipped while QR login is anonymous");
                            return;
                        }

                        await DispatchHelpGetConfigAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "help.getConfig dispatch threw: " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                });
            }
        }

        /// <summary>
        /// Fire help.getConfig on the current channel, parse the response,
        /// and hand the dc_options vector to TelegramDcOptions for
        /// persistence + plan-build merge on subsequent races. Safe to
        /// call from any thread; uses the same CallInternal pipeline as
        /// every other RPC so retries / AUTH_KEY_* / etc. are handled
        /// uniformly.
        /// </summary>
        private async Task DispatchHelpGetConfigAsync(CancellationToken ct)
        {
            byte[] request = Vianigram.Account.Infrastructure.TlEncoder.EncodeHelpGetConfig();
            EarlyLog.Write(
                "Account.LoginMTProto",
                "help.getConfig dispatching size=" + request.Length);

            CallOutcome outcome;
            var sw = Stopwatch.StartNew();
            try
            {
                outcome = await CallInternalAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                EarlyLog.Write("Account.LoginMTProto", "help.getConfig cancelled");
                return;
            }
            sw.Stop();

            if (!outcome.Ok)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "help.getConfig FAILED elapsed=" + sw.ElapsedMilliseconds +
                    "ms kind=" + (outcome.Kind ?? "(none)") +
                    " code=" + outcome.Code +
                    " msg=" + (outcome.Message ?? string.Empty));
                return;
            }

            var decoded = Vianigram.Account.Infrastructure.TlDecoder.DecodeConfig(outcome.Bytes);
            if (decoded == null || decoded.DcOptions == null || decoded.DcOptions.Length == 0)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "help.getConfig decoded empty/null elapsed=" + sw.ElapsedMilliseconds + "ms");
                return;
            }

            DcOptionRecord[] records = new DcOptionRecord[decoded.DcOptions.Length];
            int kept = 0;
            DateTime now = DateTime.UtcNow;
            for (int i = 0; i < decoded.DcOptions.Length; i++)
            {
                var o = decoded.DcOptions[i];
                if (o == null || string.IsNullOrEmpty(o.IpAddress) || o.Port <= 0) continue;

                records[kept++] = new DcOptionRecord(
                    o.DcId,
                    o.IpAddress,
                    o.Port,
                    o.Ipv6,
                    o.MediaOnly,
                    o.TcpoOnly,
                    o.Cdn,
                    o.StaticFlag,
                    o.ThisPortOnly,
                    o.Secret,
                    now);
            }

            if (kept != records.Length)
            {
                DcOptionRecord[] trimmed = new DcOptionRecord[kept];
                Array.Copy(records, trimmed, kept);
                records = trimmed;
            }

            await TelegramDcOptions.ReplaceDcOptionsAsync(records, ct).ConfigureAwait(false);

            EarlyLog.Write(
                "Account.LoginMTProto",
                "help.getConfig OK elapsed=" + sw.ElapsedMilliseconds +
                "ms dc_options=" + records.Length +
                " (test_mode=" + decoded.TestMode +
                ", this_dc=" + decoded.ThisDc + ")");
        }

        private void ResetConnectionInitialized()
        {
            lock (_connectGate)
            {
                _connectionInitialized = false;
            }
        }

        private static byte[] WrapConnectionInit(byte[] requestBytes)
        {
            return new TlByteBuilder()
                .WriteUInt32(InvokeWithLayerCtor)
                .WriteInt32(TelegramLayer)
                .WriteUInt32(InitConnectionCtor)
                .WriteInt32(0)
                .WriteInt32(TelegramAppConfig.ApiId)
                .WriteString(TelegramAppConfig.DeviceModel)
                .WriteString(TelegramAppConfig.SystemVersion)
                .WriteString(TelegramAppConfig.AppVersion)
                .WriteString(TelegramAppConfig.SystemLangCode)
                .WriteString(TelegramAppConfig.LangPack)
                .WriteString(TelegramAppConfig.LangCode)
                .WriteRaw(requestBytes)
                .ToArray();
        }

        private static bool IsIncorrectServerSalt(CallOutcome outcome)
        {
            return outcome.Code == IncorrectServerSaltErrorCode;
        }

        private static uint PeekCtor(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4)
            {
                return 0;
            }

            return (uint)(bytes[0]
                | (bytes[1] << 8)
                | (bytes[2] << 16)
                | (bytes[3] << 24));
        }

        private static bool IsRequestCtor(byte[] requestBytes, uint ctor)
        {
            return requestBytes != null &&
                   requestBytes.Length >= 4 &&
                   PeekCtor(requestBytes) == ctor;
        }

        private static bool IsConnectionNotInited(CallOutcome outcome)
        {
            return outcome.Code == 400 &&
                string.Equals(outcome.Message, "CONNECTION_NOT_INITED", StringComparison.Ordinal);
        }

        private static bool IsAuthRestart(CallOutcome outcome)
        {
            string kind = outcome.Kind ?? string.Empty;
            string message = outcome.Message ?? string.Empty;
            return kind.IndexOf("AuthRestart", StringComparison.OrdinalIgnoreCase) >= 0 ||
                string.Equals(message, "AUTH_RESTART", StringComparison.Ordinal);
        }

        private static bool IsAuthKeyInvalid(CallOutcome outcome)
        {
            string kind = outcome.Kind ?? string.Empty;
            string message = outcome.Message ?? string.Empty;
            return kind.IndexOf("AuthKeyInvalid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("AUTH_KEY_", StringComparison.Ordinal) >= 0 ||
                message.IndexOf("SESSION_REVOKED", StringComparison.Ordinal) >= 0;
        }

        private static bool IsTransientChannelExit(CallOutcome outcome)
        {
            string message = outcome.Message ?? string.Empty;
            return string.Equals(message, "receive_loop_exited", StringComparison.Ordinal) ||
                string.Equals(message, "session_not_running", StringComparison.Ordinal) ||
                string.Equals(message, "session_stopped", StringComparison.Ordinal) ||
                string.Equals(message, "transport_write_failed", StringComparison.Ordinal) ||
                string.Equals(message, "channel_closed", StringComparison.Ordinal);
        }

        private static bool TryGetMigrationDcId(CallOutcome outcome, out int dcId)
        {
            dcId = 0;
            if (outcome.Parameter > 0)
            {
                string kind = outcome.Kind ?? string.Empty;
                if (kind.IndexOf("Migrate", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    dcId = outcome.Parameter;
                    return true;
                }
            }

            string message = outcome.Message ?? string.Empty;
            int migrateIdx = message.IndexOf("_MIGRATE_", StringComparison.Ordinal);
            if (migrateIdx < 0)
            {
                return false;
            }

            string digits = message.Substring(migrateIdx + 9);
            for (int i = 0; i < digits.Length; i++)
            {
                char c = digits[i];
                if (c >= '0' && c <= '9')
                {
                    dcId = dcId * 10 + (c - '0');
                }
                else
                {
                    break;
                }
            }
            return dcId > 0;
        }

        private void RememberLoginDcHint(int dcId, string reason)
        {
            if (dcId <= 0 || _preferredDcStore == null)
            {
                return;
            }

            try
            {
                _preferredDcStore.SetLoginDcHint(dcId);
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "login DC hint persisted: dc=" + dcId + " reason=" + (reason ?? string.Empty));
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "login DC hint persist failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // Time-offset note (server_time - local_time, in seconds): the
        // value is captured at handshake time and saved alongside the
        // auth_key. Big magnitudes (hours, days) usually mean the
        // device clock is wrong vs Telegram's NTP-anchored clock —
        // this is INFORMATION, not corruption. Every subsequent RPC
        // must build msg_id from (local_time + ServerTimeOffset), so
        // as long as the channel uses the offset consistently, the
        // server accepts the msg_id even with a multi-hour skew.
        //
        // (Earlier we rejected any |offset| > 300 s here, which
        // discarded perfectly valid handshakes on devices whose clock
        // was off by an hour. The fix is to use the offset on the
        // wire, not to throw away the key.)
        private static bool IsUsableAuthKeyRecord(AccountAuthKeyRecord record)
        {
            if (record == null) return false;
            if (record.AuthKey == null || record.AuthKey.Length != 256) return false;
            if (record.AuthKeyId == 0) return false;
            return true;
        }

        private static AccountAuthKeyRecord CloneAuthKeyRecord(AccountAuthKeyRecord record)
        {
            if (record == null) return null;
            return new AccountAuthKeyRecord
            {
                AuthKey = record.AuthKey != null ? (byte[])record.AuthKey.Clone() : null,
                AuthKeyId = record.AuthKeyId,
                ServerSalt = record.ServerSalt,
                ServerTimeOffset = record.ServerTimeOffset
            };
        }

        private static void ClearAuthKeyRecord(AccountAuthKeyRecord record)
        {
            if (record == null || record.AuthKey == null) return;
            Array.Clear(record.AuthKey, 0, record.AuthKey.Length);
        }

        private static Task<bool> TaskFromBool(bool value)
        {
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(value);
            return tcs.Task;
        }

        private static void WriteLong(TlByteBuilder b, long v)
        {
            b.WriteInt64(v);
        }

        private static AccountError MapOutcomeToAccountError(CallOutcome outcome)
        {
            string kind = outcome.Kind ?? "Unknown";
            string msg = outcome.Message ?? string.Empty;
            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase) &&
                outcome.Parameter > 0)
            {
                return AccountError.PhoneNumberFlood(outcome.Parameter);
            }
            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return AccountError.NetworkError(msg);
            }
            if (outcome.Code == 401)
            {
                return AccountError.SessionExpired(msg);
            }
            int dcId;
            if (TryGetMigrationDcId(outcome, out dcId))
            {
                return AccountError.DcMigrationRequired(dcId);
            }
            if (IsAuthRestart(outcome))
            {
                return AccountError.AuthRestart(msg);
            }
            return AccountError.Unknown(msg);
        }

        private static UserFullResponse TryProjectSelfFromUserSlice(byte[] body)
        {
            if (body == null) return null;
            for (int i = 0; i + 4 <= body.Length; i += 4)
            {
                uint ctor = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));
                if (ctor != CtorUserA && ctor != CtorUserB && ctor != CtorUser214)
                {
                    continue;
                }

                try
                {
                    var sub = new byte[body.Length - (i + 4)];
                    Buffer.BlockCopy(body, i + 4, sub, 0, sub.Length);
                    var r = new TlByteReader(sub);
                    uint flags = r.ReadUInt32();
                    r.ReadUInt32();
                    long userId = r.ReadInt64();
                    if ((flags & (1u << 0)) != 0) r.ReadInt64();
                    string firstName = (flags & (1u << 1)) != 0 ? r.ReadString() : null;
                    string lastName = (flags & (1u << 2)) != 0 ? r.ReadString() : null;
                    string username = (flags & (1u << 3)) != 0 ? r.ReadString() : null;
                    string phone = (flags & (1u << 4)) != 0 ? r.ReadString() : null;

                    var dto = new UserFullResponse();
                    dto.UserId = userId;
                    dto.FirstName = firstName ?? string.Empty;
                    dto.LastName = lastName ?? string.Empty;
                    dto.Username = username ?? string.Empty;
                    dto.Phone = phone ?? string.Empty;
                    dto.Bio = string.Empty;
                    return dto;
                }
                catch
                {
                    // Try next candidate constructor offset.
                }
            }
            return null;
        }

        private sealed class AuthKeyRaceResult
        {
            public TelegramDcEndpoint Endpoint;
            public Result<AccountAuthKeyRecord, AccountError> KeyResult;
            public long ElapsedMs;
            public int AttemptedCount;
            public bool AbortWithoutSequentialFallback;
        }

        private struct CallOutcome
        {
            public bool Ok;
            public byte[] Bytes;
            public string Kind;
            public int Code;
            public string Message;
            public int Parameter;

            public static CallOutcome Success(byte[] bytes)
            {
                return new CallOutcome { Ok = true, Bytes = bytes };
            }

            public static CallOutcome Fail(string kind, int code, string message, int parameter)
            {
                return new CallOutcome
                {
                    Ok = false,
                    Kind = kind,
                    Code = code,
                    Message = message,
                    Parameter = parameter
                };
            }
        }
    }
}
