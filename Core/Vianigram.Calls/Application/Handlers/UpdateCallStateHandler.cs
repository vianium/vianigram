// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
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
    /// Routes updatePhoneCall deliveries into the Calls aggregate. The
    /// initiator-side phoneCallAccepted branch follows Telegram's call flow:
    /// compute confirm material, send phone.confirmCall, then activate media
    /// from the returned phoneCall constructor.
    /// </summary>
    public sealed class UpdateCallStateHandler
    {
        private readonly ICallRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly ICallCryptoVault _crypto;
        private readonly IVoipMediaPort _voip;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;
        private string _callConfigJson;

        public UpdateCallStateHandler(
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
            _log = new TimestampedLogger(log, "Calls.UpdateCallState");
            _clock = clock;
        }

        public async Task<Result<Unit, CallError>> HandleAsync(UpdateCallStateCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Result<Unit, CallError>.Fail(CallError.Unknown("null command"));
            if (cmd.PhoneCallTlBytes == null || cmd.PhoneCallTlBytes.Length < 4)
                return Result<Unit, CallError>.Fail(CallError.ProtocolError("empty PhoneCall update"));

            TlDecoder.DecodedPhoneCall decoded;
            try
            {
                decoded = TlDecoder.DecodePhoneCall(cmd.PhoneCallTlBytes);
            }
            catch (Exception ex)
            {
                return Result<Unit, CallError>.Fail(CallError.ProtocolError("PhoneCall decode", ex));
            }

            DateTime now = _clock.UtcNow;
            CallId id = new CallId(decoded.CallId);
            CallSession session = await _repo.FindAsync(id, ct).ConfigureAwait(false);
            _log.Info("updatePhoneCall decoded: shape=" + decoded.Shape
                + " callId=0x" + ((ulong)decoded.CallId).ToString("x16")
                + " accessHash=0x" + ((ulong)decoded.AccessHash).ToString("x16")
                + " admin=" + decoded.AdminId
                + " participant=" + decoded.ParticipantId
                + " receiveDate=" + decoded.HasReceiveDate
                + (decoded.HasProtocol ? " protocol=" + decoded.Protocol : string.Empty)
                + (decoded.Shape == TlDecoder.DecodedPhoneCall.ShapeKind.Discarded
                    ? " discardReason=" + MapWireReason(decoded.DiscardReasonCtor)
                        + "/0x" + decoded.DiscardReasonCtor.ToString("x8")
                        + " duration=" + decoded.DurationSeconds + "s"
                    : string.Empty)
                + " session=" + (session == null ? "null" : (session.State + "/initiator=" + session.IsInitiator)));

            switch (decoded.Shape)
            {
                case TlDecoder.DecodedPhoneCall.ShapeKind.Empty:
                    _log.Debug("phoneCallEmpty id=" + decoded.CallId);
                    return Result<Unit, CallError>.Ok(Unit.Value);

                case TlDecoder.DecodedPhoneCall.ShapeKind.Waiting:
                    if (session != null && session.IsInitiator && session.State == CallSessionState.Waiting)
                    {
                        if (decoded.HasReceiveDate)
                        {
                            session.MarkRinging(now);
                            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                            HandlerEventBridge.Drain(session, _bus);
                            _log.Info("phoneCallWaiting receive_date -> Ringing id=" + id);
                        }
                    }
                    else if (session != null)
                    {
                        _log.Debug("phoneCallWaiting ignored id=" + id + " state=" + session.State
                            + " initiator=" + session.IsInitiator
                            + " receiveDate=" + decoded.HasReceiveDate);
                    }
                    else
                    {
                        _log.Warn("phoneCallWaiting for unknown id=" + decoded.CallId);
                    }
                    return Result<Unit, CallError>.Ok(Unit.Value);

                case TlDecoder.DecodedPhoneCall.ShapeKind.Requested:
                    if (session == null)
                    {
                        var hashResult = _crypto.RegisterIncomingGAHash(id, decoded.GAOrB);
                        if (hashResult.IsFail)
                            return Result<Unit, CallError>.Fail(hashResult.Error);

                        session = CallSession.StartIncoming(
                            id, decoded.AdminId, decoded.AccessHash, decoded.Video, CallProtocol.Default, now);
                        session.MarkReceived(now);
                        await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                        HandlerEventBridge.Drain(session, _bus);
                        _log.Info("phoneCallRequested id=" + decoded.CallId + " from=" + decoded.AdminId);
                        await SendReceivedCallAsync(session, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        _log.Debug("phoneCallRequested duplicate id=" + decoded.CallId + " state=" + session.State);
                    }
                    return Result<Unit, CallError>.Ok(Unit.Value);

                case TlDecoder.DecodedPhoneCall.ShapeKind.Accepted:
                    return await ConfirmAcceptedCallAsync(id, session, decoded, ct).ConfigureAwait(false);

                case TlDecoder.DecodedPhoneCall.ShapeKind.Established:
                    return await ApplyEstablishedCallAsync(id, session, decoded, ct).ConfigureAwait(false);

                case TlDecoder.DecodedPhoneCall.ShapeKind.Discarded:
                    return await ApplyDiscardedCallAsync(id, session, decoded, ct).ConfigureAwait(false);
            }

            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        private async Task SendReceivedCallAsync(CallSession session, CancellationToken ct)
        {
            if (session == null) return;

            byte[] req = TlEncoder.EncodeReceivedCall(session.CallId.Value, session.AccessHash);
            _log.Info("phone.receivedCall begin id=" + session.CallId
                + " accessHash=0x" + ((ulong)session.AccessHash).ToString("x16"));

            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                CallError mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("phone.receivedCall failed id=" + session.CallId + ": " + mapped);
                return;
            }

            uint ctor = PeekCtor(rpcResult.Value);
            _log.Info("phone.receivedCall ok id=" + session.CallId
                + " responseCtor=0x" + ctor.ToString("x8")
                + " bytes=" + (rpcResult.Value == null ? 0 : rpcResult.Value.Length));
        }

        private async Task<Result<Unit, CallError>> ConfirmAcceptedCallAsync(
            CallId id,
            CallSession session,
            TlDecoder.DecodedPhoneCall decoded,
            CancellationToken ct)
        {
            if (session == null)
            {
                _log.Warn("phoneCallAccepted for unknown id=" + decoded.CallId);
                return Result<Unit, CallError>.Fail(CallError.CallNotFound(id.ToString()));
            }
            if (!session.IsInitiator)
            {
                return Result<Unit, CallError>.Fail(
                    CallError.NotInExpectedState("phoneCallAccepted is initiator-only"));
            }
            if (session.State == CallSessionState.Active || session.State == CallSessionState.MediaConnecting)
            {
                _log.Debug("phoneCallAccepted duplicate ignored for active id=" + id);
                return Result<Unit, CallError>.Ok(Unit.Value);
            }
            if (session.State == CallSessionState.Discarded)
            {
                _log.Debug("phoneCallAccepted ignored for discarded id=" + id);
                return Result<Unit, CallError>.Ok(Unit.Value);
            }
            if (session.State != CallSessionState.Waiting &&
                session.State != CallSessionState.Ringing &&
                session.State != CallSessionState.Confirming)
            {
                _log.Warn("phoneCallAccepted ignored in state=" + session.State + " id=" + id);
                return Result<Unit, CallError>.Ok(Unit.Value);
            }

            if (session.State == CallSessionState.Waiting)
            {
                session.MarkRinging(_clock.UtcNow);
                await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                HandlerEventBridge.Drain(session, _bus);
                _log.Info("phoneCallAccepted -> Ringing id=" + id);
            }

            _log.Info("phoneCallAccepted id=" + decoded.CallId + " - sending phone.confirmCall");
            if (session.State != CallSessionState.Confirming)
            {
                session.MarkConfirming(_clock.UtcNow);
                await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                HandlerEventBridge.Drain(session, _bus);
            }

            var materialResult = await _crypto.AcceptPeerGBAsync(id, decoded.GAOrB, ct).ConfigureAwait(false);
            if (materialResult.IsFail)
                return Result<Unit, CallError>.Fail(materialResult.Error);

            ConfirmCallMaterial material = materialResult.Value;
            byte[] req = TlEncoder.EncodeConfirmCall(
                id.Value,
                session.AccessHash,
                material.GA,
                material.KeyFingerprint,
                session.Protocol);

            var rpcResult = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                CallError mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("phone.confirmCall failed: " + mapped);
                return Result<Unit, CallError>.Fail(mapped);
            }

            TlDecoder.DecodedPhoneCall confirmed;
            try
            {
                confirmed = TlDecoder.DecodePhoneCall(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<Unit, CallError>.Fail(
                    CallError.ProtocolError("decode phone.confirmCall response", ex));
            }

            if (confirmed.Shape != TlDecoder.DecodedPhoneCall.ShapeKind.Established)
            {
                _log.Warn("phone.confirmCall returned " + confirmed.Shape + " for callId=" + id);
                return Result<Unit, CallError>.Ok(Unit.Value);
            }

            _log.Info("phone.confirmCall established id=" + id
                + " endpoints=" + (confirmed.Endpoints == null ? 0 : confirmed.Endpoints.Count)
                + (confirmed.HasProtocol ? " protocol=" + confirmed.Protocol : string.Empty)
                + " fingerprint=0x" + ((ulong)confirmed.KeyFingerprint).ToString("x16"));

            return await ApplyEstablishedCallAsync(id, session, confirmed, ct).ConfigureAwait(false);
        }

        private async Task<Result<Unit, CallError>> ApplyEstablishedCallAsync(
            CallId id,
            CallSession session,
            TlDecoder.DecodedPhoneCall decoded,
            CancellationToken ct)
        {
            if (session == null)
            {
                _log.Warn("phoneCall received for unknown id=" + decoded.CallId);
                return Result<Unit, CallError>.Fail(CallError.CallNotFound(id.ToString()));
            }
            if (session.State == CallSessionState.Active)
            {
                return Result<Unit, CallError>.Ok(Unit.Value);
            }

            if (!session.IsInitiator)
            {
                var confirmResult = await _crypto.ConfirmPeerGAOrBAsync(
                    id, decoded.GAOrB, decoded.KeyFingerprint, ct).ConfigureAwait(false);
                if (confirmResult.IsFail)
                    return Result<Unit, CallError>.Fail(confirmResult.Error);
            }
            else
            {
                long localFingerprint = _crypto.GetLocalFingerprint(id);
                if (localFingerprint != 0 && decoded.KeyFingerprint != 0 && localFingerprint != decoded.KeyFingerprint)
                {
                    return Result<Unit, CallError>.Fail(CallError.FingerprintMismatch(
                        "local=0x" + ((ulong)localFingerprint).ToString("x16")
                        + " server=0x" + ((ulong)decoded.KeyFingerprint).ToString("x16")));
                }
            }

            IList<CallEndpoint> endpoints = decoded.Endpoints ?? new CallEndpoint[0];
            CallKeyHandle keyHandle = _crypto.GetSharedKeyHandle(id) ?? CallKeyHandle.Empty;
            CallProtocol mediaProtocol = decoded.HasProtocol ? decoded.Protocol : session.Protocol;
            string callConfigJson = await GetCallConfigJsonAsync(ct).ConfigureAwait(false);
            if (session.State != CallSessionState.MediaConnecting)
            {
                session.MarkMediaConnecting(_clock.UtcNow);
                await _repo.SaveAsync(session, ct).ConfigureAwait(false);
                HandlerEventBridge.Drain(session, _bus);
            }
            _log.Info("voip.StartAsync begin id=" + id
                + " endpoints=" + endpoints.Count
                + " protocol=" + mediaProtocol
                + " callConfig=" + (string.IsNullOrEmpty(callConfigJson) ? "empty" : (callConfigJson.Length + "B"))
                + " keyHandle=" + (string.IsNullOrEmpty(keyHandle.Value) ? "empty" : "present"));

            var startResult = await _voip.StartAsync(
                new CallStartContext(
                    id,
                    session.AccessHash,
                    session.IsInitiator,
                    session.IsVideo,
                    mediaProtocol,
                    decoded.KeyFingerprint,
                    keyHandle,
                    endpoints,
                    callConfigJson),
                ct).ConfigureAwait(false);
            if (startResult.IsFail)
            {
                _log.Warn("voip.StartAsync failed for callId=" + id + ": " + startResult.Error);
                return await ApplyMediaStartFailureAsync(id, session, startResult.Error, ct).ConfigureAwait(false);
            }

            DateTime now = _clock.UtcNow;
            KeyFingerprint fp = new KeyFingerprint(decoded.KeyFingerprint);
            session.MarkActive(fp, endpoints, now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            _log.Info("phoneCall active id=" + decoded.CallId
                + " endpoints=" + endpoints.Count
                + " fingerprint=0x" + ((ulong)decoded.KeyFingerprint).ToString("x16"));
            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        private async Task<Result<Unit, CallError>> ApplyMediaStartFailureAsync(
            CallId id,
            CallSession session,
            CallError mediaError,
            CancellationToken ct)
        {
            if (session == null)
                return Result<Unit, CallError>.Fail(mediaError ?? CallError.MediaPlaneFailed("VoIP media start failed"));

            DateTime now = _clock.UtcNow;
            CallDuration duration = session.ActiveAt == DateTime.MinValue
                ? CallDuration.Zero
                : CallDuration.FromInterval(session.ActiveAt, now);

            session.Discard(DiscardReason.Disconnect, now);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);

            var stopResult = await _voip.StopAsync(ct).ConfigureAwait(false);
            if (stopResult.IsFail)
            {
                _log.Warn("voip.StopAsync failed after media start failure callId=" + id + ": " + stopResult.Error);
            }
            _crypto.Drop(id);

            byte[] discard = TlEncoder.EncodeDiscardCall(
                id.Value,
                session.AccessHash,
                duration.Seconds,
                DiscardReason.Disconnect,
                /*connectionId*/ 0L,
                session.IsVideo);

            var rpcResult = await _rpc.CallAsync(discard, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                CallError mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("phone.discardCall after media start failure failed (applying local discard): " + mapped);
            }
            else
            {
                _log.Info("phone.discardCall sent after media start failure id=" + id
                    + " responseCtor=0x" + PeekCtor(rpcResult.Value).ToString("x8"));
            }

            _log.Warn("call discarded locally after media start failure id=" + id + ": " + mediaError);

            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        private async Task<Result<Unit, CallError>> ApplyDiscardedCallAsync(
            CallId id,
            CallSession session,
            TlDecoder.DecodedPhoneCall decoded,
            CancellationToken ct)
        {
            if (session == null)
            {
                _log.Debug("phoneCallDiscarded for unknown id=" + decoded.CallId);
                _crypto.Drop(id);
                return Result<Unit, CallError>.Ok(Unit.Value);
            }

            if (session.State == CallSessionState.Active)
            {
                var stopResult = await _voip.StopAsync(ct).ConfigureAwait(false);
                if (stopResult.IsFail)
                {
                    _log.Warn("voip.StopAsync failed during peer discard callId=" + id + ": " + stopResult.Error);
                }
            }

            _crypto.Drop(id);
            DiscardReason reason = MapWireReason(decoded.DiscardReasonCtor);
            session.Discard(reason, _clock.UtcNow);
            await _repo.SaveAsync(session, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(session, _bus);
            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        private static DiscardReason MapWireReason(uint ctor)
        {
            switch (ctor)
            {
                case TlDecoder.CtorDiscardReasonHangup: return DiscardReason.Hangup;
                case TlDecoder.CtorDiscardReasonDisconnect: return DiscardReason.Disconnect;
                case TlDecoder.CtorDiscardReasonMissed: return DiscardReason.Missed;
                case TlDecoder.CtorDiscardReasonBusy: return DiscardReason.Busy;
                default: return DiscardReason.Hangup;
            }
        }

        private static uint PeekCtor(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 4) return 0;
            return (uint)bytes[0]
                | ((uint)bytes[1] << 8)
                | ((uint)bytes[2] << 16)
                | ((uint)bytes[3] << 24);
        }

        private async Task<string> GetCallConfigJsonAsync(CancellationToken ct)
        {
            if (_callConfigJson != null) return _callConfigJson;
            try
            {
                var rpcResult = await _rpc.CallAsync(TlEncoder.EncodeGetCallConfig(), ct).ConfigureAwait(false);
                if (rpcResult.IsFail)
                {
                    _log.Warn("phone.getCallConfig failed: " + rpcResult.Error);
                    _callConfigJson = string.Empty;
                    return _callConfigJson;
                }

                _callConfigJson = TlDecoder.DecodeDataJson(rpcResult.Value) ?? string.Empty;
                _log.Info("phone.getCallConfig cached bytes=" + _callConfigJson.Length);
                return _callConfigJson;
            }
            catch (Exception ex)
            {
                _log.Warn("phone.getCallConfig decode failed: " + ex.GetType().Name + ": " + ex.Message);
                _callConfigJson = string.Empty;
                return _callConfigJson;
            }
        }
    }
}
