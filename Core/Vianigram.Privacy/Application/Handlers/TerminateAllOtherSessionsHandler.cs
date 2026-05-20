// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Privacy.Application.UseCases;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.Entities;
using Vianigram.Privacy.Domain.ValueObjects;
using Vianigram.Privacy.Infrastructure;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Application.Handlers
{
    /// <summary>
    /// Handles <see cref="TerminateAllOtherSessionsCommand"/>: encodes
    /// <c>auth.resetAuthorizations#9fab0d1a</c> and clears every non-current
    /// session from the aggregate cache.
    /// </summary>
    internal sealed class TerminateAllOtherSessionsHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public TerminateAllOtherSessionsHandler(
            IMtProtoRpcPort rpc,
            PrivacyProfile profile,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (profile == null) throw new ArgumentNullException("profile");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _rpc = rpc;
            _profile = profile;
            _bus = bus;
            _log = new TimestampedLogger(log, "Privacy.TerminateAllOtherSessions");
            _clock = clock;
        }

        public async Task<Result<Unit, PrivacyError>> HandleAsync(TerminateAllOtherSessionsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("null command"));

            byte[] request = TlEncoder.EncodeResetAllAuthorizations();
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("auth.resetAuthorizations rpc failed: " + mapped);
                return Result<Unit, PrivacyError>.Fail(mapped);
            }

            _profile.RecordAllOtherSessionsTerminated(_clock.UtcNow);
            HandlerEventBridge.Drain(_profile, _bus);
            return Result<Unit, PrivacyError>.Ok(Unit.Value);
        }
    }
}
