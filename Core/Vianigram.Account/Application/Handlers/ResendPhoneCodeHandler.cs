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
    /// Issues auth.resendCode using the current phone_code_hash and updates the
    /// pending code hash/type returned by Telegram.
    /// </summary>
    internal sealed class ResendPhoneCodeHandler
    {
        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public ResendPhoneCodeHandler(
            AccountIdentity aggregate,
            IMtProtoRpcPort rpc,
            ILogger log,
            ITelemetry telemetry)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _aggregate = aggregate;
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Account.ResendPhoneCode");
            _telemetry = telemetry;
        }

        public async Task<Result<Unit, AccountError>> HandleAsync(
            ResendPhoneCodeCommand command,
            CancellationToken ct)
        {
            if (command == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("command is null"));
            }

            if (_aggregate.Phone == null ||
                _aggregate.State == null ||
                _aggregate.State.Kind != AuthState.AuthStateKind.WaitingForCode ||
                _aggregate.State.WaitingHash == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("ResendPhoneCode requires WaitingForCode"));
            }

            byte[] request = TlEncoder.EncodeAuthResendCode(
                _aggregate.Phone.E164,
                _aggregate.State.WaitingHash.Value);

            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = AuthErrorMapper.Map(rpcResult.Error);
                _aggregate.ApplyAuthFailure(mapped);
                _telemetry.Track("account.resend_code.error", 1);
                _log.Warn("auth.resendCode failed: " + mapped);
                return Result<Unit, AccountError>.Fail(mapped);
            }

            var decoded = TlDecoder.DecodeSentCode(rpcResult.Value);
            if (decoded == null)
            {
                var err = AccountError.Unknown("auth.resendCode response could not be decoded");
                _aggregate.ApplyAuthFailure(err);
                return Result<Unit, AccountError>.Fail(err);
            }

            int timeoutSec = decoded.Timeout.HasValue ? decoded.Timeout.Value : 120;
            _log.Info(
                "auth.resendCode accepted: type=" + decoded.Type +
                ", next_type=" + FormatNullableType(decoded.NextType) +
                ", timeout=" + timeoutSec + "s" +
                ", hash=" + SafeHashPrefix(decoded.PhoneCodeHash));

            var apply = _aggregate.ApplyCodeSent(
                new PhoneCodeHash(decoded.PhoneCodeHash),
                decoded.Type,
                decoded.NextType,
                TimeSpan.FromSeconds(timeoutSec));

            _telemetry.Track("account.resend_code.count", 1);
            return apply;
        }

        private static string FormatNullableType(SentCodeType? type)
        {
            return type.HasValue ? type.Value.ToString() : "none";
        }

        private static string SafeHashPrefix(string hash)
        {
            if (string.IsNullOrEmpty(hash)) return "(empty)";
            if (hash.Length <= 8) return hash;
            return hash.Substring(0, 8) + "...";
        }
    }
}
