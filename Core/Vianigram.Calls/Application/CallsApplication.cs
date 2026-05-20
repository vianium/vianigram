// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Application.Handlers;
using Vianigram.Calls.Application.UseCases;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.Events;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Inbound;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;

namespace Vianigram.Calls.Application
{
    /// <summary>
    /// <see cref="ICallsApi"/> implementation. Dispatches each public
    /// method to the matching handler, surfaces results as
    /// <c>Result&lt;T, CallError&gt;</c>, and re-broadcasts internal domain
    /// events on the kernel bus into two CLR events
    /// (<see cref="StateChanged"/>, <see cref="IncomingCall"/>) so XAML/UI
    /// consumers don't need an <see cref="IEventBus"/> dependency.
    ///
    /// All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="CallError"/>.
    /// </summary>
    public sealed class CallsApplication : ICallsApi, IDisposable
    {
        private readonly ICallRepository _repo;
        private readonly ICallCryptoCapabilityPort _cryptoCapability;
        private readonly IVoipMediaCapabilityPort _voipCapability;
        private readonly IVoipPlaybackSourcePort _playback;
        private readonly IUserAccessHashPort _userHashes;
        private readonly IVoipMediaPort _voip;
        private readonly ICallSignalingRpcPort _signalingRpc;
        private readonly IComponentLogger _log;

        private readonly RequestCallHandler _request;
        private readonly AcceptCallHandler _accept;
        private readonly DiscardCallHandler _discard;
        private readonly UpdateCallStateHandler _update;
        private readonly SetMutedHandler _setMuted;
        private readonly SetSpeakerHandler _setSpeaker;
        private readonly FlipCameraHandler _flipCamera;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<CallStateChangedEventArgs> StateChanged;
        public event EventHandler<CallReceivedEventArgs> IncomingCall;

        public CallsApplication(
            IMtProtoRpcPort rpc,
            ICallCryptoVault crypto,
            IVoipMediaPort voip,
            ICallRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
            : this(rpc, crypto, voip, repo, bus, logger, clock, /*userHashes*/ null)
        {
        }

        public CallsApplication(
            IMtProtoRpcPort rpc,
            ICallCryptoVault crypto,
            IVoipMediaPort voip,
            ICallRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IUserAccessHashPort userHashes)
            : this(rpc, crypto, voip, repo, bus, logger, clock, userHashes, null)
        {
        }

        public CallsApplication(
            IMtProtoRpcPort rpc,
            ICallCryptoVault crypto,
            IVoipMediaPort voip,
            ICallRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            IUserAccessHashPort userHashes,
            ICallSignalingRpcPort signalingRpc)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (crypto == null) throw new ArgumentNullException("crypto");
            if (voip == null) throw new ArgumentNullException("voip");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _repo = repo;
            _voip = voip;
            _cryptoCapability = crypto as ICallCryptoCapabilityPort;
            _voipCapability = voip as IVoipMediaCapabilityPort;
            _playback = voip as IVoipPlaybackSourcePort;
            _userHashes = userHashes; // optional; null in legacy compositions
            _signalingRpc = signalingRpc ?? voip as ICallSignalingRpcPort;
            _log = new TimestampedLogger(logger, "Calls.Application");

            _request = new RequestCallHandler(repo, rpc, crypto, bus, logger, clock);
            _accept = new AcceptCallHandler(repo, rpc, crypto, bus, logger, clock);
            _discard = new DiscardCallHandler(repo, rpc, crypto, voip, bus, logger, clock);
            _update = new UpdateCallStateHandler(repo, rpc, crypto, voip, bus, logger, clock);
            _setMuted = new SetMutedHandler(repo, voip, logger);
            _setSpeaker = new SetSpeakerHandler(repo, voip, logger);
            _flipCamera = new FlipCameraHandler(repo, voip, logger);

            _subs = new IDisposable[]
            {
                bus.Subscribe<CallRequested>(OnRequested),
                bus.Subscribe<CallReceived>(OnReceived),
                bus.Subscribe<CallAccepted>(OnAccepted),
                bus.Subscribe<CallActive>(OnActive),
                bus.Subscribe<CallDiscarded>(OnDiscarded),
                bus.Subscribe<CallStateChanged>(OnStateChanged)
            };

            _voip.SignalingDataProduced += OnVoipSignalingDataProduced;
            _voip.MediaStateChanged += OnVoipMediaStateChanged;
        }

        /// <summary>
        /// Sync / smoke-harness entry point for routing a decoded
        /// <c>updatePhoneCall</c>. Public so the host
        /// <c>Vianigram.Sync</c> reactor can drive transitions without
        /// going through the inbound API. Mirrors the same accessor the
        /// SecretChats context exposes for <c>updateEncryption</c>.
        /// </summary>
        public UpdateCallStateHandler UpdateHandler { get { return _update; } }

        public async Task<Result<Unit, CallError>> ReceiveSignalingDataAsync(CallId id, byte[] data, CancellationToken ct)
        {
            try
            {
                if (id.Value <= 0)
                    return Result<Unit, CallError>.Fail(CallError.ProtocolError("signaling call id must be positive"));
                if (data == null || data.Length == 0)
                    return Result<Unit, CallError>.Fail(CallError.ProtocolError("empty call signaling payload"));

                CallSession session = await _repo.FindAsync(id, ct).ConfigureAwait(false);
                if (session == null)
                {
                    _log.Debug("updatePhoneCallSignalingData ignored for unknown/expired call id=" + id
                        + " data=" + data.Length + "B");
                    return Result<Unit, CallError>.Ok(Unit.Value);
                }
                if (session.State == CallSessionState.Discarded)
                {
                    _log.Debug("updatePhoneCallSignalingData ignored for discarded call id=" + id
                        + " data=" + data.Length + "B");
                    return Result<Unit, CallError>.Ok(Unit.Value);
                }

                _log.Info("updatePhoneCallSignalingData -> native begin id=" + id
                    + " data=" + data.Length + "B");
                var result = await _voip.ReceiveSignalingDataAsync(id, data, ct).ConfigureAwait(false);
                if (result.IsFail)
                    _log.Warn("native ReceiveSignalingData failed id=" + id + ": " + result.Error);
                else
                    _log.Info("updatePhoneCallSignalingData -> native ok id=" + id);
                return result;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, CallError>.Fail(CallError.Unknown("ReceiveSignalingDataAsync failed", ex));
            }
        }

        // ---- ICallsApi -----------------------------------------------------

        public bool IsCallingAvailable
        {
            get { return GetCallingUnavailableReason() == null; }
        }

        public string CallingUnavailableReason
        {
            get
            {
                string reason = GetCallingUnavailableReason();
                return reason ?? string.Empty;
            }
        }

        public async Task<Result<CallSession, CallError>> RequestCallAsync(long participantUserId, bool video, CancellationToken ct)
        {
            try
            {
                CallError unavailable = GetCallingUnavailableError();
                if (unavailable != null)
                    return Result<CallSession, CallError>.Fail(unavailable);

                if (participantUserId <= 0)
                    return Result<CallSession, CallError>.Fail(CallError.NotInExpectedState("participantUserId must be positive"));
                // Resolve the user's access_hash before dispatching. The
                // server rejects phone.requestCall with USER_ID_INVALID
                // when access_hash=0 (the legacy 2-arg RequestCallCommand
                // overload). When the cache has nothing for the user we
                // still pass 0 — the request will fail at the server,
                // which the caller surfaces as ParticipantUnavailable.
                long accessHash = _userHashes != null ? _userHashes.GetUserAccessHash(participantUserId) : 0L;
                return await _request.HandleAsync(
                    new RequestCallCommand(participantUserId, accessHash, video), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<CallSession, CallError>.Fail(CallError.Unknown("RequestCallAsync failed", ex));
            }
        }

        public async Task<Result<CallSession, CallError>> AcceptCallAsync(CallId id, CancellationToken ct)
        {
            try
            {
                CallError unavailable = GetCallingUnavailableError();
                if (unavailable != null)
                    return Result<CallSession, CallError>.Fail(unavailable);

                return await _accept.HandleAsync(new AcceptCallCommand(id), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<CallSession, CallError>.Fail(CallError.Unknown("AcceptCallAsync failed", ex));
            }
        }

        public async Task<Result<Unit, CallError>> DiscardAsync(CallId id, DiscardReason reason, CancellationToken ct)
        {
            try
            {
                return await _discard.HandleAsync(new DiscardCallCommand(id, reason), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, CallError>.Fail(CallError.Unknown("DiscardAsync failed", ex));
            }
        }

        public async Task<Result<Unit, CallError>> SetMutedAsync(CallId id, bool muted, CancellationToken ct)
        {
            try
            {
                return await _setMuted.HandleAsync(new SetMutedCommand(id, muted), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, CallError>.Fail(CallError.Unknown("SetMutedAsync failed", ex));
            }
        }

        public async Task<Result<Unit, CallError>> SetSpeakerAsync(CallId id, bool on, CancellationToken ct)
        {
            try
            {
                return await _setSpeaker.HandleAsync(new SetSpeakerCommand(id, on), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, CallError>.Fail(CallError.Unknown("SetSpeakerAsync failed", ex));
            }
        }

        public async Task<Result<Unit, CallError>> FlipCameraAsync(CallId id, CancellationToken ct)
        {
            try
            {
                return await _flipCamera.HandleAsync(new FlipCameraCommand(id), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, CallError>.Fail(CallError.Unknown("FlipCameraAsync failed", ex));
            }
        }

        public CallSession GetSession(CallId id)
        {
            // Synchronous lookup against the in-memory repository. Other
            // adapters (SQLite) may need to block briefly here; that's
            // acceptable per principles.md §M9 because GetSession is a UI
            // affordance, not a hot-path operation.
            try
            {
                return _repo.FindAsync(id, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch
            {
                return null;
            }
        }

        public object CreatePlaybackSource(CallId id)
        {
            try
            {
                return _playback == null ? null : _playback.CreatePlaybackSource(id);
            }
            catch
            {
                return null;
            }
        }

        public async Task<Result<IList<CallSession>, CallError>> ListRecentAsync(CancellationToken ct)
        {
            try
            {
                IList<CallSession> sessions = await _repo.ListAsync(ct).ConfigureAwait(false);
                return Result<IList<CallSession>, CallError>.Ok(sessions ?? new CallSession[0]);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<CallSession>, CallError>.Fail(CallError.Unknown("ListRecentAsync failed", ex));
            }
        }

        // ---- bus -> CLR-event bridge ---------------------------------------

        private void OnRequested(CallRequested e)
        {
            RaiseState(CallStateChangedEventArgs.ChangeReason.Requested, e.CallId, CallSessionState.Waiting, e.At);
        }

        private void OnReceived(CallReceived e)
        {
            var h = IncomingCall;
            if (h == null) return;
            try
            {
                h(this, new CallReceivedEventArgs(e.CallId, e.FromUserId, e.Video, e.At));
            }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        private void OnAccepted(CallAccepted e)
        {
            RaiseState(CallStateChangedEventArgs.ChangeReason.Accepted, e.CallId, CallSessionState.Pending, e.At);
        }

        private void OnActive(CallActive e)
        {
            RaiseState(CallStateChangedEventArgs.ChangeReason.Active, e.CallId, CallSessionState.Active, e.At);
        }

        private void OnDiscarded(CallDiscarded e)
        {
            RaiseState(CallStateChangedEventArgs.ChangeReason.Discarded, e.CallId, CallSessionState.Discarded, e.At);
        }

        private void OnStateChanged(CallStateChanged e)
        {
            RaiseState(CallStateChangedEventArgs.ChangeReason.StateChanged, e.CallId, e.Current, e.At);
        }

        private void RaiseState(CallStateChangedEventArgs.ChangeReason reason, CallId id, CallSessionState state, DateTime at)
        {
            var h = StateChanged;
            if (h == null) return;
            try
            {
                h(this, new CallStateChangedEventArgs(reason, id, state, at));
            }
            catch
            {
                // Swallow downstream subscriber faults.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { _voip.SignalingDataProduced -= OnVoipSignalingDataProduced; }
            catch { }
            try { _voip.MediaStateChanged -= OnVoipMediaStateChanged; }
            catch { }
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }

        private void OnVoipSignalingDataProduced(object sender, CallSignalingDataProducedEventArgs args)
        {
            var ignored = SendProducedSignalingAsync(args);
        }

        private async Task SendProducedSignalingAsync(CallSignalingDataProducedEventArgs args)
        {
            if (args == null || args.Data == null || args.Data.Length == 0) return;
            if (_signalingRpc == null)
            {
                _log.Warn("native produced signaling but ICallSignalingRpcPort is unavailable id=" + args.Id);
                return;
            }

            CallSession session = await _repo.FindAsync(args.Id, CancellationToken.None).ConfigureAwait(false);
            if (session == null)
            {
                _log.Warn("native produced signaling for unknown call id=" + args.Id);
                return;
            }

            _log.Info("phone.sendSignalingData begin id=" + args.Id
                + " data=" + args.Data.Length + "B");
            var result = await _signalingRpc
                .SendSignalingDataAsync(args.Id, session.AccessHash, args.Data, CancellationToken.None)
                .ConfigureAwait(false);
            if (result.IsFail)
                _log.Warn("phone.sendSignalingData failed id=" + args.Id + ": " + result.Error);
            else
                _log.Info("phone.sendSignalingData ok id=" + args.Id);
        }

        private void OnVoipMediaStateChanged(object sender, CallMediaStateChangedEventArgs args)
        {
            if (args == null) return;
            _log.Info("native media state id=" + args.Id
                + " state=" + args.State
                + " detail=" + args.Detail);
        }

        private CallError GetCallingUnavailableError()
        {
            string reason = GetCallingUnavailableReason();
            return reason == null ? null : CallError.MediaPlaneFailed(reason);
        }

        private string GetCallingUnavailableReason()
        {
            if (_cryptoCapability == null)
                return "Telegram call crypto capability is unknown";

            if (!_cryptoCapability.CanExchangeCallKeys)
            {
                string reason = _cryptoCapability.UnavailableReason;
                return string.IsNullOrEmpty(reason) ? "Telegram call crypto is unavailable" : reason;
            }

            if (_voipCapability != null && !_voipCapability.CanStartCalls)
            {
                string reason = _voipCapability.UnavailableReason;
                return string.IsNullOrEmpty(reason) ? "native VoIP media plane is unavailable" : reason;
            }

            return null;
        }
    }
}
