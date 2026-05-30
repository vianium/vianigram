// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PollQrLoginHandler.cs — Vianigram.Account.Application.Handlers
//
// Single entry-point handler for the QR-login state machine.
//
// Per Telegram's protocol the unauthenticated client polls the login
// status by RE-ISSUING auth.exportLoginToken (NOT auth.importLoginToken —
// that's for the already-authorized device, or for the unauthenticated
// client to switch DCs after MigrateTo). Each re-issue replaces any prior
// pending token, and once the other device approves, the wire response
// flips from auth.loginToken to auth.loginTokenSuccess (with the embedded
// auth.authorization). Until the user scans, every call returns a fresh
// auth.loginToken with a new 30s expiry.
//
// This handler unifies what used to be two methods (RequestQrToken +
// PollQrLogin) onto a single wire path:
//
//   - RequestQrTokenAsync (initial fetch) → calls HandleAsync, returns
//     QrLoginPoll. On Pending the caller renders Token; on any of the
//     terminal states the caller pivots immediately.
//
//   - PollQrLoginAsync (periodic poll) → also calls HandleAsync. If a
//     fresh server-side token is issued (Pending), the caller decides
//     whether to swap the visible QR or keep the already-rendered token
//     until its own expiry.
//
// On Accepted: decodes the embedded auth.authorization, loads the
// auth_key for the current DC, lifts the aggregate to Authorized, and
// persists (homeDcId, userId) via IPreferredDcStore.
//
// On TwoFaRequired (server returned SESSION_PASSWORD_NEEDED on the wire):
// fetches the SRP challenge via account.getPassword and transitions the
// aggregate to WaitingForPassword. The subsequent
// SubmitTwoFaPasswordAsync call has a primed challenge.
//
// On MigrateTo: surfaces as Pending so the caller retries on the next
// tick. The transport layer is responsible for following the migration
// before returning. (For the dedicated AccountLoginMtProtoRpcPort that
// already happens transparently for rpc-error MIGRATE_X; the TL-level
// auth.loginTokenMigrateTo is a separate signal the transport doesn't
// follow yet — the caller falls back to a fresh exportLoginToken on the
// same DC, which is acceptable.)

using System;
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Entities;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Infrastructure;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;

namespace Vianigram.Account.Application.Handlers
{
    internal sealed class PollQrLoginHandler
    {
        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IAuthKeyStore _authKeyStore;
        private readonly IPreferredDcStore _preferredDcStore;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;
        private readonly int _activeDcId;
        private readonly int _apiId;
        private readonly string _apiHash;
        private const long MaxReasonableTelegramUserId = 100000000000000L;

        public PollQrLoginHandler(
            AccountIdentity aggregate,
            IMtProtoRpcPort rpc,
            IAuthKeyStore authKeyStore,
            ILogger log,
            ITelemetry telemetry,
            int activeDcId,
            int apiId,
            string apiHash,
            IPreferredDcStore preferredDcStore = null)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (authKeyStore == null) throw new ArgumentNullException("authKeyStore");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            if (apiHash == null) throw new ArgumentNullException("apiHash");

            _aggregate = aggregate;
            _rpc = rpc;
            _authKeyStore = authKeyStore;
            _preferredDcStore = preferredDcStore;
            _log = new TimestampedLogger(log, "Account.QrLogin");
            _telemetry = telemetry;
            _activeDcId = activeDcId;
            _apiId = apiId;
            _apiHash = apiHash;
        }

        public async Task<Result<QrLoginPoll, AccountError>> HandleAsync(CancellationToken ct)
        {
            var rpcResult = await _rpc.AuthExportLoginTokenAsync(_apiId, _apiHash, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _telemetry.Track("account.qr_login.error", 1);
                _log.Warn("auth.exportLoginToken failed: " + rpcResult.Error);
                return Result<QrLoginPoll, AccountError>.Fail(rpcResult.Error);
            }

            var wire = rpcResult.Value;
            if (wire == null)
            {
                return Result<QrLoginPoll, AccountError>.Fail(
                    AccountError.Unknown("auth.exportLoginToken returned null wire"));
            }

            _log.Info("kind=" + wire.Kind);
            _telemetry.Track("account.qr_login.count", 1);

            // ---- Pending: build a QrLoginToken for the caller to render. ----
            if (wire.Kind == QrPollKind.Pending)
            {
                if (wire.Token == null || wire.Token.Length == 0)
                {
                    return Result<QrLoginPoll, AccountError>.Fail(
                        AccountError.Unknown("auth.exportLoginToken returned empty token"));
                }

                try
                {
                    var token = QrLoginToken.FromTelegramToken(
                        wire.Token,
                        FromUnixSeconds(wire.ExpiresUnixSeconds));
                    return Result<QrLoginPoll, AccountError>.Ok(
                        new QrLoginPoll(QrLoginStatus.Pending, null, 0L, token));
                }
                catch (Exception ex)
                {
                    return Result<QrLoginPoll, AccountError>.Fail(
                        AccountError.Unknown("could not build tg:// URI: " + ex.Message, ex));
                }
            }

            // ---- MigrateTo: switch the unauthenticated channel to the
            //      indicated DC and call auth.importLoginToken with the
            //      server-issued migrate token. The new DC's response is
            //      authoritative — typically loginTokenSuccess, sometimes
            //      another loginToken (still pending on the new DC) or
            //      SESSION_PASSWORD_NEEDED (2FA). ----
            if (wire.Kind == QrPollKind.MigrateTo)
            {
                _log.Info("loginTokenMigrateTo: dc=" + wire.MigrateDcId +
                          " token_len=" + (wire.MigrateToken == null ? 0 : wire.MigrateToken.Length));

                if (wire.MigrateDcId <= 0 || wire.MigrateToken == null || wire.MigrateToken.Length == 0)
                {
                    _log.Warn("MigrateTo missing dc/token; surfacing as Pending");
                    return Result<QrLoginPoll, AccountError>.Ok(
                        new QrLoginPoll(QrLoginStatus.Pending, null, 0L, null));
                }

                var migrationPort = _rpc as IQrLoginMigrationPort;
                if (migrationPort == null)
                {
                    _log.Warn("MigrateTo: transport doesn't implement IQrLoginMigrationPort; surfacing as Pending");
                    return Result<QrLoginPoll, AccountError>.Ok(
                        new QrLoginPoll(QrLoginStatus.Pending, null, 0L, null));
                }

                bool switched = migrationPort.SwitchDcForQrMigration(wire.MigrateDcId);
                if (!switched)
                {
                    _log.Warn("DC switch to " + wire.MigrateDcId + " failed; surfacing as Pending");
                    return Result<QrLoginPoll, AccountError>.Ok(
                        new QrLoginPoll(QrLoginStatus.Pending, null, 0L, null));
                }

                _log.Info("DC switched to " + wire.MigrateDcId + "; calling auth.importLoginToken on new DC");
                var importResult = await _rpc.AuthImportLoginTokenAsync(wire.MigrateToken, ct).ConfigureAwait(false);
                if (importResult.IsFail)
                {
                    _log.Warn("post-migrate importLoginToken failed: " + importResult.Error);
                    migrationPort.ResetQrMigrationAfterFailure();
                    return Result<QrLoginPoll, AccountError>.Fail(importResult.Error);
                }

                var imported = importResult.Value;
                if (imported == null)
                {
                    migrationPort.ResetQrMigrationAfterFailure();
                    return Result<QrLoginPoll, AccountError>.Fail(
                        AccountError.Unknown("post-migrate importLoginToken returned null wire"));
                }

                _log.Info("post-migrate import kind=" + imported.Kind);
                if (imported.Kind == QrPollKind.Accepted)
                {
                    return await ProcessAcceptedAsync(imported.AuthorizationBytes, ct).ConfigureAwait(false);
                }
                if (imported.Kind == QrPollKind.TwoFaRequired)
                {
                    return await ProcessTwoFaRequiredAsync(ct).ConfigureAwait(false);
                }
                if (imported.Kind == QrPollKind.Expired)
                {
                    migrationPort.ResetQrMigrationAfterFailure();
                    return Result<QrLoginPoll, AccountError>.Ok(
                        new QrLoginPoll(QrLoginStatus.Expired, null, 0L));
                }
                // Pending / chained MigrateTo on the new DC: keep
                // surfacing as Pending; the next periodic poll on the
                // new DC will pick it up.
                return Result<QrLoginPoll, AccountError>.Ok(
                    new QrLoginPoll(QrLoginStatus.Pending, null, 0L, null));
            }

            // ---- TwoFaRequired: prime the aggregate so the next
            //      SubmitTwoFaPasswordAsync call has its SRP challenge. ----
            if (wire.Kind == QrPollKind.TwoFaRequired)
            {
                return await ProcessTwoFaRequiredAsync(ct).ConfigureAwait(false);
            }

            // ---- Accepted: decode user_id, lift aggregate, persist. ----
            if (wire.Kind == QrPollKind.Accepted)
            {
                return await ProcessAcceptedAsync(wire.AuthorizationBytes, ct).ConfigureAwait(false);
            }

            return Result<QrLoginPoll, AccountError>.Fail(
                AccountError.Unknown("auth.exportLoginToken: unmapped wire kind " + wire.Kind));
        }

        /// <summary>
        /// Decode the embedded auth.authorization, lift the aggregate to
        /// Authorized for the current DC, and persist (homeDc, userId).
        /// Used by both the export path (when the user authorises between
        /// pre-emptive refreshes) and the import path (after following a
        /// MigrateTo).
        /// </summary>
        private async Task<Result<QrLoginPoll, AccountError>> ProcessAcceptedAsync(
            byte[] authorizationBytes,
            CancellationToken ct)
        {
            var decoded = TlDecoder.DecodeAuthorization(authorizationBytes);
            if (decoded == null)
            {
                _log.Warn(
                    "authorization decode failed; " +
                    DescribeAuthorizationBytes(authorizationBytes) +
                    "; trying users.getFullUser(inputUserSelf)");
                long resolvedUserId = await TryResolveUserIdFromAuthorizedSessionAsync(ct).ConfigureAwait(false);
                if (!IsReasonableUserId(resolvedUserId))
                {
                    var err = AccountError.Unknown(
                        "auth.loginTokenSuccess: authorization could not be decoded and self lookup failed");
                    _aggregate.ApplyAuthFailure(err);
                    return Result<QrLoginPoll, AccountError>.Fail(err);
                }

                decoded = new TlDecoder.AuthorizationDecoded
                {
                    SignupRequired = false,
                    UserId = resolvedUserId
                };
            }

            if (decoded.SignupRequired)
            {
                _telemetry.Track("account.qr_login.signup_required", 1);
                _log.Warn("auth.loginTokenSuccess returned authorizationSignUpRequired; switching caller to phone sign-up");
                return Result<QrLoginPoll, AccountError>.Ok(
                    new QrLoginPoll(QrLoginStatus.SignUpRequired, null));
            }

            if (!IsReasonableUserId(decoded.UserId))
            {
                _log.Warn(
                    "authorization decoded an invalid userId=" + decoded.UserId +
                    "; trying users.getFullUser(inputUserSelf)");
                long resolvedUserId = await TryResolveUserIdFromAuthorizedSessionAsync(ct).ConfigureAwait(false);
                if (!IsReasonableUserId(resolvedUserId))
                {
                    var err = AccountError.Unknown(
                        "auth.loginTokenSuccess: resolved userId is invalid: " + decoded.UserId);
                    _aggregate.ApplyAuthFailure(err);
                    return Result<QrLoginPoll, AccountError>.Fail(err);
                }

                decoded.UserId = resolvedUserId;
            }

            _log.Info(
                "authorization resolved: userId=" + decoded.UserId + " " +
                DescribeAuthorizationBytes(authorizationBytes));

            int currentDcId = GetCurrentDcId();
            var migrationPort = _rpc as IQrLoginMigrationPort;

            // The server authorized whichever auth_key was encrypting
            // the wire frames at the moment auth.loginTokenSuccess
            // arrived — i.e. the LOGIN CHANNEL's in-memory key. That's
            // the source of truth. The persistent store can disagree
            // for two known reasons:
            //   1) LogoutHandler.DeleteAsync wiped the slot while the
            //      channel object stayed alive with its prior key, then
            //      a fresh navigation triggered a prewarm that generated
            //      a DIFFERENT key into the empty slot.
            //   2) A concurrent prewarm finished its handshake just
            //      before the channel did and saved a different key.
            // In both cases the store now holds an UNAUTHORIZED key —
            // persisting it would land the next launch with
            // AUTH_KEY_UNREGISTERED. Always prefer the channel's key
            // when it disagrees with the store, and re-save it.
            AuthKeyRecord channelKey = migrationPort != null
                ? migrationPort.TryGetCurrentChannelAuthKey(currentDcId)
                : null;

            AuthKeyRecord authKeyRecord = await TryLoadAuthKeyAsync(currentDcId, ct).ConfigureAwait(false);

            if (channelKey != null)
            {
                if (authKeyRecord == null || authKeyRecord.AuthKeyId != channelKey.AuthKeyId)
                {
                    string storeStr = authKeyRecord == null
                        ? "(null)"
                        : "0x" + authKeyRecord.AuthKeyId.ToString("x16");
                    _log.Info(
                        "store auth_key " + storeStr +
                        " differs from channel auth_key 0x" + channelKey.AuthKeyId.ToString("x16") +
                        "; persisting the channel's authorized key");
                    try
                    {
                        await _authKeyStore.SaveAsync(currentDcId, channelKey, ct).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("authorized key persist failed: " + ex.GetType().Name + ": " + ex.Message);
                    }
                    authKeyRecord = channelKey;
                }
            }
            else if (authKeyRecord == null)
            {
                // No channel key available AND store empty. Last-resort
                // fallback: kick off / await a prewarm to populate the
                // store, then re-load. This covers the rare case where
                // the channel object has gone away between the wire
                // call returning and ProcessAccepted running.
                if (migrationPort != null)
                {
                    _log.Info(
                        "auth_key DC#" + currentDcId +
                        " missing on Accepted and no live channel key; awaiting prewarm");
                    try
                    {
                        await migrationPort.PrewarmAuthKeyAsync(currentDcId, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _log.Warn("Prewarm await threw: " + ex.GetType().Name + ": " + ex.Message);
                    }
                    authKeyRecord = await TryLoadAuthKeyAsync(currentDcId, ct).ConfigureAwait(false);
                }
            }

            if (authKeyRecord == null)
            {
                var err = AccountError.Unknown("auth_key for active DC " + currentDcId + " is missing");
                _aggregate.ApplyAuthFailure(err);
                return Result<QrLoginPoll, AccountError>.Fail(err);
            }

            byte[] cipherShape = authKeyRecord.AuthKey == null
                ? new byte[0]
                : new byte[authKeyRecord.AuthKey.Length];
            var key = AuthKey.FromGenerated(authKeyRecord.AuthKey, cipherShape, authKeyRecord.AuthKeyId);

            var apply = _aggregate.ApplyAuthSuccess(new UserId(decoded.UserId), key, currentDcId);
            if (apply.IsFail)
            {
                _log.Warn("ApplyAuthSuccess failed: " + apply.Error);
                return Result<QrLoginPoll, AccountError>.Fail(apply.Error);
            }

            if (_preferredDcStore != null)
            {
                try
                {
                    _preferredDcStore.SetHomeDcId(currentDcId);
                    _preferredDcStore.SetUserId(decoded.UserId);
                    _log.Info("session persisted: dc=" + currentDcId + " userId=" + decoded.UserId);
                }
                catch (Exception ex)
                {
                    // Non-fatal — the user just pays the migration cost
                    // again next launch.
                    _log.Warn("session persistence failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            _telemetry.Track("account.qr_login.success", 1);
            return Result<QrLoginPoll, AccountError>.Ok(
                new QrLoginPoll(QrLoginStatus.Accepted, null, decoded.UserId));
        }

        private async Task<long> TryResolveUserIdFromAuthorizedSessionAsync(CancellationToken ct)
        {
            try
            {
                var self = await _rpc.UsersGetFullUserAsync(InputUserSelf.Instance, ct).ConfigureAwait(false);
                if (self.IsFail)
                {
                    _log.Warn("users.getFullUser fallback failed: " + self.Error);
                    return 0L;
                }

                UserFullResponse wire = self.Value;
                if (wire == null)
                {
                    _log.Warn("users.getFullUser fallback returned null wire");
                    return 0L;
                }

                if (!IsReasonableUserId(wire.UserId))
                {
                    _log.Warn("users.getFullUser fallback returned invalid userId=" + wire.UserId);
                    return 0L;
                }

                _log.Info("users.getFullUser fallback resolved userId=" + wire.UserId);
                return wire.UserId;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.Warn("users.getFullUser fallback threw: " + ex.GetType().Name + ": " + ex.Message);
                return 0L;
            }
        }

        /// <summary>
        /// Fetch the SRP challenge via account.getPassword and transition
        /// the aggregate to WaitingForPassword. Used by both the export
        /// and the import (post-migrate) paths.
        /// </summary>
        private async Task<Result<QrLoginPoll, AccountError>> ProcessTwoFaRequiredAsync(CancellationToken ct)
        {
            var srpResult = await FetchSrpChallengeAsync(ct).ConfigureAwait(false);
            if (srpResult.IsFail)
            {
                _log.Warn("2FA required but SRP fetch failed: " + srpResult.Error);
                return Result<QrLoginPoll, AccountError>.Fail(srpResult.Error);
            }

            var transition = _aggregate.Apply2faRequired(srpResult.Value);
            if (transition.IsFail)
            {
                _log.Warn("Apply2faRequired failed: " + transition.Error);
                return Result<QrLoginPoll, AccountError>.Fail(transition.Error);
            }

            _telemetry.Track("account.qr_login.two_fa_required", 1);
            return Result<QrLoginPoll, AccountError>.Ok(
                new QrLoginPoll(QrLoginStatus.TwoFaRequired, srpResult.Value.Hint));
        }

        /// <summary>
        /// Best-effort auth_key load. Logs and swallows storage faults so
        /// the caller can decide whether to fall back to a prewarm-await
        /// or fail. Returns null when the store is empty or the cached
        /// record is unusable (zero-length key, missing keyId).
        /// </summary>
        private async Task<AuthKeyRecord> TryLoadAuthKeyAsync(int dcId, CancellationToken ct)
        {
            AuthKeyRecord record;
            try
            {
                record = await _authKeyStore.LoadAsync(dcId, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.Warn(
                    "auth_key load DC#" + dcId + " threw: " +
                    ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            if (record == null) return null;
            if (record.AuthKey == null || record.AuthKey.Length == 0 || record.AuthKeyId == 0)
            {
                _log.Warn(
                    "auth_key DC#" + dcId + " loaded but rejected as unusable" +
                    " (keyLen=" + (record.AuthKey == null ? -1 : record.AuthKey.Length) +
                    " keyId=" + record.AuthKeyId + ")");
                return null;
            }
            return record;
        }

        private async Task<Result<SrpChallenge, AccountError>> FetchSrpChallengeAsync(CancellationToken ct)
        {
            byte[] request = TlEncoder.EncodeAccountGetPassword();
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                return Result<SrpChallenge, AccountError>.Fail(AuthErrorMapper.Map(rpcResult.Error));
            }

            var decoded = TlDecoder.DecodeAccountPassword(rpcResult.Value);
            if (decoded == null || !decoded.HasPassword)
            {
                return Result<SrpChallenge, AccountError>.Fail(
                    AccountError.Unknown("account.getPassword returned no password challenge"));
            }

            var challenge = new SrpChallenge(
                decoded.SrpId,
                decoded.CurrentAlgoBlob,
                decoded.Salt1,
                decoded.Salt2,
                decoded.G,
                decoded.P,
                decoded.SrpB,
                decoded.Hint);

            return Result<SrpChallenge, AccountError>.Ok(challenge);
        }

        private int GetCurrentDcId()
        {
            var dcProvider = _rpc as IMtProtoDcProvider;
            if (dcProvider != null && dcProvider.CurrentDcId > 0)
            {
                return dcProvider.CurrentDcId;
            }
            return _activeDcId;
        }

        private static bool IsReasonableUserId(long userId)
        {
            return userId > 0L && userId <= MaxReasonableTelegramUserId;
        }

        private static string DescribeAuthorizationBytes(byte[] bytes)
        {
            int length = bytes == null ? 0 : bytes.Length;
            return "auth_len=" + length +
                   " auth_ctor=" + TryReadCtorHex(bytes) +
                   " auth_hex_prefix=" + HexPrefix(bytes, 48);
        }

        private static string TryReadCtorHex(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return "(none)";
            uint ctor = (uint)(bytes[0]
                | (bytes[1] << 8)
                | (bytes[2] << 16)
                | (bytes[3] << 24));
            return "0x" + ctor.ToString("x8", CultureInfo.InvariantCulture);
        }

        private static string HexPrefix(byte[] bytes, int maxBytes)
        {
            if (bytes == null || bytes.Length == 0) return "(empty)";
            int count = bytes.Length < maxBytes ? bytes.Length : maxBytes;
            var sb = new StringBuilder(count * 2);
            for (int i = 0; i < count; i++)
            {
                sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
            }
            if (bytes.Length > count) sb.Append("...");
            return sb.ToString();
        }

        private static DateTimeOffset FromUnixSeconds(int unixSeconds)
        {
            var epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
            return epoch.AddSeconds(unixSeconds);
        }
    }
}
