// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CheckUsernameHandler.cs — Vianigram.Account.Application.Handlers
// Pre-flight account.checkUsername availability probe.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;

namespace Vianigram.Account.Application.Handlers
{
    /// <summary>
    /// Issues account.checkUsername, returning <c>true</c> when the requested
    /// username is available for the signed-in user. Server-side validation
    /// errors (USERNAME_INVALID etc.) propagate as typed AccountErrors.
    /// </summary>
    internal sealed class CheckUsernameHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public CheckUsernameHandler(
            IMtProtoRpcPort rpc,
            ILogger log,
            ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _rpc = rpc;
            _log = new TimestampedLogger(log, "Account.CheckUsername");
            _telemetry = telemetry;
        }

        public async Task<Result<bool, AccountError>> HandleAsync(
            string username,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(username))
            {
                return Result<bool, AccountError>.Fail(
                    AccountError.NotInExpectedState("CheckUsername requires a non-empty username"));
            }

            var rpcResult = await _rpc.AccountCheckUsernameAsync(username, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _telemetry.Track("account.check_username.error", 1);
                _log.Warn("account.checkUsername failed: " + rpcResult.Error);
                return Result<bool, AccountError>.Fail(rpcResult.Error);
            }

            _telemetry.Track("account.check_username.count", 1);
            return Result<bool, AccountError>.Ok(rpcResult.Value);
        }
    }
}
