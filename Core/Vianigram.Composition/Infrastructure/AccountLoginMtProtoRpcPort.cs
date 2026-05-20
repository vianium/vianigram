// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
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

        // Account typed-method constructor ids.
        private const uint CtorBoolTrue = 0x997275b5u;
        private const uint CtorBoolFalse = 0xbc799737u;
        private const uint CtorInputUserSelf = 0xf7c1b13fu;
        private const uint CtorAuthExportLoginToken = 0xb7e085feu;
        private const uint CtorAuthImportLoginToken = 0x95ac5ce4u;
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

                if (existing != null && IsUsableAuthKeyRecord(existing))
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " skipped (already cached)");
                    ClearAuthKeyRecord(existing);
                    return;
                }

                if (existing != null)
                {
                    // Stale / corrupt cache entry — clear before regenerating.
                    ClearAuthKeyRecord(existing);
                }

                TelegramDcEndpoint[] endpoints = TelegramDcOptions.GetConnectionPlan(
                    dcId,
                    TelegramAppConfig.UseTestEnvironment,
                    null,
                    0);
                if (endpoints.Length == 0)
                {
                    EarlyLog.Write(
                        "Account.LoginMTProto",
                        "Prewarm DC#" + dcId + " skipped (no endpoint plan)");
                    return;
                }

                TelegramDcEndpoint endpoint = endpoints[0];
                EarlyLog.Write(
                    "Account.LoginMTProto",
                    "Prewarm DC#" + dcId + " handshake begin against " + endpoint);
                var sw = Stopwatch.StartNew();

                Result<AccountAuthKeyRecord, AccountError> keyResult;
                try
                {
                    keyResult = await GenerateAuthKeyWithDeadlineAsync(endpoint, ct)
                        .ConfigureAwait(false);
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

                try
                {
                    await _authKeyStore.SaveAsync(dcId, keyResult.Value, ct).ConfigureAwait(false);
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
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
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
                TelegramDcEndpoint[] endpoints = TelegramDcOptions.GetConnectionPlan(
                    targetDcId,
                    TelegramAppConfig.UseTestEnvironment,
                    null,
                    0);
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
                // race the prewarm started in OnNavigatedTo and end up
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

                    if (key != null && !IsUsableAuthKeyRecord(key))
                    {
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key DC#" + targetDcId + " cached but rejected as unusable" +
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
                        EarlyLog.Write(
                            "Account.LoginMTProto",
                            "auth_key DC#" + targetDcId + " cache HIT keyId=0x" + key.AuthKeyId.ToString("x16") +
                            " salt=" + key.ServerSalt.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            " time_offset=" + key.ServerTimeOffset.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                }

                for (int i = 0; i < endpoints.Length; i++)
                {
                    TelegramDcEndpoint endpoint = endpoints[i];
                    long keyElapsedMs = 0;
                    long channelOpenElapsedMs = 0;
                    try
                    {
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
                                TelegramDcOptions.ReportEndpointFailure(endpoint);
                                continue;
                            }

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

                        TimeSpan openBudget = string.Equals(keySource, "cache", StringComparison.Ordinal)
                            ? TelegramDcOptions.CachedOpenTimeout
                            : TelegramDcOptions.ChannelOpenTimeout;
                        var openSw = Stopwatch.StartNew();
                        var opened = await OpenLoginChannelWithDeadlineAsync(endpoint, key, openBudget, ct)
                            .ConfigureAwait(false);
                        openSw.Stop();
                        channelOpenElapsedMs = openSw.ElapsedMilliseconds;

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
                        }

                        if (oldChannel != null && !object.ReferenceEquals(oldChannel, opened))
                        {
                            try { oldChannel.Close(); }
                            catch (Exception) { }
                        }

                        TelegramDcOptions.ReportEndpointSuccess(endpoint);
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
                        TelegramDcOptions.ReportEndpointFailure(endpoint);
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
            Task<Result<AccountAuthKeyRecord, AccountError>> keyTask =
                _keyGen.GenerateAsync(endpoint.Host, endpoint.Port, ct);
            Task timeoutTask = Task.Delay(TelegramDcOptions.AuthKeyGenerationTimeout, ct);
            Task completed = await Task.WhenAny(keyTask, timeoutTask).ConfigureAwait(false);
            if (!object.ReferenceEquals(completed, keyTask))
            {
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                ObserveFault(keyTask);
                return Result<AccountAuthKeyRecord, AccountError>.Fail(
                    AccountError.NetworkError("auth_key generation timed out against " + endpoint.ToString()));
            }

            return await keyTask.ConfigureAwait(false);
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

        private static bool IsUsableAuthKeyRecord(AccountAuthKeyRecord record)
        {
            if (record == null) return false;
            if (record.AuthKey == null || record.AuthKey.Length != 256) return false;
            if (record.AuthKeyId == 0) return false;
            return true;
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
                if (ctor != CtorUserA && ctor != CtorUserB)
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
