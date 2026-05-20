// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// UpdateProfileHandler.cs — Vianigram.Account.Application.Handlers
// Issues account.updateProfile (first/last/bio); username changes go to a separate flow.

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
    /// Updates first name, last name, and bio via account.updateProfile. The
    /// username field of the inbound API is reserved for a future
    /// account.updateUsername round-trip; in wave 1 it is forwarded to the
    /// adapter only when non-null but the wire response is not surfaced
    /// separately (the inbound contract returns Unit).
    /// </summary>
    internal sealed class UpdateProfileHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public UpdateProfileHandler(
            IMtProtoRpcPort rpc,
            ILogger log,
            ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _rpc = rpc;
            _log = new TimestampedLogger(log, "Account.UpdateProfile");
            _telemetry = telemetry;
        }

        public async Task<Result<Unit, AccountError>> HandleAsync(
            string firstName,
            string lastName,
            string username,
            string bio,
            CancellationToken ct)
        {
            // username is accepted by the inbound API but not yet round-tripped
            // here — the dedicated account.updateUsername call lands in wave 2.
            // The argument is preserved to keep the contract stable.
            string fn = firstName ?? string.Empty;
            string ln = lastName ?? string.Empty;
            string ab = bio ?? string.Empty;

            var rpcResult = await _rpc.AccountUpdateProfileAsync(fn, ln, ab, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _telemetry.Track("account.update_profile.error", 1);
                _log.Warn("account.updateProfile failed: " + rpcResult.Error);
                return Result<Unit, AccountError>.Fail(rpcResult.Error);
            }

            if (username != null)
            {
                _log.Debug("update_profile: username field captured but updateUsername round-trip lands in wave 2");
            }

            _telemetry.Track("account.update_profile.count", 1);
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }
    }
}
