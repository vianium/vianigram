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
    /// Responder side of an incoming phone call. Handles the case where
    /// Sync has delivered <c>phoneCallRequested</c>; this handler:
    ///
    /// <list type="number">
    ///   <item>Looks up the pending session in the repository.</item>
    ///   <item>Generates our own <c>g_b</c> via the crypto vault (currently stubbed).</item>
    ///   <item>Sends <c>phone.acceptCall</c> with our <c>g_b</c> and the negotiated protocol.</item>
    ///   <item>Transitions the aggregate to <see cref="CallSessionState.Pending"/>.</item>
    /// </list>
    ///
    /// <para>The actual <see cref="CallSessionState.Active"/> transition
    /// happens later when the peer's <c>phone.confirmCall</c> arrives via
    /// <c>updatePhoneCall</c> — that path is owned by
    /// <see cref="UpdateCallStateHandler"/>.</para>
    /// </summary>
    internal sealed class AcceptCallHandler
    {
        private readonly ICallRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ICallCryptoVault _crypto;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public AcceptCallHandler(
            ICallRepository repo,
            IMtProtoRpcPort rpc,
            ICallCryptoVault crypto,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _crypto = crypto;
            _bus = bus;
            _log = new TimestampedLogger(log, "Calls.AcceptCall");
            _clock = clock;
        }

        public async Task<Result<CallSession, CallError>> HandleAsync(AcceptCallCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Result<CallSession, CallError>.Fail(CallError.Unknown("null command"));

            CallSession session = await _repo.FindAsync(cmd.CallId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<CallSession, CallError>.Fail(CallError.CallNotFound(cmd.CallId.ToString()));
            if (session.State != CallSessionState.Receiving || session.IsInitiator)
                return Result<CallSession, CallError>.Fail(
                    CallError.NotInExpectedState("session is not a pending incoming call: " + session.State));

            var cryptoResult = await _crypto.CreateIncomingGBAsync(cmd.CallId, ct).ConfigureAwait(false);
            if (cryptoResult.IsFail)
                return Result<CallSession, CallError>.Fail(cryptoResult.Error);

            byte[] gB = cryptoResult.Value;
            byte[] req = TlEncoder.EncodeAcceptCall(
                cmd.CallId.Value, session.AccessHash, gB, session.Protocol);
            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                CallError mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("phone.acceptCall failed: " + mapped);
                return Result<CallSession, CallError>.Fail(mapped);
            }

            DateTime now = _clock.UtcNow;
            session.MarkAccepted(now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            _log.Info("phone.acceptCall ok: callId=" + cmd.CallId);
            return Result<CallSession, CallError>.Ok(session);
        }
    }
}
