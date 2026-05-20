// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Application.UseCases;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Infrastructure;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;

namespace Vianigram.Calls.Application.Handlers
{
    /// <summary>
    /// Discard a phone call — issues <c>phone.discardCall</c>, stops the
    /// VoIP media plane via <see cref="IVoipMediaPort"/>, transitions the
    /// aggregate to <see cref="CallSessionState.Discarded"/>, and stages
    /// <see cref="Domain.Events.CallDiscarded"/>.
    ///
    /// <para><b>Idempotent:</b> invoking discard on an already-discarded
    /// session is a no-op (returns Ok without re-emitting events). The
    /// repository row is retained so the UI can display the call entry
    /// with an "ended" marker until the user clears it.</para>
    ///
    /// <para><b>Best-effort wire ack:</b> if the server-side
    /// <c>phone.discardCall</c> RPC fails (network blip, peer already
    /// discarded), we still apply the local discard. Discarded-locally-only
    /// is preferable to keeping the call alive on a dead session, and the
    /// server reconciles via its own <c>updatePhoneCall</c>
    /// <c>phoneCallDiscarded</c> push.</para>
    /// </summary>
    internal sealed class DiscardCallHandler
    {
        private readonly ICallRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ICallCryptoVault _crypto;
        private readonly IVoipMediaPort _voip;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public DiscardCallHandler(
            ICallRepository repo,
            IMtProtoRpcPort rpc,
            ICallCryptoVault crypto,
            IVoipMediaPort voip,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (voip == null) throw new ArgumentNullException("voip");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _crypto = crypto;
            _voip = voip;
            _bus = bus;
            _log = new TimestampedLogger(log, "Calls.DiscardCall");
            _clock = clock;
        }

        public async Task<Result<Unit, CallError>> HandleAsync(DiscardCallCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, CallError>.Fail(CallError.Unknown("null command"));

            CallSession session = await _repo.FindAsync(cmd.CallId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<Unit, CallError>.Fail(CallError.CallNotFound(cmd.CallId.ToString()));

            if (session.State == CallSessionState.Discarded)
            {
                // Idempotent — already gone.
                return Result<Unit, CallError>.Ok(Unit.Value);
            }

            // Step 1: stop the media plane if it was running.
            if (session.State == CallSessionState.Active || session.State == CallSessionState.MediaConnecting)
            {
                var stopResult = await _voip.StopAsync(ct).ConfigureAwait(false);
                if (stopResult.IsFail)
                {
                    // Log and continue — we still want to issue the wire
                    // discard and apply the local transition.
                    _log.Warn("voip.StopAsync failed during discard callId=" + cmd.CallId + ": " + stopResult.Error);
                }
            }
            else
            {
                _crypto.Drop(cmd.CallId);
            }

            // Step 2: send phone.discardCall. Compute the duration the
            // server expects (talk-time, zero for never-active calls).
            DateTime now = _clock.UtcNow;
            CallDuration duration = session.ActiveAt == DateTime.MinValue
                ? CallDuration.Zero
                : CallDuration.FromInterval(session.ActiveAt, now);

            byte[] req = TlEncoder.EncodeDiscardCall(
                cmd.CallId.Value, session.AccessHash, duration.Seconds, cmd.Reason, /*connectionId*/ 0L, session.IsVideo);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                CallError mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("phone.discardCall failed (applying local discard anyway): " + mapped);
            }

            // Step 3: apply the local transition unconditionally.
            session.Discard(cmd.Reason, _clock.UtcNow);
            _crypto.Drop(cmd.CallId);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            _log.Info("call discarded: callId=" + cmd.CallId + " reason=" + cmd.Reason
                + " duration=" + session.Duration);
            return Result<Unit, CallError>.Ok(Unit.Value);
        }
    }
}
