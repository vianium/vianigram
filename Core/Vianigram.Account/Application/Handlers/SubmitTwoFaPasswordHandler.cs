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
    /// Computes SRP-2048 M1 from the cached <see cref="SrpChallenge"/> + user
    /// password and submits auth.checkPassword.
    /// </summary>
    internal sealed class SubmitTwoFaPasswordHandler
    {
        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISrpClientPort _srp;
        private readonly IAuthKeyStore _authKeyStore;
        private readonly IPreferredDcStore _preferredDcStore;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;
        private readonly int _activeDcId;

        public SubmitTwoFaPasswordHandler(
            AccountIdentity aggregate,
            IMtProtoRpcPort rpc,
            ISrpClientPort srp,
            IAuthKeyStore authKeyStore,
            ILogger log,
            ITelemetry telemetry,
            int activeDcId,
            IPreferredDcStore preferredDcStore = null)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (srp == null) throw new ArgumentNullException("srp");
            if (authKeyStore == null) throw new ArgumentNullException("authKeyStore");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _aggregate = aggregate;
            _rpc = rpc;
            _srp = srp;
            _authKeyStore = authKeyStore;
            _preferredDcStore = preferredDcStore; // optional; legacy compositions may omit it.
            _log = new TimestampedLogger(log, "Account.SubmitTwoFaPassword");
            _telemetry = telemetry;
            _activeDcId = activeDcId;
        }

        public async Task<Result<Unit, AccountError>> HandleAsync(
            SubmitTwoFaPasswordCommand command,
            CancellationToken ct)
        {
            if (command == null || string.IsNullOrEmpty(command.Password))
            {
                return Result<Unit, AccountError>.Fail(AccountError.SrpPasswordInvalid("password is empty"));
            }

            if (_aggregate.State.Kind != AuthState.AuthStateKind.WaitingForPassword)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("SubmitPassword requires WaitingForPassword"));
            }

            var challenge = _aggregate.State.SrpChallenge;
            if (challenge == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("SRP challenge missing"));
            }

            var proofResult = await _srp.ComputeProofAsync(command.Password, challenge, ct).ConfigureAwait(false);
            if (proofResult.IsFail)
            {
                _aggregate.ApplyAuthFailure(proofResult.Error);
                return Result<Unit, AccountError>.Fail(proofResult.Error);
            }

            SrpProof proof = proofResult.Value;
            byte[] request = TlEncoder.EncodeAuthCheckPassword(challenge.SrpId, proof.A, proof.M1);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = AuthErrorMapper.Map(rpcResult.Error);
                _aggregate.ApplyAuthFailure(mapped);
                _telemetry.Track("account.two_fa.error", 1);
                return Result<Unit, AccountError>.Fail(mapped);
            }

            var decoded = TlDecoder.DecodeAuthorization(rpcResult.Value);
            if (decoded == null || decoded.SignupRequired)
            {
                var err = AccountError.Unknown("auth.checkPassword response unexpected");
                _aggregate.ApplyAuthFailure(err);
                return Result<Unit, AccountError>.Fail(err);
            }

            int currentDcId = GetCurrentDcId();
            var authKeyRecord = await _authKeyStore.LoadAsync(currentDcId, ct).ConfigureAwait(false);
            if (authKeyRecord == null)
            {
                var err = AccountError.Unknown("auth_key for active DC " + currentDcId + " is missing");
                _aggregate.ApplyAuthFailure(err);
                return Result<Unit, AccountError>.Fail(err);
            }

            byte[] cipherShape = authKeyRecord.AuthKey == null
                ? new byte[0]
                : new byte[authKeyRecord.AuthKey.Length];
            var key = AuthKey.FromGenerated(authKeyRecord.AuthKey, cipherShape, authKeyRecord.AuthKeyId);

            var apply = _aggregate.ApplyAuthSuccess(new UserId(decoded.UserId), key, currentDcId);
            if (apply.IsFail)
            {
                return apply;
            }

            // Persist the home DC after a successful 2FA login so the next
            // launch boots straight against the user's authorised DC.
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
                    _log.Warn("session persistence failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            _telemetry.Track("account.two_fa.success", 1);
            return Result<Unit, AccountError>.Ok(Unit.Value);
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
