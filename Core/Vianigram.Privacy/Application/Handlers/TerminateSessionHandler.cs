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
    /// Handles <see cref="TerminateSessionCommand"/>: encodes
    /// <c>account.resetAuthorization#df77f3bc</c> and drops the session from
    /// the aggregate cache. Refuses to terminate the session marked
    /// <c>IsCurrent</c> in the cache (a faster typed error than the server's
    /// FRESH_RESET_AUTHORISATION_FORBIDDEN).
    /// </summary>
    internal sealed class TerminateSessionHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public TerminateSessionHandler(
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
            _log = new TimestampedLogger(log, "Privacy.TerminateSession");
            _clock = clock;
        }

        public async Task<Result<Unit, PrivacyError>> HandleAsync(TerminateSessionCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("null command"));
            if (cmd.Hash == 0)
                return Result<Unit, PrivacyError>.Fail(PrivacyError.InvalidValue("hash must be non-zero"));

            // Pre-empt: if the cache says the hash is the current session,
            // refuse without a roundtrip.
            var sessions = _profile.Sessions;
            for (int i = 0; i < sessions.Count; i++)
            {
                if (sessions[i].Hash == cmd.Hash && sessions[i].IsCurrent)
                {
                    return Result<Unit, PrivacyError>.Fail(PrivacyError.CurrentSessionTermination("hash refers to the current session"));
                }
            }

            byte[] request = TlEncoder.EncodeResetAuthorization(cmd.Hash);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("account.resetAuthorization rpc failed: " + mapped);
                return Result<Unit, PrivacyError>.Fail(mapped);
            }

            // The server returns boolTrue / boolFalse — we treat both as
            // "operation accepted" because the cache invalidation is the
            // user-visible effect we care about. Future reads will reconcile.
            _profile.RecordSessionTerminated(cmd.Hash, _clock.UtcNow);
            HandlerEventBridge.Drain(_profile, _bus);
            return Result<Unit, PrivacyError>.Ok(Unit.Value);
        }
    }
}
