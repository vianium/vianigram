// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProtoChannelAdapter.cs
//
// Adapter that fronts the native Vianigram.Core.MTProto MtProtoChannel WinRT
// component. Implements every per-context outbound
// IMtProtoRpcPort interface (Account / Chats / Messages / Sync) using
// explicit interface implementation: each context defines its own port
// shape and its own structured error type, so the same physical adapter
// projects four faces.
//
// Wiring: VianigramCompositionRoot.BuildPhase2Async opens the channel via
// MtProtoChannel::OpenAsync(host, port, authKeyBytes, authKeyId, salt,
// timeOffset) once the auth_key has been generated (either freshly via the
// AuthKeyGeneratorAdapter or loaded from the encrypted JsonAuthKeyStore),
// then constructs this adapter and registers it against every context's
// outbound interface in the service registry.
//
// Failure model: native CallAsync returns a structured RpcResult; on
// Success=false it carries (ErrorKind, ErrorCode, ErrorMessage,
// ErrorParameter). Each per-context CallAsync translates that to its own
// error type without throwing. Native exceptions (TCP reset, timeout,
// projection errors) are caught and translated into "Network" / "Unknown"
// kind errors. OperationCanceledException always propagates.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Vianigram.Account.Domain.Errors;
using Vianigram.Composition.Configuration;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using AccountAuthKeyRecord = Vianigram.Account.Ports.Outbound.AuthKeyRecord;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Real <c>IMtProtoRpcPort</c> adapter for every bounded context.
    /// Wraps a single <c>Vianigram.MTProto.MtProtoChannel</c> WinRT instance.
    /// One adapter per host process; the MtProtoChannel itself serializes
    /// concurrent CallAsyncs internally (per native contract).
    /// </summary>
    public sealed partial class MtProtoChannelAdapter
        : Vianigram.Account.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Account.Ports.Outbound.IMtProtoDcProvider
        , Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Contacts.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Notifications.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Settings.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Privacy.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Search.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Media.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Stickers.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.SecretChats.Ports.Outbound.IMtProtoRpcPort
        , Vianigram.Calls.Ports.Outbound.IMtProtoRpcPort
    {
        // Either _channel is non-null (direct ctor — used by tests/smoke runners
        // that fully synchronize on the live channel) or _deferred is non-null
        // (deferred-construction ctor — used by the composition root so
        // cold-launch first-paint isn't blocked on DH). Exactly one is set.
        // Once a deferred channel resolves we promote the result into _channel
        // so subsequent calls skip the deferred await fast-path entirely.
        private Vianigram.MTProto.MtProtoChannel _channel;
        private readonly DeferredMtProtoChannel _deferred;
        private readonly object _resolveGate = new object();
        private readonly object _connectionInitGate = new object();
        private readonly object _migrationGate = new object();
        private readonly AuthKeyGeneratorAdapter _migrationKeyGen;
        private readonly BridgeAuthKeyStore _migrationAuthKeyStore;
        private Task<bool> _migrationTask;
        private int _migrationTaskDcId;
        private bool _migrationTaskForceFreshKey;
        private bool _migrationTaskForceReopen;
        private int _migrationGeneration;
        private int _currentDcId;
        private string _currentDcHost;
        private int _currentDcPort;
        private bool _connectionInitialized;
        private Func<bool> _isAuthorizedForUserChannel;
        private int _authorizationGateLogged;
        private const uint InvokeWithLayerCtor = 0xda9b0d0d;
        private const uint InitConnectionCtor = 0xc1cd5ea9;
        private const int TelegramLayer = 214;
        private const int IncorrectServerSaltErrorCode = 48;
        // Native pool max is 8; each retry can land on a different session
        // whose local salt mirror has not been refreshed yet.
        private const int IncorrectServerSaltRetryCount = 8;

        // Shared per-(client,peer) access_hash cache. Optional: when null,
        // every InputUser/InputChannel/InputPeer{User,Channel} builder falls
        // back to access_hash=0 (the pre-cache behaviour).
        // The TypedStubs partial uses this through the _peerCache field.
        private readonly IPeerCache _peerCache;

        // Persistent cache of cross-DC authorization blobs minted by
        // auth.exportAuthorization. Wired post-construction via
        // ConfigureImportedAuthorizationCache so the cache only becomes
        // active once the Account aggregate is built and the user_id is
        // known. Optional: when null, MediaDc.ImportMediaAuthorizationCoreAsync
        // falls back to the live export+import path on every media DC.
        private Vianigram.Storage.Ports.Stubs.IImportedAuthorizationCacheStore _importedAuthCache;
        private Func<long> _importedAuthUserIdProvider;

        public event Action<Vianigram.MTProto.MtProtoChannel> ChannelChanged;

        public Vianigram.MTProto.MtProtoChannel CurrentChannelSnapshot
        {
            get { return IsAuthorizedForUserChannel() ? _channel : null; }
        }

        public MtProtoChannelAdapter(Vianigram.MTProto.MtProtoChannel channel)
            : this(channel, null)
        {
        }

        public MtProtoChannelAdapter(Vianigram.MTProto.MtProtoChannel channel, IPeerCache peerCache)
            : this(channel, null, peerCache, null, null, TelegramAppConfig.ActiveDcId, null, 0)
        {
        }

        public MtProtoChannelAdapter(
            Vianigram.MTProto.MtProtoChannel channel,
            IPeerCache peerCache,
            AuthKeyGeneratorAdapter migrationKeyGen,
            BridgeAuthKeyStore migrationAuthKeyStore,
            int currentDcId,
            string currentDcHost,
            int currentDcPort)
            : this(channel, null, peerCache, migrationKeyGen, migrationAuthKeyStore, currentDcId, currentDcHost, currentDcPort)
        {
        }

        private MtProtoChannelAdapter(
            Vianigram.MTProto.MtProtoChannel channel,
            DeferredMtProtoChannel deferred,
            IPeerCache peerCache,
            AuthKeyGeneratorAdapter migrationKeyGen,
            BridgeAuthKeyStore migrationAuthKeyStore,
            int currentDcId,
            string currentDcHost,
            int currentDcPort)
        {
            if (channel == null && deferred == null)
            {
                throw new ArgumentException("channel or deferred required");
            }
            if (channel != null && deferred != null)
            {
                throw new ArgumentException("channel and deferred are mutually exclusive");
            }
            _channel = channel;
            _deferred = deferred;
            _peerCache = peerCache;
            _migrationKeyGen = migrationKeyGen;
            _migrationAuthKeyStore = migrationAuthKeyStore;
            _currentDcId = currentDcId > 0 ? currentDcId : TelegramAppConfig.ActiveDcId;
            _currentDcHost = currentDcHost;
            _currentDcPort = currentDcPort;
        }

        /// <summary>
        /// Deferred ctor: defers the live channel acquisition behind a
        /// <see cref="DeferredMtProtoChannel"/>. The first port call awaits
        /// the wrapped task (subject to the deferred wrapper's max-wait
        /// budget); subsequent calls go straight to the now-resolved channel.
        /// Composition root uses this to return without blocking on DH.
        /// </summary>
        public MtProtoChannelAdapter(DeferredMtProtoChannel deferred)
            : this(deferred, null)
        {
        }

        public MtProtoChannelAdapter(DeferredMtProtoChannel deferred, IPeerCache peerCache)
            : this(null, deferred, peerCache, null, null, TelegramAppConfig.ActiveDcId, null, 0)
        {
        }

        public MtProtoChannelAdapter(
            DeferredMtProtoChannel deferred,
            IPeerCache peerCache,
            AuthKeyGeneratorAdapter migrationKeyGen,
            BridgeAuthKeyStore migrationAuthKeyStore,
            int currentDcId,
            string currentDcHost,
            int currentDcPort)
            : this(null, deferred, peerCache, migrationKeyGen, migrationAuthKeyStore, currentDcId, currentDcHost, currentDcPort)
        {
        }

        public int CurrentDcId
        {
            get
            {
                lock (_migrationGate)
                {
                    return _currentDcId > 0 ? _currentDcId : TelegramAppConfig.ActiveDcId;
                }
            }
        }

        // Resolves the live channel from the deferred wrapper on first touch.
        // Caches the result into _channel so subsequent calls skip the await
        // entirely. Called once-or-many depending on whether we beat the
        // resolution race; either way the underlying GetAsync short-circuits
        // when the handshake task is already complete.
        private async Task<Vianigram.MTProto.MtProtoChannel> GetChannelAsync(CancellationToken ct)
        {
            if (!IsAuthorizedForUserChannel())
            {
                LogAuthorizationGateBlocked("deferred resolve");
                throw new InvalidOperationException("Main MTProto channel is parked until the account is authorized.");
            }

            var cached = _channel;
            if (cached != null) return cached;

            Task<bool> inFlightMigration = null;
            lock (_migrationGate)
            {
                if (_migrationTask != null && !_migrationTask.IsCompleted)
                {
                    inFlightMigration = _migrationTask;
                }
            }

            if (inFlightMigration != null)
            {
                EarlyLog.Write(
                    "MTProto.Channel",
                    "channel null; awaiting in-flight migration before deferred resolution");
                try
                {
                    Task cancelTask = Task.Delay(Timeout.Infinite, ct);
                    Task completed = await Task.WhenAny(inFlightMigration, cancelTask).ConfigureAwait(false);
                    if (!object.ReferenceEquals(completed, inFlightMigration))
                    {
                        throw new OperationCanceledException(ct);
                    }

                    await inFlightMigration.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    EarlyLog.Write(
                        "MTProto.Channel",
                        "in-flight migration failed before deferred resolution: " +
                        ex.GetType().Name + ": " + ex.Message);
                }

                cached = _channel;
                if (cached != null) return cached;
            }
            if (_deferred == null)
            {
                // Should be unreachable — one of the two ctors must have run.
                throw new InvalidOperationException(
                    "MtProtoChannelAdapter has neither a direct channel nor a deferred wrapper.");
            }
            var resolved = await _deferred.GetAsync(ct).ConfigureAwait(false);
            // Promote into _channel under a small lock so concurrent first-
            // touches don't keep awaiting. Volatile would also work — we use
            // the lock to mirror the pattern elsewhere in this class.
            Vianigram.MTProto.MtProtoChannel promoted = null;
            Vianigram.MTProto.MtProtoChannel staleResolved = null;
            Vianigram.MTProto.MtProtoChannel result;
            lock (_resolveGate)
            {
                if (_channel == null)
                {
                    _channel = resolved;
                    promoted = resolved;
                }
                else if (!object.ReferenceEquals(_channel, resolved))
                {
                    staleResolved = resolved;
                }
                result = _channel;
            }
            if (staleResolved != null)
            {
                try { staleResolved.Close(); }
                catch (Exception) { }
            }
            if (promoted != null) PublishChannelChanged(promoted);
            return result;
        }

        public void SetAuthorizationGate(Func<bool> isAuthorized)
        {
            _isAuthorizedForUserChannel = isAuthorized;
        }

        /// <summary>
        /// Wires the persistent imported-authorization cache so subsequent
        /// media-DC bootstraps short-circuit <c>auth.exportAuthorization</c>
        /// on cache hit. Safe to call multiple times; the most recent
        /// configuration wins. Pass <paramref name="cache"/> = null to
        /// disable the cache (the adapter then falls back to the live
        /// export+import path on every media DC).
        /// </summary>
        public void ConfigureImportedAuthorizationCache(
            Vianigram.Storage.Ports.Stubs.IImportedAuthorizationCacheStore cache,
            Func<long> userIdProvider)
        {
            _importedAuthCache = cache;
            _importedAuthUserIdProvider = userIdProvider;
        }

        public void ParkAfterLogout()
        {
            Vianigram.MTProto.MtProtoChannel oldChannel = null;

            lock (_resolveGate)
            {
                oldChannel = _channel;
                _channel = null;
            }

            lock (_migrationGate)
            {
                unchecked { _migrationGeneration++; }
                _migrationTask = null;
                _migrationTaskDcId = 0;
                _migrationTaskForceFreshKey = false;
                _migrationTaskForceReopen = false;
            }

            ResetConnectionInitialized();
            if (oldChannel != null)
            {
                try { oldChannel.Close(); }
                catch (Exception) { }
            }
            CloseMediaDcSessions();

            PublishChannelChanged(null);
            EarlyLog.Write("MTProto.Channel", "main channel parked after logout");
        }

        private Task<bool> EnsureMigratedAsync(int targetDcId, bool forceFreshKey, CancellationToken ct)
        {
            return EnsureChannelAsync(targetDcId, forceFreshKey, false, ct);
        }

        /// <summary>
        /// Public migration hook. Called post-login by the host so the main
        /// channel re-binds to the user's home DC (where auth.signIn issued
        /// the authorised auth_key). Without this the Sync layer's first
        /// updates.getState fires on the default DC#2 with an unrelated
        /// freshly-generated auth_key and the server rejects it with
        /// AUTH_KEY_UNREGISTERED.
        ///
        /// <paramref name="forceFreshKey"/> is exposed so callers can force
        /// a DH re-handshake on the target DC, but the typical post-login
        /// path leaves it false: the home-DC auth_key was just persisted by
        /// AccountLoginMtProtoRpcPort and reusing the cache is the whole
        /// point — the cached key carries the active session.
        /// </summary>
        public Task<bool> MigrateToDcAsync(int targetDcId, bool forceFreshKey, CancellationToken ct)
        {
            return EnsureChannelAsync(targetDcId, forceFreshKey, false, ct);
        }

        /// <summary>
        /// Re-open the physical channel on <paramref name="targetDcId"/> using
        /// the persisted auth_key. This is intentionally different from a
        /// fresh migration: after QR/phone login the Account flow has just
        /// persisted the server-authorized key, while this adapter may still
        /// be holding an older anonymous channel for the same DC.
        /// </summary>
        public Task<bool> ReopenToDcWithPersistedAuthKeyAsync(int targetDcId, CancellationToken ct)
        {
            return EnsureChannelAsync(targetDcId, false, true, ct);
        }

        /// <summary>
        /// Force the active channel to reopen on the same DC using the
        /// persisted auth_key. Invoked by the proxy runtime sink after
        /// the user saves a new proxy descriptor so the change takes
        /// effect immediately without waiting for the next reconnect.
        ///
        /// Best-effort: the reopen runs through the same endpoint-failure
        /// fallback as a regular migration, so a misconfigured proxy will
        /// either succeed (if it works) or fall through to a network error
        /// the next CallAsync surfaces.
        /// </summary>
        public Task<bool> ReopenAfterProxyChangeAsync(CancellationToken ct)
        {
            int dc = CurrentDcId;
            if (dc <= 0) dc = TelegramAppConfig.ActiveDcId;
            return EnsureChannelAsync(dc, /* forceFreshKey */ false, /* forceReopen */ true, ct);
        }

        private Task<bool> EnsureReopenedAsync(CancellationToken ct)
        {
            return EnsureChannelAsync(CurrentDcId, false, true, ct);
        }

        private Task<bool> EnsureChannelAsync(
            int targetDcId,
            bool forceFreshKey,
            bool forceReopen,
            CancellationToken ct)
        {
            if (!IsAuthorizedForUserChannel())
            {
                LogAuthorizationGateBlocked("open DC#" + targetDcId);
                return TaskFromBool(false);
            }

            if (targetDcId <= 0 || targetDcId > 5)
            {
                return TaskFromBool(false);
            }
            if (_migrationKeyGen == null || _migrationAuthKeyStore == null)
            {
                return TaskFromBool(false);
            }

            lock (_migrationGate)
            {
                if (!forceFreshKey && !forceReopen && _currentDcId == targetDcId && _channel != null)
                {
                    return TaskFromBool(true);
                }
                if (!forceFreshKey && !forceReopen && _currentDcId == targetDcId && _deferred != null)
                {
                    return EnsureDeferredCurrentOrReopenAsync(targetDcId, ct);
                }

                if (_migrationTask != null &&
                    !_migrationTask.IsCompleted &&
                    _migrationTaskDcId == targetDcId &&
                    _migrationTaskForceFreshKey == forceFreshKey &&
                    _migrationTaskForceReopen == forceReopen)
                {
                    return _migrationTask;
                }

                _migrationTaskDcId = targetDcId;
                _migrationTaskForceFreshKey = forceFreshKey;
                _migrationTaskForceReopen = forceReopen;
                int generation = ++_migrationGeneration;
                _migrationTask = MigrateCoreAsync(targetDcId, forceFreshKey, generation, ct);
                return _migrationTask;
            }
        }

        private async Task<bool> EnsureDeferredCurrentOrReopenAsync(int targetDcId, CancellationToken ct)
        {
            Exception caughtEx = null;
            try
            {
                var channel = await GetChannelAsync(ct).ConfigureAwait(false);
                return channel != null;
            }
            catch (Exception ex)
            {
                caughtEx = ex;
            }

            // C# 5 disallows `await` inside a catch — finish the recovery here.
            Vianigram.Kernel.Logging.EarlyLog.Write(
                "MTProto.DC",
                "deferred open for DC#" + targetDcId + " failed; reopening: " +
                caughtEx.GetType().Name + ": " + caughtEx.Message);
            return await EnsureChannelAsync(targetDcId, false, true, ct).ConfigureAwait(false);
        }

        private async Task<bool> MigrateCoreAsync(
            int targetDcId,
            bool forceFreshKey,
            int generation,
            CancellationToken ct)
        {
            AccountAuthKeyRecord key = null;
            try
            {
                if (_migrationKeyGen == null || _migrationAuthKeyStore == null)
                {
                    Vianigram.Kernel.Logging.EarlyLog.Write(
                        "MTProto.DC",
                        "migration requested but key services are not wired for DC#" + targetDcId);
                    return false;
                }

                if (!IsMigrationCurrent(generation) || !IsAuthorizedForUserChannel())
                {
                    LogAuthorizationGateBlocked("migration start DC#" + targetDcId);
                    return false;
                }

                string preferredHost = null;
                int preferredPort = 0;
                lock (_migrationGate)
                {
                    if (_currentDcId == targetDcId)
                    {
                        preferredHost = _currentDcHost;
                        preferredPort = _currentDcPort;
                    }
                }

                TelegramDcEndpoint[] endpoints = TelegramDcOptions.GetConnectionPlan(
                    targetDcId,
                    TelegramAppConfig.UseTestEnvironment,
                    preferredHost,
                    preferredPort);
                if (endpoints.Length == 0)
                {
                    return false;
                }
                Vianigram.Kernel.Logging.EarlyLog.Write(
                    "MTProto.DC",
                    "opening DC#" + targetDcId +
                    " plan=" + TelegramDcOptions.DescribePlan(endpoints) +
                    " forceFresh=" + forceFreshKey.ToString());

                if (forceFreshKey)
                {
                    try
                    {
                        await _migrationAuthKeyStore.DeleteAsync(targetDcId, ct).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Best effort; a fresh SaveAsync below overwrites stale records.
                    }
                }
                else
                {
                    try
                    {
                        key = await _migrationAuthKeyStore.LoadAsync(targetDcId, ct).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        key = null;
                    }

                    if (key != null && !IsUsableAuthKeyRecord(key))
                    {
                        try
                        {
                            await _migrationAuthKeyStore.DeleteAsync(targetDcId, ct).ConfigureAwait(false);
                        }
                        catch (Exception)
                        {
                        }
                        ClearAuthKeyRecord(key);
                        key = null;
                    }
                }

                for (int i = 0; i < endpoints.Length; i++)
                {
                    TelegramDcEndpoint endpoint = endpoints[i];
                    try
                    {
                        if (key == null)
                        {
                            var keySw = Stopwatch.StartNew();
                            var keyResult = await GenerateMigrationKeyWithDeadlineAsync(endpoint, ct).ConfigureAwait(false);
                            keySw.Stop();
                            if (!keyResult.IsOk || keyResult.Value == null || !IsUsableAuthKeyRecord(keyResult.Value))
                            {
                                string detail = keyResult.IsFail && keyResult.Error != null
                                    ? keyResult.Error.ToString()
                                    : "no key returned";
                                Vianigram.Kernel.Logging.EarlyLog.Write(
                                    "MTProto.DC",
                                    "auth_key generation failed for DC#" + targetDcId +
                                    " endpoint=" + endpoint.ToString() +
                                    " elapsed=" + keySw.ElapsedMilliseconds + "ms: " + detail);
                                TelegramDcOptions.ReportEndpointFailure(endpoint);
                                continue;
                            }

                            key = keyResult.Value;
                            if (!IsMigrationCurrent(generation) || !IsAuthorizedForUserChannel())
                            {
                                LogAuthorizationGateBlocked("save generated key DC#" + targetDcId);
                                ClearAuthKeyRecord(key);
                                key = null;
                                return false;
                            }

                            try
                            {
                                await _migrationAuthKeyStore.SaveAsync(targetDcId, key, ct).ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                                // Keep the in-memory key for this process; persistence can retry on the next migration.
                            }
                        }

                        var openSw = Stopwatch.StartNew();
                        var migratedChannel = await OpenChannelWithDeadlineAsync(endpoint, key, ct).ConfigureAwait(false);
                        openSw.Stop();
                        if (migratedChannel == null)
                        {
                            Vianigram.Kernel.Logging.EarlyLog.Write(
                                "MTProto.DC",
                                "OpenAsync timed out/null for DC#" + targetDcId +
                                " endpoint=" + endpoint.ToString() +
                                " elapsed=" + openSw.ElapsedMilliseconds + "ms");
                            TelegramDcOptions.ReportEndpointFailure(endpoint);
                            continue;
                        }

                        if (!IsMigrationCurrent(generation) || !IsAuthorizedForUserChannel())
                        {
                            try { migratedChannel.Close(); }
                            catch (Exception) { }
                            LogAuthorizationGateBlocked("publish migrated channel DC#" + targetDcId);
                            return false;
                        }

                        Vianigram.MTProto.MtProtoChannel oldChannel = null;
                        lock (_resolveGate)
                        {
                            oldChannel = _channel;
                            _channel = migratedChannel;
                        }

                        lock (_migrationGate)
                        {
                            _currentDcId = targetDcId;
                            _currentDcHost = endpoint.Host;
                            _currentDcPort = endpoint.Port;
                        }
                        ResetConnectionInitialized();

                        if (oldChannel != null && !object.ReferenceEquals(oldChannel, migratedChannel))
                        {
                            try { oldChannel.Close(); }
                            catch (Exception) { }
                        }
                        PublishChannelChanged(migratedChannel);

                        TelegramDcOptions.ReportEndpointSuccess(endpoint);
                        Vianigram.Kernel.Logging.EarlyLog.Write(
                            "MTProto.DC",
                            "opened DC#" + targetDcId +
                            " endpoint=" + endpoint.ToString() +
                            " auth_key_id=0x" + key.AuthKeyId.ToString("x16"));

                        return true;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        TelegramDcOptions.ReportEndpointFailure(endpoint);
                        Vianigram.Kernel.Logging.EarlyLog.Write(
                            "MTProto.DC",
                            "open failed for DC#" + targetDcId +
                            " endpoint=" + endpoint.ToString() + ": " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }

                Vianigram.Kernel.Logging.EarlyLog.Write(
                    "MTProto.DC",
                    "all endpoints failed for DC#" + targetDcId);
                return false;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Vianigram.Kernel.Logging.EarlyLog.Write(
                    "MTProto.DC",
                    "open failed for DC#" + targetDcId + ": " +
                    ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            finally
            {
                ClearAuthKeyRecord(key);
                lock (_migrationGate)
                {
                    if (_migrationGeneration == generation)
                    {
                        _migrationTask = null;
                        _migrationTaskDcId = 0;
                        _migrationTaskForceFreshKey = false;
                        _migrationTaskForceReopen = false;
                    }
                }
            }
        }

        private async Task<Result<AccountAuthKeyRecord, AccountError>> GenerateMigrationKeyWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            CancellationToken ct)
        {
            // Use the DC-aware overload so the obfuscated MTProxy init
            // packet carries the migration target DC id. Falls through
            // to the same code path as GenerateAsync on the direct-dial
            // branch — the dcId is consulted only by the transport
            // factory when an MTProxy is configured.
            Task<Result<AccountAuthKeyRecord, AccountError>> keyTask =
                _migrationKeyGen.GenerateForDcAsync(endpoint.Host, endpoint.Port, _migrationTaskDcId, ct);
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

        private async Task<Vianigram.MTProto.MtProtoChannel> OpenChannelWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            AccountAuthKeyRecord key,
            CancellationToken ct)
        {
            // DC-aware open so the obfuscated MTProxy init packet routes
            // through the proxy to the correct upstream DC. Uses the
            // migration-task's target dc_id (captured by EnsureChannelAsync
            // before the loop begins).
            Task<Vianigram.MTProto.MtProtoChannel> openTask = Vianigram.MTProto.MtProtoChannel
                .OpenWithDcAsync(
                    endpoint.Host,
                    endpoint.Port,
                    _migrationTaskDcId > 0 ? _migrationTaskDcId : 2,
                    key.AuthKey,
                    key.AuthKeyId,
                    key.ServerSalt,
                    key.ServerTimeOffset)
                .AsTask(ct);
            Task timeoutTask = Task.Delay(TelegramDcOptions.ChannelOpenTimeout, ct);
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

        // ---------- Account port ----------

        async Task<Result<byte[], Vianigram.Account.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Account.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Account.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Account.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Account.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Chats port (throw-style by contract) ----------

        async Task<byte[]> Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort.CallAsync(
            byte[] payload, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(payload, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return outcome.Bytes;
            }
            // The Chats port contract demands throwing on RPC error; the handler
            // catches and translates to ChatError. We pack the structured fields
            // into the message so handlers can parse them if needed.
            throw new MtProtoRpcException(outcome.Kind, outcome.Code, outcome.Message, outcome.Parameter);
        }

        // ---------- Messages port ----------

        async Task<Result<byte[], Vianigram.Messages.Domain.MessageError>>
            Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] tlRequest, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(tlRequest, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Messages.Domain.MessageError>.Ok(outcome.Bytes);
            }
            return Result<byte[], Vianigram.Messages.Domain.MessageError>.Fail(MapToMessageError(outcome));
        }

        // ---------- Sync port (note: InvokeAsync, not CallAsync) ----------

        async Task<Result<byte[], Vianigram.Sync.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort.InvokeAsync(
                byte[] requestBody, string methodName, CancellationToken ct)
        {
            // methodName is for tracing/diagnostics on the managed side; the wire
            // request itself is fully serialized in requestBody.
            CallOutcome outcome = await CallInternalAsync(requestBody, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Sync.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Sync.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Sync.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Shared call core ----------

        private async Task<CallOutcome> CallInternalAsync(byte[] requestBytes, CancellationToken ct)
        {
            if (requestBytes == null) throw new ArgumentNullException("requestBytes");

            uint topCtor = PeekCtor(requestBytes);
            EarlyLog.Write(
                "MTProto.Channel",
                "CallInternal begin ctor=0x" + topCtor.ToString("x8") +
                " size=" + requestBytes.Length +
                " dc=" + CurrentDcId +
                " connInited=" + IsConnectionInitialized());

            if (!IsAuthorizedForUserChannel())
            {
                LogAuthorizationGateBlocked("CallInternal ctor=0x" + topCtor.ToString("x8"));
                return CallOutcome.Fail("AuthKeyInvalid", 401, "AUTH_KEY_UNREGISTERED", 0);
            }

            try
            {
                // If this adapter was built with the deferred ctor and DH
                // hasn't landed yet, await the resolution here. After the
                // first successful resolve _channel is non-null and the
                // helper short-circuits to a direct return.
                Vianigram.MTProto.MtProtoChannel chan = _channel;
                if (chan == null)
                {
                    EarlyLog.Write("MTProto.Channel", "channel null; awaiting deferred resolution");
                    chan = await GetChannelAsync(ct).ConfigureAwait(false);
                }

                bool wrappedForInit = !IsConnectionInitialized();
                bool retriedInit = false;
                bool retriedMigration = false;
                bool retriedAuthKey = false;
                bool retriedChannelReopen = false;
                byte[] requestToSend = wrappedForInit
                    ? WrapConnectionInit(requestBytes)
                    : requestBytes;

                if (wrappedForInit)
                {
                    EarlyLog.Write(
                        "MTProto.Channel",
                        "wrapping first RPC in initConnection ctor=0x" + topCtor.ToString("x8") +
                        " innerSize=" + requestBytes.Length +
                        " wrappedSize=" + requestToSend.Length);
                }

                for (int attempt = 0; ; attempt++)
                {
                    EarlyLog.Write(
                        "MTProto.Channel",
                        "attempt #" + (attempt + 1) + " native CallAsync begin size=" + requestToSend.Length);
                    var rpcSw = Stopwatch.StartNew();
                    Vianigram.MTProto.RpcResult result = await chan
                        .CallAsync(requestToSend)
                        .AsTask(ct)
                        .ConfigureAwait(false);
                    rpcSw.Stop();
                    EarlyLog.Write(
                        "MTProto.Channel",
                        "attempt #" + (attempt + 1) + " native CallAsync returned elapsed=" +
                        rpcSw.ElapsedMilliseconds + "ms" +
                        " success=" + (result != null ? result.Success.ToString() : "(null)"));

                    if (result == null)
                    {
                        return CallOutcome.Fail("Unknown", -1, "Native CallAsync returned null.", 0);
                    }

                    if (!result.Success)
                    {
                        CallOutcome failure = CallOutcome.Fail(
                            result.ErrorKind ?? "Unknown",
                            result.ErrorCode,
                            result.ErrorMessage ?? string.Empty,
                            result.ErrorParameter);

                        if (attempt < IncorrectServerSaltRetryCount && IsIncorrectServerSalt(failure))
                        {
                            continue;
                        }

                        if (!retriedChannelReopen && IsTransientChannelExit(failure))
                        {
                            bool reopened = await EnsureReopenedAsync(ct).ConfigureAwait(false);
                            if (reopened)
                            {
                                retriedChannelReopen = true;
                                chan = await GetChannelAsync(ct).ConfigureAwait(false);
                                wrappedForInit = true;
                                retriedInit = false;
                                requestToSend = WrapConnectionInit(requestBytes);
                                continue;
                            }
                        }

                        int migrateToDcId;
                        if (!retriedMigration && TryGetMigrationDcId(failure, out migrateToDcId))
                        {
                            bool migrated = await EnsureMigratedAsync(migrateToDcId, false, ct).ConfigureAwait(false);
                            if (migrated)
                            {
                                retriedMigration = true;
                                chan = await GetChannelAsync(ct).ConfigureAwait(false);
                                wrappedForInit = true;
                                retriedInit = false;
                                requestToSend = WrapConnectionInit(requestBytes);
                                continue;
                            }
                        }

                        if (!retriedAuthKey && IsAuthKeyInvalid(failure))
                        {
                            bool reopened = await EnsureChannelAsync(CurrentDcId, false, true, ct).ConfigureAwait(false);
                            if (reopened)
                            {
                                retriedAuthKey = true;
                                chan = await GetChannelAsync(ct).ConfigureAwait(false);
                                wrappedForInit = true;
                                retriedInit = false;
                                requestToSend = WrapConnectionInit(requestBytes);
                                continue;
                            }
                        }

                        if (!wrappedForInit && !retriedInit && IsConnectionNotInited(failure))
                        {
                            ResetConnectionInitialized();
                            wrappedForInit = true;
                            retriedInit = true;
                            requestToSend = WrapConnectionInit(requestBytes);
                            continue;
                        }

                        if (wrappedForInit && !IsConnectionNotInited(failure))
                        {
                            MarkConnectionInitialized();
                        }

                        return failure;
                    }

                    byte[] body = result.ResultBytes;
                    if (body == null) body = new byte[0];

                    // Telegram gzips large responses (messages.getDialogs,
                    // users.getFullUser, channels.getDifference, …). Native
                    // MTProto on WP 8.1 has no zlib so it forwards the
                    // gzip_packed envelope to us — inflate before handing
                    // anything off to the typed decoders.
                    int beforeLen = body.Length;
                    body = GzipResponseDecoder.MaybeInflate(body);
                    if (body.Length != beforeLen)
                    {
                        EarlyLog.Write(
                            "MTProto.Channel",
                            "gzip_packed inflated " + beforeLen + " -> " + body.Length + " bytes");
                    }

                    EarlyLog.Write(
                        "MTProto.Channel",
                        "attempt #" + (attempt + 1) + " success bytes=" + body.Length +
                        " responseCtor=0x" + PeekCtor(body).ToString("x8"));
                    if (wrappedForInit)
                    {
                        MarkConnectionInitialized();
                        EarlyLog.Write("MTProto.Channel", "connection marked initialized");
                    }
                    // Every successful response gets a permissive peer-cache
                    // hydration pass. The implementation lives in the
                    // TypedStubs partial (HydratePeerCacheFromResponse).
                    HydratePeerCacheFromResponse(body);
                    return CallOutcome.Success(body);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CallOutcome.Fail("Network", -1, ex.GetType().Name + ": " + ex.Message, 0);
            }
        }

        // Zero-copy variant. Mirrors CallInternalAsync above but routes
        // through MtProtoChannel.CallBufferAsync(IBuffer) and surfaces
        // the IBuffer reply on the success path. The byte[] field of the
        // CallOutcome is left null when the buffer path succeeds — callers
        // branch on (outcome.ResultBufferOpt != null) to pick which payload
        // field is populated. Used only by the Media outbound port today;
        // the other twelve contexts stay on the byte[] path.
        private async Task<CallOutcome> CallInternalBufferAsync(IBuffer requestBuffer, CancellationToken ct)
        {
            if (requestBuffer == null) throw new ArgumentNullException("requestBuffer");

            if (!IsAuthorizedForUserChannel())
            {
                LogAuthorizationGateBlocked("CallInternalBuffer");
                return CallOutcome.Fail("AuthKeyInvalid", 401, "AUTH_KEY_UNREGISTERED", 0);
            }

            try
            {
                Vianigram.MTProto.MtProtoChannel chan = _channel;
                if (chan == null) chan = await GetChannelAsync(ct).ConfigureAwait(false);

                bool wrappedForInit = !IsConnectionInitialized();
                bool retriedInit = false;
                bool retriedMigration = false;
                bool retriedAuthKey = false;
                bool retriedChannelReopen = false;
                byte[] requestBytes = null;
                IBuffer requestToSend = requestBuffer;
                if (wrappedForInit)
                {
                    requestBytes = BufferToBytes(requestBuffer);
                    requestToSend = Windows.Security.Cryptography.CryptographicBuffer
                        .CreateFromByteArray(WrapConnectionInit(requestBytes));
                }

                for (int attempt = 0; ; attempt++)
                {
                    Vianigram.MTProto.RpcResult result = await chan
                        .CallBufferAsync(requestToSend)
                        .AsTask(ct)
                        .ConfigureAwait(false);

                    if (result == null)
                    {
                        return CallOutcome.Fail("Unknown", -1, "Native CallBufferAsync returned null.", 0);
                    }

                    if (!result.Success)
                    {
                        CallOutcome failure = CallOutcome.Fail(
                            result.ErrorKind ?? "Unknown",
                            result.ErrorCode,
                            result.ErrorMessage ?? string.Empty,
                            result.ErrorParameter);

                        if (attempt < IncorrectServerSaltRetryCount && IsIncorrectServerSalt(failure))
                        {
                            continue;
                        }

                        if (!retriedChannelReopen && IsTransientChannelExit(failure))
                        {
                            bool reopened = await EnsureReopenedAsync(ct).ConfigureAwait(false);
                            if (reopened)
                            {
                                retriedChannelReopen = true;
                                chan = await GetChannelAsync(ct).ConfigureAwait(false);
                                wrappedForInit = true;
                                retriedInit = false;
                                if (requestBytes == null) requestBytes = BufferToBytes(requestBuffer);
                                requestToSend = Windows.Security.Cryptography.CryptographicBuffer
                                    .CreateFromByteArray(WrapConnectionInit(requestBytes));
                                continue;
                            }
                        }

                        int migrateToDcId;
                        if (!retriedMigration && TryGetMigrationDcId(failure, out migrateToDcId))
                        {
                            bool migrated = await EnsureMigratedAsync(migrateToDcId, false, ct).ConfigureAwait(false);
                            if (migrated)
                            {
                                retriedMigration = true;
                                chan = await GetChannelAsync(ct).ConfigureAwait(false);
                                wrappedForInit = true;
                                retriedInit = false;
                                if (requestBytes == null) requestBytes = BufferToBytes(requestBuffer);
                                requestToSend = Windows.Security.Cryptography.CryptographicBuffer
                                    .CreateFromByteArray(WrapConnectionInit(requestBytes));
                                continue;
                            }
                        }

                        if (!retriedAuthKey && IsAuthKeyInvalid(failure))
                        {
                            bool reopened = await EnsureChannelAsync(CurrentDcId, false, true, ct).ConfigureAwait(false);
                            if (reopened)
                            {
                                retriedAuthKey = true;
                                chan = await GetChannelAsync(ct).ConfigureAwait(false);
                                wrappedForInit = true;
                                retriedInit = false;
                                if (requestBytes == null) requestBytes = BufferToBytes(requestBuffer);
                                requestToSend = Windows.Security.Cryptography.CryptographicBuffer
                                    .CreateFromByteArray(WrapConnectionInit(requestBytes));
                                continue;
                            }
                        }

                        if (!wrappedForInit && !retriedInit && IsConnectionNotInited(failure))
                        {
                            ResetConnectionInitialized();
                            wrappedForInit = true;
                            retriedInit = true;
                            if (requestBytes == null) requestBytes = BufferToBytes(requestBuffer);
                            requestToSend = Windows.Security.Cryptography.CryptographicBuffer
                                .CreateFromByteArray(WrapConnectionInit(requestBytes));
                            continue;
                        }

                        if (wrappedForInit && !IsConnectionNotInited(failure))
                        {
                            MarkConnectionInitialized();
                        }

                        return failure;
                    }

                    if (wrappedForInit)
                    {
                        MarkConnectionInitialized();
                    }

                    // Native invariant: success on the buffer path populates
                    // ResultBuffer (and leaves ResultBytes null). Defensive
                    // fallback: if for any reason the buffer is null we still
                    // surface success with an empty IBuffer so the caller
                    // doesn't have to special-case nullable IBuffer.
                    IBuffer body = result.ResultBuffer;
                    if (body == null)
                    {
                        body = Windows.Security.Cryptography.CryptographicBuffer
                            .CreateFromByteArray(new byte[0]);
                    }
                    return CallOutcome.SuccessBuffer(body);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return CallOutcome.Fail("Network", -1, ex.GetType().Name + ": " + ex.Message, 0);
            }
        }

        private static Vianigram.Messages.Domain.MessageError MapToMessageError(CallOutcome outcome)
        {
            // Best-effort mapping from rpc_error kind to MessageError.
            // FloodWait is the only kind with a first-class field; everything
            // else lands as NetworkFailed or Unauthorized depending on the code.
            string kind = outcome.Kind ?? "Unknown";
            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase) && outcome.Parameter > 0)
            {
                return Vianigram.Messages.Domain.MessageError.FloodWait(outcome.Parameter);
            }
            if (outcome.Code == 401)
            {
                return Vianigram.Messages.Domain.MessageError.Unauthorized(outcome.Message ?? "auth required");
            }
            return Vianigram.Messages.Domain.MessageError.NetworkFailed(
                outcome.Message ?? ("RPC error " + outcome.Code));
        }

        private static bool IsIncorrectServerSalt(CallOutcome outcome)
        {
            return outcome.Code == IncorrectServerSaltErrorCode;
        }

        private static bool TryGetMigrationDcId(CallOutcome outcome, out int dcId)
        {
            dcId = 0;
            if (IsFileMigration(outcome))
            {
                EarlyLog.Write(
                    "MTProto.Channel",
                    "FILE_MIGRATE ignored on shared user channel; preserving current DC.");
                return false;
            }

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

        private static bool IsFileMigration(CallOutcome outcome)
        {
            string kind = outcome.Kind ?? string.Empty;
            if (kind.IndexOf("FileMigrate", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            string message = outcome.Message ?? string.Empty;
            return message.IndexOf("FILE_MIGRATE_", StringComparison.Ordinal) >= 0;
        }

        private static bool IsAuthKeyInvalid(CallOutcome outcome)
        {
            if (outcome.Code != 401)
            {
                return false;
            }

            string message = outcome.Message ?? string.Empty;
            return message.IndexOf("AUTH_KEY_", StringComparison.Ordinal) >= 0 ||
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

        private static bool IsConnectionNotInited(CallOutcome outcome)
        {
            return outcome.Code == 400 &&
                string.Equals(outcome.Message, "CONNECTION_NOT_INITED", StringComparison.Ordinal);
        }

        private bool IsConnectionInitialized()
        {
            lock (_connectionInitGate)
            {
                return _connectionInitialized;
            }
        }

        private void MarkConnectionInitialized()
        {
            lock (_connectionInitGate)
            {
                _connectionInitialized = true;
            }
        }

        private void ResetConnectionInitialized()
        {
            lock (_connectionInitGate)
            {
                _connectionInitialized = false;
            }
        }

        private bool IsAuthorizedForUserChannel()
        {
            Func<bool> gate = _isAuthorizedForUserChannel;
            if (gate == null)
            {
                return true;
            }

            try
            {
                bool allowed = gate();
                if (allowed && Interlocked.Exchange(ref _authorizationGateLogged, 0) != 0)
                {
                    EarlyLog.Write("MTProto.Channel", "authorization gate reopened");
                }
                return allowed;
            }
            catch
            {
                return false;
            }
        }

        private void LogAuthorizationGateBlocked(string reason)
        {
            if (Interlocked.Exchange(ref _authorizationGateLogged, 1) != 0)
            {
                return;
            }

            EarlyLog.Write(
                "MTProto.Channel",
                "authorization gate blocked main-channel work: " + (reason ?? string.Empty));
        }

        private bool IsMigrationCurrent(int generation)
        {
            lock (_migrationGate)
            {
                return _migrationGeneration == generation;
            }
        }

        private void PublishChannelChanged(Vianigram.MTProto.MtProtoChannel channel)
        {
            Action<Vianigram.MTProto.MtProtoChannel> handler = ChannelChanged;
            if (handler == null) return;
            try
            {
                handler(channel);
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MTProto.Channel", "ChannelChanged subscriber threw: "
                    + ex.GetType().Name + ": " + ex.Message);
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

        private static bool IsUsableAuthKeyRecord(Vianigram.Account.Ports.Outbound.AuthKeyRecord record)
        {
            if (record == null) return false;
            if (record.AuthKey == null || record.AuthKey.Length != 256) return false;
            if (record.AuthKeyId == 0) return false;
            return true;
        }

        private static void ClearAuthKeyRecord(Vianigram.Account.Ports.Outbound.AuthKeyRecord record)
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

        private static byte[] BufferToBytes(IBuffer buffer)
        {
            if (buffer == null || buffer.Length == 0)
            {
                return new byte[0];
            }

            if (buffer.Length > int.MaxValue)
            {
                throw new InvalidOperationException("IBuffer is too large to wrap in initConnection.");
            }

            byte[] bytes = new byte[(int)buffer.Length];
            DataReader reader = DataReader.FromBuffer(buffer);
            reader.ReadBytes(bytes);
            return bytes;
        }

        // Tiny POCO so each per-context method shares the same parsing without
        // leaking a type that imports any specific bounded-context namespace.
        // Carries an optional ResultBufferOpt for the Media zero-copy IBuffer
        // reply path. On the byte[] path Bytes is set and
        // ResultBufferOpt is null; on the buffer path it's the inverse — the
        // Media adapter reads ResultBufferOpt and the other contexts read
        // Bytes.
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

        private struct CallOutcome
        {
            public bool Ok;
            public byte[] Bytes;
            public IBuffer ResultBufferOpt;
            public string Kind;
            public int Code;
            public string Message;
            public int Parameter;

            public static CallOutcome Success(byte[] bytes)
            {
                return new CallOutcome { Ok = true, Bytes = bytes, ResultBufferOpt = null };
            }

            public static CallOutcome SuccessBuffer(IBuffer buffer)
            {
                return new CallOutcome { Ok = true, Bytes = null, ResultBufferOpt = buffer };
            }

            public static CallOutcome Fail(string kind, int code, string message, int parameter)
            {
                return new CallOutcome
                {
                    Ok = false,
                    Bytes = null,
                    ResultBufferOpt = null,
                    Kind = kind,
                    Code = code,
                    Message = message,
                    Parameter = parameter
                };
            }
        }
    }

    /// <summary>
    /// Exception thrown by the Chats-flavoured CallAsync (its contract is
    /// throw-style, not Result-style). Carries the structured rpc_error
    /// fields so the handler can translate to a typed ChatError.
    /// </summary>
    public sealed class MtProtoRpcException : Exception
    {
        public MtProtoRpcException(string kind, int code, string message, int parameter)
            : base(BuildMessage(kind, code, message, parameter))
        {
            Kind = kind ?? "Unknown";
            Code = code;
            ServerMessage = message ?? string.Empty;
            Parameter = parameter;
        }

        public string Kind { get; private set; }
        public int Code { get; private set; }
        public string ServerMessage { get; private set; }
        public int Parameter { get; private set; }

        private static string BuildMessage(string kind, int code, string message, int parameter)
        {
            return (kind ?? "?") + "[" + code + "] " + (message ?? "") + " param=" + parameter;
        }
    }
}
