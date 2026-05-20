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
    /// Issues auth.sendCode, decodes auth.sentCode, and transitions the
    /// aggregate from Anonymous → WaitingForCode.
    /// </summary>
    internal sealed class SendPhoneCodeHandler
    {
        private readonly AccountIdentity _aggregate;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;
        private readonly int _apiId;
        private readonly string _apiHash;

        public SendPhoneCodeHandler(
            AccountIdentity aggregate,
            IMtProtoRpcPort rpc,
            ILogger log,
            ITelemetry telemetry,
            int apiId,
            string apiHash)
        {
            if (aggregate == null) throw new ArgumentNullException("aggregate");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");
            if (apiHash == null) throw new ArgumentNullException("apiHash");

            _aggregate = aggregate;
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Account.SendPhoneCode");
            _telemetry = telemetry;
            _apiId = apiId;
            _apiHash = apiHash;
        }

        public async Task<Result<Unit, AccountError>> HandleAsync(
            SendPhoneCodeCommand command,
            CancellationToken ct)
        {
            if (command == null || command.Phone == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.InvalidPhoneNumber("phone is null"));
            }

            var begin = _aggregate.BeginPhoneAuth(command.Phone);
            if (begin.IsFail)
            {
                _telemetry.Track("account.send_code.error", 1);
                return begin;
            }

            var dcPreference = _rpc as IPhoneLoginDcPreferencePort;
            if (dcPreference != null)
            {
                int preferredDc = dcPreference.PreferDcForPhone(command.Phone.E164);
                if (preferredDc > 0)
                {
                    _log.Info("auth.sendCode phone DC preference applied: dc=" + preferredDc);
                }
            }

            byte[] request = TlEncoder.EncodeAuthSendCode(command.Phone.E164, _apiId, _apiHash);
            _log.Info("auth.sendCode dispatch: phone=" + command.Phone.E164 + " bytes=" + request.Length);
            var rpcSw = System.Diagnostics.Stopwatch.StartNew();
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            rpcSw.Stop();
            _log.Info("auth.sendCode rpc returned elapsed=" + rpcSw.ElapsedMilliseconds + "ms ok=" + rpcResult.IsOk);

            if (rpcResult.IsFail)
            {
                var mapped = AuthErrorMapper.Map(rpcResult.Error);
                _aggregate.ApplyAuthFailure(mapped);
                _telemetry.Track("account.send_code.error", 1);
                _log.Warn("auth.sendCode failed: " + mapped);
                return Result<Unit, AccountError>.Fail(mapped);
            }

            var decoded = TlDecoder.DecodeSentCode(rpcResult.Value);
            if (decoded == null)
            {
                var err = AccountError.Unknown("auth.sendCode response could not be decoded");
                _aggregate.ApplyAuthFailure(err);
                return Result<Unit, AccountError>.Fail(err);
            }

            int timeoutSec = decoded.Timeout.HasValue ? decoded.Timeout.Value : 120;
            _log.Info(
                "auth.sendCode accepted: type=" + decoded.Type +
                ", next_type=" + FormatNullableType(decoded.NextType) +
                ", timeout=" + timeoutSec + "s" +
                ", hash=" + SafeHashPrefix(decoded.PhoneCodeHash));
            if (decoded.Type == SentCodeType.App && !decoded.NextType.HasValue)
            {
                _log.Warn("Telegram selected in-app code without next_type; resend fallback will try SMS after timeout.");
            }

            var apply = _aggregate.ApplyCodeSent(
                new PhoneCodeHash(decoded.PhoneCodeHash),
                decoded.Type,
                decoded.NextType,
                TimeSpan.FromSeconds(timeoutSec));

            _telemetry.Track("account.send_code.count", 1);
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
