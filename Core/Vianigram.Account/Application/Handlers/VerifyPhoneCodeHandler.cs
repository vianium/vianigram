// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Application.Commands;
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
    /// <summary>
    /// Issues auth.signIn. Three outcomes:
    ///   1) auth.authorization → store auth_key + transition to Authorized.
    ///   2) SESSION_PASSWORD_NEEDED → fetch account.password challenge,
    ///      transition to WaitingForPassword, return AuthOutcome with TwoFaRequired=true.
    ///   3) Any other rpc error → ApplyAuthFailure + return mapped error.
    /// </summary>
    internal sealed class VerifyPhoneCodeHandler
    {
        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IAuthKeyStore _authKeyStore;
        private readonly IPreferredDcStore _preferredDcStore;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;
        private readonly int _activeDcId;

        public VerifyPhoneCodeHandler(
            AccountIdentity aggregate,
            IMtProtoRpcPort rpc,
            IAuthKeyStore authKeyStore,
            ILogger log,
            ITelemetry telemetry,
            int activeDcId,
            IPreferredDcStore preferredDcStore = null)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (authKeyStore == null) throw new ArgumentNullException("authKeyStore");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _aggregate = aggregate;
            _rpc = rpc;
            _authKeyStore = authKeyStore;
            _preferredDcStore = preferredDcStore; // optional; missing in legacy compositions.
            _log = new TimestampedLogger(log, "Account.VerifyPhoneCode");
            _telemetry = telemetry;
            _activeDcId = activeDcId;
        }

        public async Task<Result<AuthOutcome, AccountError>> HandleAsync(
            VerifyPhoneCodeCommand command,
            CancellationToken ct)
        {
            if (command == null || command.Phone == null || string.IsNullOrEmpty(command.Code))
            {
                return Result<AuthOutcome, AccountError>.Fail(
                    AccountError.PhoneCodeInvalid("code is empty"));
            }

            if (_aggregate.State.Kind != AuthState.AuthStateKind.WaitingForCode)
            {
                return Result<AuthOutcome, AccountError>.Fail(
                    AccountError.NotInExpectedState("VerifyCode requires WaitingForCode"));
            }

            string codeHash = _aggregate.State.WaitingHash == null ? null : _aggregate.State.WaitingHash.Value;
            byte[] request = TlEncoder.EncodeAuthSignIn(command.Phone.E164, codeHash, command.Code);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);

            if (rpcResult.IsFail)
            {
                if (AuthErrorMapper.Is2faRequired(rpcResult.Error))
                {
                    var srp = await FetchSrpChallengeAsync(ct).ConfigureAwait(false);
                    if (srp.IsFail)
                    {
                        _aggregate.ApplyAuthFailure(srp.Error);
                        return Result<AuthOutcome, AccountError>.Fail(srp.Error);
                    }

                    var transition = _aggregate.Apply2faRequired(srp.Value);
                    if (transition.IsFail)
                    {
                        return Result<AuthOutcome, AccountError>.Fail(transition.Error);
                    }

                    _telemetry.Track("account.verify_code.two_fa_required", 1);
                    return Result<AuthOutcome, AccountError>.Ok(new AuthOutcome
                    {
                        TwoFaRequired = true,
                        PasswordHint = srp.Value.Hint
                    });
                }

                var mapped = AuthErrorMapper.Map(rpcResult.Error);
                _aggregate.ApplyAuthFailure(mapped);
                _telemetry.Track("account.verify_code.error", 1);
                return Result<AuthOutcome, AccountError>.Fail(mapped);
            }

            var decoded = TlDecoder.DecodeAuthorization(rpcResult.Value);
            if (decoded == null)
            {
                var err = AccountError.Unknown("auth.signIn response could not be decoded");
                _aggregate.ApplyAuthFailure(err);
                return Result<AuthOutcome, AccountError>.Fail(err);
            }

            if (decoded.SignupRequired)
            {
                _telemetry.Track("account.verify_code.signup_required", 1);
                return Result<AuthOutcome, AccountError>.Ok(new AuthOutcome
                {
                    SignUpRequired = true
                });
            }

            // Load auth_key for the DC we authenticated against (the one
            // currently bound to the rpc port). The store was populated by the
            // auth-key generator before the channel opened.
            int currentDcId = GetCurrentDcId();
            var authKeyRecord = await _authKeyStore.LoadAsync(currentDcId, ct).ConfigureAwait(false);
            if (authKeyRecord == null)
            {
                var err = AccountError.Unknown("auth_key for active DC " + currentDcId + " is missing");
                _aggregate.ApplyAuthFailure(err);
                return Result<AuthOutcome, AccountError>.Fail(err);
            }

            // The AuthKey value object holds the at-rest ciphertext. We do not
            // re-encrypt here because the store already persisted ciphertext;
            // we mirror the byte length so the value object's invariants hold.
            byte[] cipherShape = authKeyRecord.AuthKey == null
                ? new byte[0]
                : new byte[authKeyRecord.AuthKey.Length];
            var key = AuthKey.FromGenerated(authKeyRecord.AuthKey, cipherShape, authKeyRecord.AuthKeyId);

            var apply = _aggregate.ApplyAuthSuccess(new UserId(decoded.UserId), key, currentDcId);
            if (apply.IsFail)
            {
                return Result<AuthOutcome, AccountError>.Fail(apply.Error);
            }

            // Persist the home DC so the next launch can boot directly against
            // the user's authorised DC instead of generating a fresh auth_key
            // on the default DC#2 and then catching AUTH_KEY_UNREGISTERED on
            // the first updates.getState (Sync bootstrap).
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
                    // Persistence failure is non-fatal — the user just pays
                    // the migration cost again next launch (and re-login).
                    _log.Warn("session persistence failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            _telemetry.Track("account.verify_code.success", 1);
            return Result<AuthOutcome, AccountError>.Ok(new AuthOutcome
            {
                TwoFaRequired = false,
                UserId = decoded.UserId
            });
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
    }
}
