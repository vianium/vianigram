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
    /// Initiator side of a new phone call. Encodes
    /// <c>phone.requestCall</c> with the SHA256 hash of the native call-DH
    /// public value, persists the aggregate in
    /// <see cref="CallSessionState.Requesting"/>, then transitions to
    /// <see cref="CallSessionState.Waiting"/> on the server reply.
    ///
    /// <para><b>One-active-call invariant:</b> the handler queries the
    /// repository for a non-Discarded session and fails with
    /// <see cref="CallErrorKind.AlreadyInCall"/> if one exists. This
    /// matches Telegram's UX rule and the hardware reality (one
    /// microphone). The actual transition out of Active to Discarded for
    /// the prior call is the user's responsibility; we don't auto-hang-up
    /// to free the slot.</para>
    ///
    /// <para>Errors:
    /// <list type="bullet">
    ///   <item>Network / cancellation -&gt; <see cref="CallError.NetworkError"/>.</item>
    ///   <item>Peer unreachable      -&gt; <see cref="CallError.ParticipantUnavailable"/>.</item>
    ///   <item>TL decode failure    -&gt; <see cref="CallError.ProtocolError"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    internal sealed class RequestCallHandler
    {
        private readonly ICallRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ICallCryptoVault _crypto;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public RequestCallHandler(
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
            _log = new TimestampedLogger(log, "Calls.RequestCall");
            _clock = clock;
        }

        public async Task<Result<CallSession, CallError>> HandleAsync(RequestCallCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Result<CallSession, CallError>.Fail(CallError.Unknown("null command"));
            if (cmd.ParticipantUserId <= 0)
                return Result<CallSession, CallError>.Fail(CallError.NotInExpectedState("participant id required"));

            // Step 1: enforce one-active-call invariant.
            CallSession existing = await _repo.FindActiveAsync(ct).ConfigureAwait(false);
            if (existing != null)
            {
                return Result<CallSession, CallError>.Fail(
                    CallError.AlreadyInCall("an active call already exists: " + existing.CallId));
            }

            // Step 2: build the provisional aggregate while the server
            // assigns the real CallId. MarkWaiting replaces the zero id
            // after phone.requestCall succeeds.
            CallProtocol protocol = CallProtocol.Default;
            DateTime now = _clock.UtcNow;
            int randomId = unchecked((int)now.Ticks);
            CallSession session = CallSession.StartOutgoing(
                new CallId(0L), cmd.ParticipantUserId, cmd.ParticipantAccessHash, cmd.Video, protocol, now);

            // Step 3: encode the RPC with SHA256(g_a). The private
            // exponent belongs to the call crypto vault; managed signaling
            // only publishes the hash until phone.confirmCall.
            var cryptoResult = await _crypto.CreateOutgoingGAHashAsync(randomId, ct).ConfigureAwait(false);
            if (cryptoResult.IsFail)
                return Result<CallSession, CallError>.Fail(cryptoResult.Error);

            byte[] gAHash = cryptoResult.Value;
            byte[] request = TlEncoder.EncodeRequestCall(
                cmd.ParticipantUserId, cmd.ParticipantAccessHash, randomId, gAHash, protocol, cmd.Video);
            _log.Info("phone.requestCall begin: peer=" + cmd.ParticipantUserId
                + " accessHash=0x" + ((ulong)cmd.ParticipantAccessHash).ToString("x16")
                + " video=" + cmd.Video
                + " protocol=" + protocol
                + " bytes=" + request.Length);

            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                CallError mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("phone.requestCall failed: " + mapped);
                return Result<CallSession, CallError>.Fail(mapped);
            }

            // Step 4: decode the phone.PhoneCall response. We expect
            // phoneCallWaiting until the peer accepts.
            TlDecoder.DecodedPhoneCall decoded;
            try
            {
                decoded = TlDecoder.DecodePhoneCall(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<CallSession, CallError>.Fail(
                    CallError.ProtocolError("decode phone.PhoneCall response", ex));
            }

            _log.Info("phone.requestCall decoded: shape=" + decoded.Shape
                + " callId=0x" + ((ulong)decoded.CallId).ToString("x16")
                + " accessHash=0x" + ((ulong)decoded.AccessHash).ToString("x16")
                + " admin=" + decoded.AdminId
                + " participant=" + decoded.ParticipantId
                + " receiveDate=" + decoded.HasReceiveDate
                + " responseBytes=" + (rpcResult.Value == null ? 0 : rpcResult.Value.Length));

            CallId assigned = new CallId(decoded.CallId);
            var bindResult = _crypto.BindOutgoingCall(randomId, assigned);
            if (bindResult.IsFail)
                return Result<CallSession, CallError>.Fail(bindResult.Error);

            session.MarkWaiting(assigned, decoded.AccessHash, _clock.UtcNow);
            if (decoded.HasReceiveDate)
            {
                session.MarkRinging(_clock.UtcNow);
            }
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            _log.Info("phone.requestCall ok: callId=" + assigned + " peer=" + cmd.ParticipantUserId);
            return Result<CallSession, CallError>.Ok(session);
        }
    }
}
