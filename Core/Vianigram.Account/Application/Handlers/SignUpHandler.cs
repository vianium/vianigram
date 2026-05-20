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
    internal sealed class SignUpHandler
    {
        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IAuthKeyStore _authKeyStore;
        private readonly IPreferredDcStore _preferredDcStore;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;
        private readonly int _activeDcId;

        public SignUpHandler(
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
            _preferredDcStore = preferredDcStore;
            _log = new TimestampedLogger(log, "Account.SignUp");
            _telemetry = telemetry;
            _activeDcId = activeDcId;
        }

        public async Task<Result<AuthOutcome, AccountError>> HandleAsync(
            SignUpCommand command,
            CancellationToken ct)
        {
            string first = command == null ? null : command.FirstName;
            string last = command == null ? null : command.LastName;
            first = first == null ? string.Empty : first.Trim();
            last = last == null ? string.Empty : last.Trim();

            if (string.IsNullOrWhiteSpace(first))
            {
                return Result<AuthOutcome, AccountError>.Fail(
                    AccountError.NotInExpectedState("first name is required"));
            }

            if (_aggregate.State.Kind != AuthState.AuthStateKind.WaitingForCode)
            {
                return Result<AuthOutcome, AccountError>.Fail(
                    AccountError.NotInExpectedState("SignUp requires WaitingForCode"));
            }

            if (_aggregate.Phone == null || _aggregate.State.WaitingHash == null)
            {
                return Result<AuthOutcome, AccountError>.Fail(
                    AccountError.NotInExpectedState("signup phone/code hash missing"));
            }

            byte[] request = TlEncoder.EncodeAuthSignUp(
                _aggregate.Phone.E164,
                _aggregate.State.WaitingHash.Value,
                first,
                last);

            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = AuthErrorMapper.Map(rpcResult.Error);
                _aggregate.ApplyAuthFailure(mapped);
                _telemetry.Track("account.signup.error", 1);
                return Result<AuthOutcome, AccountError>.Fail(mapped);
            }

            var decoded = TlDecoder.DecodeAuthorization(rpcResult.Value);
            if (decoded == null || decoded.SignupRequired)
            {
                var err = AccountError.Unknown("auth.signUp response could not be decoded");
                _aggregate.ApplyAuthFailure(err);
                return Result<AuthOutcome, AccountError>.Fail(err);
            }

            int currentDcId = GetCurrentDcId();
            var authKeyRecord = await _authKeyStore.LoadAsync(currentDcId, ct).ConfigureAwait(false);
            if (authKeyRecord == null)
            {
                var err = AccountError.Unknown("auth_key for active DC " + currentDcId + " is missing");
                _aggregate.ApplyAuthFailure(err);
                return Result<AuthOutcome, AccountError>.Fail(err);
            }

            byte[] cipherShape = authKeyRecord.AuthKey == null
                ? new byte[0]
                : new byte[authKeyRecord.AuthKey.Length];
            var key = AuthKey.FromGenerated(authKeyRecord.AuthKey, cipherShape, authKeyRecord.AuthKeyId);

            var apply = _aggregate.ApplyAuthSuccess(new UserId(decoded.UserId), key, currentDcId);
            if (apply.IsFail)
            {
                return Result<AuthOutcome, AccountError>.Fail(apply.Error);
            }

            PersistSessionMarker(currentDcId, decoded.UserId);
            _telemetry.Track("account.signup.success", 1);
            return Result<AuthOutcome, AccountError>.Ok(new AuthOutcome
            {
                UserId = decoded.UserId
            });
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

        private void PersistSessionMarker(int dcId, long userId)
        {
            if (_preferredDcStore == null) return;
            try
            {
                _preferredDcStore.SetHomeDcId(dcId);
                _preferredDcStore.SetUserId(userId);
                _log.Info("session persisted: dc=" + dcId + " userId=" + userId);
            }
            catch (Exception ex)
            {
                _log.Warn("session persistence failed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
