// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GetSelfHandler.cs — Vianigram.Account.Application.Handlers
// Reads users.getFullUser(inputUserSelf) and projects to a SelfProfile value object.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;

namespace Vianigram.Account.Application.Handlers
{
    /// <summary>
    /// Calls users.getFullUser(inputUserSelf) and copies the wire response
    /// into a <see cref="SelfProfile"/>. Stateless — relies entirely on the
    /// signed-in session bound to the underlying rpc port.
    /// </summary>
    internal sealed class GetSelfHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public GetSelfHandler(
            IMtProtoRpcPort rpc,
            ILogger log,
            ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _rpc = rpc;
            _log = new TimestampedLogger(log, "Account.GetSelf");
            _telemetry = telemetry;
        }

        public async Task<Result<SelfProfile, AccountError>> HandleAsync(CancellationToken ct)
        {
            var rpcResult = await _rpc.UsersGetFullUserAsync(InputUserSelf.Instance, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _telemetry.Track("account.get_self.error", 1);
                _log.Warn("users.getFullUser failed: " + rpcResult.Error);
                return Result<SelfProfile, AccountError>.Fail(rpcResult.Error);
            }

            var wire = rpcResult.Value;
            if (wire == null)
            {
                return Result<SelfProfile, AccountError>.Fail(
                    AccountError.Unknown("users.getFullUser returned null wire"));
            }

            var profile = new SelfProfile(
                wire.UserId,
                wire.FirstName,
                wire.LastName,
                wire.Username,
                wire.Phone,
                wire.Bio);

            _telemetry.Track("account.get_self.count", 1);
            return Result<SelfProfile, AccountError>.Ok(profile);
        }
    }
}
