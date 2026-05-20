// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
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
    /// Handles <see cref="GetActiveSessionsCommand"/>: encodes
    /// <c>account.getAuthorizations#e320c158</c>, decodes the
    /// <c>account.authorizations#4bff8ea0</c> response, and refreshes the
    /// aggregate's session cache.
    /// </summary>
    internal sealed class GetActiveSessionsHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public GetActiveSessionsHandler(
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
            _log = new TimestampedLogger(log, "Privacy.GetActiveSessions");
            _clock = clock;
        }

        public async Task<Result<IList<ActiveSession>, PrivacyError>> HandleAsync(GetActiveSessionsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<ActiveSession>, PrivacyError>.Fail(PrivacyError.Unknown("null command"));

            byte[] request = TlEncoder.EncodeGetAuthorizations();
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("account.getAuthorizations rpc failed: " + mapped);
                return Result<IList<ActiveSession>, PrivacyError>.Fail(mapped);
            }

            try
            {
                IList<ActiveSession> sessions = TlDecoder.DecodeAuthorizations(rpcResult.Value);
                _profile.RecordSessions(sessions, _clock.UtcNow);
                HandlerEventBridge.Drain(_profile, _bus);
                return Result<IList<ActiveSession>, PrivacyError>.Ok(_profile.Sessions);
            }
            catch (Exception ex)
            {
                _log.Warn("account.getAuthorizations decode failed: " + ex.Message);
                return Result<IList<ActiveSession>, PrivacyError>.Fail(PrivacyError.Unknown("getAuthorizations decode failed", ex));
            }
        }
    }
}
