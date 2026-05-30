// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Vianigram.Account.Domain.Errors;
using Vianigram.Composition.Configuration;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using AccountAuthKeyRecord = Vianigram.Account.Ports.Outbound.AuthKeyRecord;

namespace Vianigram.Composition.Infrastructure
{
    public sealed partial class MtProtoChannelAdapter
    {
        private const uint CtorAuthExportAuthorization = 0xe5bfffcdu;
        private const uint CtorAuthImportAuthorization = 0xa57a7dadu;
        private const uint CtorAuthExportedAuthorization = 0xb434e2b8u;

        private readonly object _mediaDcGate = new object();
        private readonly Dictionary<int, MediaDcSession> _mediaDcSessions =
            new Dictionary<int, MediaDcSession>();
        private readonly Dictionary<int, Task<MediaDcSession>> _mediaDcOpenTasks =
            new Dictionary<int, Task<MediaDcSession>>();

        private static int ToMediaProxyDcId(int targetDcId)
        {
            return targetDcId > 0 ? -targetDcId : targetDcId;
        }

        private async Task<CallOutcome> CallMediaInternalAsync(
            byte[] requestBytes,
            int targetDcId,
            CancellationToken ct)
        {
            if (requestBytes == null) throw new ArgumentNullException("requestBytes");

            int dcId = targetDcId;
            bool retriedFileMigration = false;

            for (;;)
            {
                if (dcId <= 0 || dcId == CurrentDcId)
                {
                    CallOutcome mainOutcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
                    int migratedDcId;
                    if (!mainOutcome.Ok &&
                        !retriedFileMigration &&
                        TryGetAnyMigrationDcId(mainOutcome, out migratedDcId) &&
                        migratedDcId > 0 &&
                        migratedDcId != CurrentDcId)
                    {
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "routing FILE_MIGRATE to media DC#" + migratedDcId);
                        dcId = migratedDcId;
                        retriedFileMigration = true;
                        continue;
                    }

                    return mainOutcome;
                }

                MediaDcSession session = await GetOrOpenMediaDcSessionAsync(dcId, ct).ConfigureAwait(false);
                if (session == null || session.Channel == null)
                {
                    return CallOutcome.Fail(
                        "Network",
                        -1,
                        "media DC#" + dcId + " channel could not be opened",
                        0);
                }

                CallOutcome auth = await EnsureMediaAuthorizationImportedAsync(session, dcId, ct)
                    .ConfigureAwait(false);
                if (!auth.Ok)
                {
                    return auth;
                }

                CallOutcome outcome = await CallOnMediaSessionAsync(session, requestBytes, ct)
                    .ConfigureAwait(false);
                if (outcome.Ok)
                {
                    return outcome;
                }

                int nextDcId;
                if (!retriedFileMigration &&
                    TryGetAnyMigrationDcId(outcome, out nextDcId) &&
                    nextDcId > 0 &&
                    nextDcId != dcId)
                {
                    EarlyLog.Write(
                        "MTProto.MediaDC",
                        "media RPC redirected DC#" + dcId + " -> DC#" + nextDcId);
                    dcId = nextDcId;
                    retriedFileMigration = true;
                    continue;
                }

                return outcome;
            }
        }

        private async Task<CallOutcome> CallMediaInternalBufferAsync(
            IBuffer requestBuffer,
            int targetDcId,
            CancellationToken ct)
        {
            if (requestBuffer == null) throw new ArgumentNullException("requestBuffer");

            if (targetDcId <= 0 || targetDcId == CurrentDcId)
            {
                return await CallInternalBufferAsync(requestBuffer, ct).ConfigureAwait(false);
            }

            byte[] requestBytes = BufferToBytes(requestBuffer);
            CallOutcome outcome = await CallMediaInternalAsync(requestBytes, targetDcId, ct)
                .ConfigureAwait(false);
            if (!outcome.Ok)
            {
                return outcome;
            }

            return CallOutcome.SuccessBuffer(
                CryptographicBuffer.CreateFromByteArray(outcome.Bytes ?? new byte[0]));
        }

        private Task<MediaDcSession> GetOrOpenMediaDcSessionAsync(int targetDcId, CancellationToken ct)
        {
            if (targetDcId <= 0 || targetDcId > 5)
            {
                return TaskFromMediaDcSession(null);
            }

            if (_migrationKeyGen == null || _migrationAuthKeyStore == null)
            {
                return TaskFromMediaDcSession(null);
            }

            lock (_mediaDcGate)
            {
                MediaDcSession existing;
                if (_mediaDcSessions.TryGetValue(targetDcId, out existing) &&
                    existing != null &&
                    existing.Channel != null)
                {
                    return TaskFromMediaDcSession(existing);
                }

                Task<MediaDcSession> openTask;
                if (_mediaDcOpenTasks.TryGetValue(targetDcId, out openTask) &&
                    openTask != null)
                {
                    return openTask;
                }

                openTask = OpenAndCacheMediaDcSessionAsync(targetDcId, ct);
                _mediaDcOpenTasks[targetDcId] = openTask;
                return openTask;
            }
        }

        /// <summary>
        /// Eagerly open a media-DC session and (when the imported-auth
        /// cache + user_id provider are wired) import the cached
        /// authorization blob, so a subsequent media RPC against
        /// <paramref name="targetDcId"/> finds a ready session and skips
        /// both the TCP open and the auth.exportAuthorization round-trip.
        ///
        /// This is the H half of the cold-start optimisation pair: it
        /// pre-warms the typical media DCs (DC#2 for the EU avatar/file
        /// cluster, DC#4 for the AMS cluster) right after the user is
        /// authorised, while the first-paint code is still rendering
        /// ChatListPage. Idempotent — concurrent pre-warm calls coalesce
        /// on the same in-flight open task. Best-effort: failures are
        /// swallowed and the next demand call retries through the
        /// regular open path.
        /// </summary>
        public async Task PrewarmMediaDcAsync(int targetDcId, CancellationToken ct)
        {
            if (targetDcId <= 0 || targetDcId > 5) return;
            if (_migrationKeyGen == null || _migrationAuthKeyStore == null) return;
            if (!IsAuthorizedForUserChannel())
            {
                EarlyLog.Write(
                    "MTProto.MediaDC",
                    "prewarm DC#" + targetDcId + " skipped (not authorized yet)");
                return;
            }

            try
            {
                var sw = Stopwatch.StartNew();
                MediaDcSession session = await GetOrOpenMediaDcSessionAsync(targetDcId, ct).ConfigureAwait(false);
                sw.Stop();
                if (session == null || session.Channel == null)
                {
                    EarlyLog.Write(
                        "MTProto.MediaDC",
                        "prewarm DC#" + targetDcId + " open=null elapsed=" +
                        sw.ElapsedMilliseconds + "ms");
                    return;
                }

                EarlyLog.Write(
                    "MTProto.MediaDC",
                    "prewarm DC#" + targetDcId + " session ready elapsed=" +
                    sw.ElapsedMilliseconds + "ms");

                // Drive the auth.importAuthorization step now (cache
                // fast-path if available). EnsureMediaAuthorizationImportedAsync
                // is idempotent — it short-circuits when the session is
                // already marked imported. Without this the first real
                // media RPC against the DC still pays the import cost.
                // Best-effort: a failure here drops back to the demand
                // path on first call.
                try
                {
                    var importSw = Stopwatch.StartNew();
                    CallOutcome outcome = await EnsureMediaAuthorizationImportedAsync(session, targetDcId, ct)
                        .ConfigureAwait(false);
                    importSw.Stop();
                    if (outcome.Ok)
                    {
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "prewarm DC#" + targetDcId +
                            " import OK elapsed=" + importSw.ElapsedMilliseconds + "ms");
                    }
                    else
                    {
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "prewarm DC#" + targetDcId +
                            " import failed elapsed=" + importSw.ElapsedMilliseconds + "ms " +
                            "msg=\"" + (outcome.Message ?? string.Empty) + "\"");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception importEx)
                {
                    EarlyLog.Write(
                        "MTProto.MediaDC",
                        "prewarm DC#" + targetDcId +
                        " import threw: " + importEx.GetType().Name + ": " + importEx.Message);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "MTProto.MediaDC",
                    "prewarm DC#" + targetDcId +
                    " threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private async Task<MediaDcSession> OpenAndCacheMediaDcSessionAsync(
            int targetDcId,
            CancellationToken ct)
        {
            try
            {
                MediaDcSession session = await OpenMediaDcSessionAsync(targetDcId, ct).ConfigureAwait(false);
                if (session != null && session.Channel != null)
                {
                    lock (_mediaDcGate)
                    {
                        _mediaDcSessions[targetDcId] = session;
                    }
                }
                return session;
            }
            finally
            {
                lock (_mediaDcGate)
                {
                    _mediaDcOpenTasks.Remove(targetDcId);
                }
            }
        }

        private async Task<MediaDcSession> OpenMediaDcSessionAsync(
            int targetDcId,
            CancellationToken ct)
        {
            AccountAuthKeyRecord key = null;
            try
            {
                TelegramDcEndpoint[] endpoints = TelegramDcOptions.GetConnectionPlan(
                    targetDcId,
                    TelegramAppConfig.UseTestEnvironment,
                    null,
                    0);
                if (endpoints == null || endpoints.Length == 0)
                {
                    return null;
                }

                EarlyLog.Write(
                    "MTProto.MediaDC",
                    "opening media DC#" + targetDcId +
                    " plan=" + TelegramDcOptions.DescribePlan(endpoints));

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

                for (int i = 0; i < endpoints.Length; i++)
                {
                    TelegramDcEndpoint endpoint = endpoints[i];
                    try
                    {
                        if (key == null)
                        {
                            Result<AccountAuthKeyRecord, AccountError> keyResult =
                                await GenerateMediaAuthKeyWithDeadlineAsync(endpoint, targetDcId, ct)
                                    .ConfigureAwait(false);
                            if (keyResult.IsFail || keyResult.Value == null ||
                                !IsUsableAuthKeyRecord(keyResult.Value))
                            {
                                string detail = keyResult.IsFail && keyResult.Error != null
                                    ? keyResult.Error.ToString()
                                    : "no key returned";
                                EarlyLog.Write(
                                    "MTProto.MediaDC",
                                    "auth_key generation failed for DC#" + targetDcId +
                                    " endpoint=" + endpoint.ToString() + ": " + detail);
                                TelegramDcOptions.ReportEndpointFailure(endpoint);
                                continue;
                            }

                            key = keyResult.Value;
                            try
                            {
                                await _migrationAuthKeyStore.SaveAsync(targetDcId, key, ct)
                                    .ConfigureAwait(false);
                            }
                            catch (Exception)
                            {
                            }
                        }

                        Vianigram.MTProto.MtProtoChannel opened =
                            await OpenMediaChannelWithDeadlineAsync(endpoint, targetDcId, key, ct)
                                .ConfigureAwait(false);
                        if (opened == null)
                        {
                            TelegramDcOptions.ReportEndpointFailure(endpoint);
                            continue;
                        }

                        TelegramDcOptions.ReportEndpointSuccess(endpoint);
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "opened media DC#" + targetDcId +
                            " endpoint=" + endpoint.ToString() +
                            " auth_key_id=0x" + key.AuthKeyId.ToString("x16"));

                        return new MediaDcSession
                        {
                            DcId = targetDcId,
                            Channel = opened,
                            Endpoint = endpoint.ToString()
                        };
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        TelegramDcOptions.ReportEndpointFailure(endpoint);
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "open media DC#" + targetDcId +
                            " endpoint=" + endpoint.ToString() +
                            " failed: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "MTProto.MediaDC",
                    "open media DC#" + targetDcId + " failed: " +
                    ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            finally
            {
                ClearAuthKeyRecord(key);
            }
        }

        private async Task<Result<AccountAuthKeyRecord, AccountError>> GenerateMediaAuthKeyWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            int targetDcId,
            CancellationToken ct)
        {
            Task<Result<AccountAuthKeyRecord, AccountError>> keyTask =
                _migrationKeyGen.GenerateForDcAsync(endpoint.Host, endpoint.Port, ToMediaProxyDcId(targetDcId), ct);
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

        private static async Task<Vianigram.MTProto.MtProtoChannel> OpenMediaChannelWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            int targetDcId,
            AccountAuthKeyRecord key,
            CancellationToken ct)
        {
            Task<Vianigram.MTProto.MtProtoChannel> openTask = Vianigram.MTProto.MtProtoChannel
                .OpenWithDcAsync(
                    endpoint.Host,
                    endpoint.Port,
                    ToMediaProxyDcId(targetDcId),
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

        private Task<CallOutcome> EnsureMediaAuthorizationImportedAsync(
            MediaDcSession session,
            int targetDcId,
            CancellationToken ct)
        {
            if (session == null)
            {
                return TaskFromCallOutcome(CallOutcome.Fail("Network", -1, "media session null", 0));
            }

            if (targetDcId == CurrentDcId)
            {
                MarkMediaAuthorizationImported(session);
                return TaskFromCallOutcome(CallOutcome.Success(new byte[0]));
            }

            lock (session.Gate)
            {
                if (session.AuthorizationImported)
                {
                    return TaskFromCallOutcome(CallOutcome.Success(new byte[0]));
                }

                if (session.AuthorizationImportTask == null)
                {
                    session.AuthorizationImportTask = ImportMediaAuthorizationCoreAsync(session, targetDcId, ct);
                }

                return CompleteMediaAuthorizationImportAsync(session, session.AuthorizationImportTask);
            }
        }

        private async Task<CallOutcome> CompleteMediaAuthorizationImportAsync(
            MediaDcSession session,
            Task<CallOutcome> importTask)
        {
            try
            {
                CallOutcome outcome = await importTask.ConfigureAwait(false);
                if (outcome.Ok)
                {
                    MarkMediaAuthorizationImported(session);
                }
                return outcome;
            }
            finally
            {
                lock (session.Gate)
                {
                    if (object.ReferenceEquals(session.AuthorizationImportTask, importTask))
                    {
                        session.AuthorizationImportTask = null;
                    }
                }
            }
        }

        private async Task<CallOutcome> ImportMediaAuthorizationCoreAsync(
            MediaDcSession session,
            int targetDcId,
            CancellationToken ct)
        {
            // Cache hit fast-path. The blob handed back by
            // auth.exportAuthorization stays valid until the user revokes
            // the session server-side, so a persisted copy lets us skip
            // the home-DC export round-trip (~200-300 ms) and feed the
            // saved blob straight into auth.importAuthorization on the
            // target media DC (~150-200 ms). When two media DCs come up
            // at first paint this turns ~1.0 s of cross-DC chatter into
            // ~400 ms of pure import RPCs.
            long userId = 0L;
            int homeDcId = CurrentDcId;
            Vianigram.Storage.Ports.Stubs.IImportedAuthorizationCacheStore cache = _importedAuthCache;
            Func<long> userIdProvider = _importedAuthUserIdProvider;
            if (cache != null && userIdProvider != null)
            {
                try { userId = userIdProvider(); }
                catch { userId = 0L; }
            }

            bool cacheEligible = cache != null && userId != 0L && targetDcId > 0 && homeDcId > 0;
            if (cacheEligible)
            {
                try
                {
                    var record = await cache.TryLoadAsync(userId, targetDcId, ct).ConfigureAwait(false);
                    if (record != null
                        && record.AuthBlob != null
                        && record.AuthBlob.Length > 0
                        && record.HomeDcId == homeDcId)
                    {
                        byte[] cachedImportReq = new TlByteBuilder()
                            .WriteUInt32(CtorAuthImportAuthorization)
                            .WriteInt64(userId)
                            .WriteBytes(record.AuthBlob)
                            .ToArray();

                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "auth.importAuthorization (cached) begin targetDc=" + targetDcId +
                            " id=" + userId +
                            " blobLen=" + record.AuthBlob.Length);
                        CallOutcome cachedImport = await CallOnMediaSessionAsync(session, cachedImportReq, ct).ConfigureAwait(false);
                        if (cachedImport.Ok)
                        {
                            EarlyLog.Write(
                                "MTProto.MediaDC",
                                "auth.importAuthorization OK (cached) targetDc=" + targetDcId +
                                " responseCtor=0x" + PeekCtor(cachedImport.Bytes).ToString("x8"));
                            return CallOutcome.Success(new byte[0]);
                        }

                        // Cached blob no longer works (server-side revoke
                        // or home-DC migration we missed). Evict the row
                        // and fall through to the live export+import path
                        // so this login still succeeds.
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "auth.importAuthorization (cached) rejected targetDc=" + targetDcId +
                            " msg=\"" + (cachedImport.Message ?? string.Empty) + "\" — evicting and re-minting");
                        try
                        {
                            await cache.EvictForTargetAsync(userId, targetDcId, ct).ConfigureAwait(false);
                        }
                        catch (Exception evictEx)
                        {
                            EarlyLog.Write(
                                "MTProto.MediaDC",
                                "imported-auth cache evict failed targetDc=" + targetDcId +
                                " " + evictEx.GetType().Name + ": " + evictEx.Message);
                        }
                    }
                    else if (record != null && record.HomeDcId != homeDcId)
                    {
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "imported-auth cache home-DC mismatch targetDc=" + targetDcId +
                            " cachedHome=" + record.HomeDcId + " currentHome=" + homeDcId +
                            " — evicting and re-minting");
                        try
                        {
                            await cache.EvictForTargetAsync(userId, targetDcId, ct).ConfigureAwait(false);
                        }
                        catch { /* best-effort */ }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Cache layer is best-effort; on any failure fall
                    // through to the live export+import path.
                    EarlyLog.Write(
                        "MTProto.MediaDC",
                        "imported-auth cache lookup failed targetDc=" + targetDcId +
                        " " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            byte[] exportReq = new TlByteBuilder()
                .WriteUInt32(CtorAuthExportAuthorization)
                .WriteInt32(targetDcId)
                .ToArray();

            EarlyLog.Write("MTProto.MediaDC", "auth.exportAuthorization begin targetDc=" + targetDcId);
            CallOutcome export = await CallInternalAsync(exportReq, ct).ConfigureAwait(false);
            if (!export.Ok)
            {
                EarlyLog.Write(
                    "MTProto.MediaDC",
                    "auth.exportAuthorization failed targetDc=" + targetDcId +
                    " msg=\"" + (export.Message ?? string.Empty) + "\"");
                return export;
            }

            long id;
            byte[] bytes;
            try
            {
                var reader = new TlByteReader(export.Bytes);
                uint ctor = reader.ReadUInt32();
                if (ctor != CtorAuthExportedAuthorization)
                {
                    return CallOutcome.Fail(
                        "Unknown",
                        -1,
                        "auth.exportAuthorization unexpected ctor 0x" + ctor.ToString("x8"),
                        0);
                }

                id = reader.ReadInt64();
                bytes = reader.ReadBytes();
                if (bytes == null) bytes = new byte[0];
            }
            catch (Exception ex)
            {
                return CallOutcome.Fail(
                    "Unknown",
                    -1,
                    "auth.exportAuthorization decode failed: " + ex.Message,
                    0);
            }

            byte[] importReq = new TlByteBuilder()
                .WriteUInt32(CtorAuthImportAuthorization)
                .WriteInt64(id)
                .WriteBytes(bytes)
                .ToArray();

            EarlyLog.Write(
                "MTProto.MediaDC",
                "auth.importAuthorization begin targetDc=" + targetDcId +
                " id=" + id +
                " blobLen=" + bytes.Length);
            CallOutcome import = await CallOnMediaSessionAsync(session, importReq, ct).ConfigureAwait(false);
            if (!import.Ok)
            {
                EarlyLog.Write(
                    "MTProto.MediaDC",
                    "auth.importAuthorization failed targetDc=" + targetDcId +
                    " msg=\"" + (import.Message ?? string.Empty) + "\"");
                return import;
            }

            EarlyLog.Write(
                "MTProto.MediaDC",
                "auth.importAuthorization OK targetDc=" + targetDcId +
                " responseCtor=0x" + PeekCtor(import.Bytes).ToString("x8"));

            // Save on success. Use the freshly captured snapshot of
            // (userId, homeDcId) — both must be unchanged from cache-load
            // time to keep the (user_id, target_dc_id, home_dc_id) tuple
            // consistent. This is fire-and-forget on the cache write: the
            // import already succeeded so a save failure only costs us
            // the same export+import on next login.
            if (cacheEligible && bytes.Length > 0)
            {
                try
                {
                    await cache.SaveAsync(userId, targetDcId, homeDcId, bytes, ct).ConfigureAwait(false);
                    EarlyLog.Write(
                        "MTProto.MediaDC",
                        "imported-auth cache saved targetDc=" + targetDcId +
                        " homeDc=" + homeDcId +
                        " blobLen=" + bytes.Length);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    EarlyLog.Write(
                        "MTProto.MediaDC",
                        "imported-auth cache save failed targetDc=" + targetDcId +
                        " " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return CallOutcome.Success(new byte[0]);
        }

        private async Task<CallOutcome> CallOnMediaSessionAsync(
            MediaDcSession session,
            byte[] requestBytes,
            CancellationToken ct)
        {
            try
            {
                uint topCtor = PeekCtor(requestBytes);
                bool wrappedForInit = !IsMediaConnectionInitialized(session);
                bool retriedInit = false;
                byte[] requestToSend = wrappedForInit
                    ? WrapConnectionInit(requestBytes)
                    : requestBytes;

                if (wrappedForInit)
                {
                    EarlyLog.Write(
                        "MTProto.MediaDC",
                        "wrapping first media RPC dc=" + session.DcId +
                        " ctor=0x" + topCtor.ToString("x8") +
                        " innerSize=" + requestBytes.Length +
                        " wrappedSize=" + requestToSend.Length);
                }

                for (int attempt = 0; ; attempt++)
                {
                    Vianigram.MTProto.RpcResult result = await session.Channel
                        .CallAsync(requestToSend)
                        .AsTask(ct)
                        .ConfigureAwait(false);

                    if (result == null)
                    {
                        return CallOutcome.Fail("Unknown", -1, "Native media CallAsync returned null.", 0);
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

                        if (!wrappedForInit && !retriedInit && IsConnectionNotInited(failure))
                        {
                            ResetMediaConnectionInitialized(session);
                            wrappedForInit = true;
                            retriedInit = true;
                            requestToSend = WrapConnectionInit(requestBytes);
                            continue;
                        }

                        if (wrappedForInit && !IsConnectionNotInited(failure))
                        {
                            MarkMediaConnectionInitialized(session);
                        }

                        return failure;
                    }

                    byte[] body = result.ResultBytes;
                    if (body == null) body = new byte[0];
                    int beforeLen = body.Length;
                    body = GzipResponseDecoder.MaybeInflate(body);
                    if (body.Length != beforeLen)
                    {
                        EarlyLog.Write(
                            "MTProto.MediaDC",
                            "gzip_packed inflated " + beforeLen + " -> " + body.Length + " bytes");
                    }

                    if (wrappedForInit)
                    {
                        MarkMediaConnectionInitialized(session);
                    }

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

        private static bool TryGetAnyMigrationDcId(CallOutcome outcome, out int dcId)
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

        private static bool IsMediaConnectionInitialized(MediaDcSession session)
        {
            lock (session.Gate)
            {
                return session.ConnectionInitialized;
            }
        }

        private static void MarkMediaConnectionInitialized(MediaDcSession session)
        {
            lock (session.Gate)
            {
                session.ConnectionInitialized = true;
            }
        }

        private static void ResetMediaConnectionInitialized(MediaDcSession session)
        {
            lock (session.Gate)
            {
                session.ConnectionInitialized = false;
            }
        }

        private static void MarkMediaAuthorizationImported(MediaDcSession session)
        {
            lock (session.Gate)
            {
                session.AuthorizationImported = true;
            }
        }

        private void CloseMediaDcSessions()
        {
            List<MediaDcSession> sessions = new List<MediaDcSession>();
            lock (_mediaDcGate)
            {
                foreach (KeyValuePair<int, MediaDcSession> kv in _mediaDcSessions)
                {
                    if (kv.Value != null)
                    {
                        sessions.Add(kv.Value);
                    }
                }
                _mediaDcSessions.Clear();
                _mediaDcOpenTasks.Clear();
            }

            for (int i = 0; i < sessions.Count; i++)
            {
                try
                {
                    if (sessions[i].Channel != null)
                    {
                        sessions[i].Channel.Close();
                    }
                }
                catch (Exception)
                {
                }
            }
        }

        private static Task<MediaDcSession> TaskFromMediaDcSession(MediaDcSession session)
        {
            var tcs = new TaskCompletionSource<MediaDcSession>();
            tcs.SetResult(session);
            return tcs.Task;
        }

        private static Task<CallOutcome> TaskFromCallOutcome(CallOutcome outcome)
        {
            var tcs = new TaskCompletionSource<CallOutcome>();
            tcs.SetResult(outcome);
            return tcs.Task;
        }

        private sealed class MediaDcSession
        {
            public readonly object Gate = new object();
            public int DcId;
            public Vianigram.MTProto.MtProtoChannel Channel;
            public string Endpoint;
            public bool ConnectionInitialized;
            public bool AuthorizationImported;
            public Task<CallOutcome> AuthorizationImportTask;
        }
    }
}
