// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Calls.Domain.Events;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Calls.Domain.Entities
{
    /// <summary>
    /// Aggregate root for one live phone call.
    ///
    /// <para><b>Identity:</b> <see cref="CallId"/> (server-assigned during
    /// <c>phone.requestCall</c>). While the session is in
    /// <see cref="CallSessionState.Requesting"/> the id may be a placeholder
    /// (0); the server populates it when responding.</para>
    ///
    /// <para><b>State machines:</b>
    /// <list type="bullet">
    ///   <item>Outgoing: <c>Requesting → Waiting → Ringing → Active → Discarded</c>.</item>
    ///   <item>Incoming: <c>Receiving → Pending → Active → Discarded</c>.</item>
    /// </list>
    /// Each transition method is the only entry point that can mutate
    /// <see cref="State"/>; invariants are checked locally and a domain
    /// event is staged for the handler to publish post-persistence.</para>
    ///
    /// <para><b>Key isolation (M3):</b> the aggregate carries only
    /// <see cref="Fingerprint"/> (8 non-secret bytes derived from
    /// SHA1(auth_key)). Raw key material lives in the native VoIP /
    /// crypto vault and is referenced via <c>IVoipMediaPort</c> by an
    /// opaque blob the application layer hands over via
    /// <see cref="MarkActive"/>.</para>
    ///
    /// <para><b>Event staging:</b> mutators append to a private pending
    /// list drained by <see cref="DequeuePendingEvents"/> after persistence
    /// — keeps the aggregate dependency-free and makes the domain events
    /// transactional with the state change. Every transition also stages a
    /// <see cref="CallStateChanged"/> coarse-grained pulse.</para>
    ///
    /// <para><b>One-active-call invariant:</b> the application layer
    /// (<c>CallsApplication</c>) enforces that only one non-Discarded
    /// session exists at a time per device — Telegram's UX rule and a
    /// hardware reality (one microphone). The aggregate itself doesn't
    /// know about siblings; the application checks the repository before
    /// constructing a new aggregate.</para>
    /// </summary>
    public sealed class CallSession
    {
        private CallId _callId;
        private long _accessHash;
        private long _peerUserId;
        private bool _isInitiator;
        private bool _isVideo;
        private CallSessionState _state;
        private CallProtocol _protocol;
        private KeyFingerprint _fingerprint;
        private List<CallEndpoint> _endpoints;
        private DateTime _createdAt;
        private DateTime _lastActivityAt;
        private DateTime _activeAt;
        private CallDuration _duration;
        private DiscardReason _discardReason;
        private readonly List<IDomainEvent> _pending;

        /// <summary>
        /// Construct an outgoing call in <see cref="CallSessionState.Requesting"/>
        /// — the local user is initiating <c>phone.requestCall</c>. The
        /// <paramref name="callId"/> is normally <c>0</c> until the server
        /// replies; the application layer populates it via
        /// <see cref="MarkWaiting"/>.
        /// </summary>
        public static CallSession StartOutgoing(CallId callId, long peerUserId, long accessHash, bool video, CallProtocol protocol, DateTime now)
        {
            if (peerUserId <= 0) throw new ArgumentException("peerUserId must be positive", "peerUserId");
            return new CallSession(callId, peerUserId, accessHash, /*isInitiator*/ true, video, CallSessionState.Requesting, protocol, now);
        }

        /// <summary>
        /// Construct a session for an incoming <c>phoneCallRequested</c>
        /// (peer initiated) in <see cref="CallSessionState.Receiving"/>.
        /// </summary>
        public static CallSession StartIncoming(CallId callId, long fromUserId, long accessHash, bool video, CallProtocol protocol, DateTime now)
        {
            if (fromUserId <= 0) throw new ArgumentException("fromUserId must be positive", "fromUserId");
            return new CallSession(callId, fromUserId, accessHash, /*isInitiator*/ false, video, CallSessionState.Receiving, protocol, now);
        }

        private CallSession(CallId callId, long peerUserId, long accessHash, bool isInitiator, bool video, CallSessionState state, CallProtocol protocol, DateTime now)
        {
            _callId = callId;
            _peerUserId = peerUserId;
            _accessHash = accessHash;
            _isInitiator = isInitiator;
            _isVideo = video;
            _state = state;
            _protocol = protocol;
            _fingerprint = new KeyFingerprint(0L);
            _endpoints = new List<CallEndpoint>(0);
            _createdAt = now;
            _lastActivityAt = now;
            _activeAt = DateTime.MinValue;
            _duration = CallDuration.Zero;
            _discardReason = DiscardReason.Hangup;
            _pending = new List<IDomainEvent>(8);
        }

        // ---- read-only projection ------------------------------------------

        public CallId CallId { get { return _callId; } }
        public long AccessHash { get { return _accessHash; } }
        public long PeerUserId { get { return _peerUserId; } }
        public bool IsInitiator { get { return _isInitiator; } }
        public bool IsVideo { get { return _isVideo; } }
        public CallSessionState State { get { return _state; } }
        public CallProtocol Protocol { get { return _protocol; } }
        public KeyFingerprint Fingerprint { get { return _fingerprint; } }
        public DateTime CreatedAt { get { return _createdAt; } }
        public DateTime LastActivityAt { get { return _lastActivityAt; } }
        public DateTime ActiveAt { get { return _activeAt; } }
        public CallDuration Duration { get { return _duration; } }
        public DiscardReason DiscardReason { get { return _discardReason; } }

        public IList<CallEndpoint> SnapshotEndpoints()
        {
            return _endpoints.ToArray();
        }

        public bool IsTerminal { get { return _state == CallSessionState.Discarded; } }

        // ---- transitions ---------------------------------------------------

        /// <summary>
        /// Server has acknowledged <c>phone.requestCall</c>: assign the
        /// real <see cref="CallId"/> and move to
        /// <see cref="CallSessionState.Waiting"/>. Stages
        /// <see cref="CallRequested"/>.
        /// </summary>
        public void MarkWaiting(CallId serverAssignedId, long accessHash, DateTime now)
        {
            if (_state != CallSessionState.Requesting)
                throw new InvalidOperationException("MarkWaiting: expected state Requesting; was " + _state);
            CallSessionState previous = _state;
            _callId = serverAssignedId;
            _accessHash = accessHash;
            _state = CallSessionState.Waiting;
            _lastActivityAt = now;
            Stage(new CallRequested(_callId, _peerUserId, _isVideo, now));
            Stage(new CallStateChanged(_callId, previous, _state, now));
        }

        /// <summary>
        /// Outbound side: server has signalled the peer's device received
        /// the offer (<c>phoneCallWaiting.receive_date</c> set, or
        /// <c>phoneCallAccepted</c> in transit). Move to
        /// <see cref="CallSessionState.Ringing"/>. Idempotent if already
        /// past Waiting.
        /// </summary>
        public void MarkRinging(DateTime now)
        {
            if (_state == CallSessionState.Ringing) return;
            if (_state != CallSessionState.Waiting)
                throw new InvalidOperationException("MarkRinging: expected state Waiting; was " + _state);
            CallSessionState previous = _state;
            _state = CallSessionState.Ringing;
            _lastActivityAt = now;
            Stage(new CallStateChanged(_callId, previous, _state, now));
        }

        /// <summary>
        /// Inbound side: local user has accepted the ringing call. Move
        /// from <see cref="CallSessionState.Receiving"/> to
        /// <see cref="CallSessionState.Pending"/> (<c>phone.acceptCall</c>
        /// is in flight). Stages <see cref="CallAccepted"/>.
        /// </summary>
        public void MarkAccepted(DateTime now)
        {
            if (_state != CallSessionState.Receiving)
                throw new InvalidOperationException("MarkAccepted: expected state Receiving; was " + _state);
            if (_isInitiator)
                throw new InvalidOperationException("MarkAccepted: only the responder accepts");
            CallSessionState previous = _state;
            _state = CallSessionState.Pending;
            _lastActivityAt = now;
            Stage(new CallAccepted(_callId, now));
            Stage(new CallStateChanged(_callId, previous, _state, now));
        }

        public void MarkConfirming(DateTime now)
        {
            if (_state == CallSessionState.Confirming) return;
            if (_state != CallSessionState.Ringing && _state != CallSessionState.Waiting)
                throw new InvalidOperationException("MarkConfirming: expected state Ringing|Waiting; was " + _state);
            CallSessionState previous = _state;
            _state = CallSessionState.Confirming;
            _lastActivityAt = now;
            Stage(new CallStateChanged(_callId, previous, _state, now));
        }

        public void MarkMediaConnecting(DateTime now)
        {
            if (_state == CallSessionState.MediaConnecting) return;
            if (_state != CallSessionState.Pending &&
                _state != CallSessionState.Ringing &&
                _state != CallSessionState.Waiting &&
                _state != CallSessionState.Confirming)
                throw new InvalidOperationException("MarkMediaConnecting: expected state Pending|Ringing|Waiting|Confirming; was " + _state);
            CallSessionState previous = _state;
            _state = CallSessionState.MediaConnecting;
            _lastActivityAt = now;
            Stage(new CallStateChanged(_callId, previous, _state, now));
        }

        /// <summary>
        /// Mark inbound <c>phoneCallRequested</c> received — the call
        /// arrived from Sync. Idempotent — repeat updates from Sync are no-ops.
        /// Stages <see cref="CallReceived"/> the first time.
        /// </summary>
        public void MarkReceived(DateTime now)
        {
            if (_isInitiator)
                throw new InvalidOperationException("MarkReceived: outbound calls don't transition through Receiving");
            if (_state != CallSessionState.Receiving) return; // dedup
            _lastActivityAt = now;
            Stage(new CallReceived(_callId, _peerUserId, _isVideo, now));
        }

        /// <summary>
        /// <c>phoneCall</c> arrived: shared key fingerprint and
        /// connections in hand. Move to <see cref="CallSessionState.Active"/>.
        /// Stages <see cref="CallActive"/>. Idempotent on repeat
        /// transitions (server can re-deliver the constructor).
        /// </summary>
        public void MarkActive(KeyFingerprint fingerprint, IList<CallEndpoint> endpoints, DateTime now)
        {
            if (_state == CallSessionState.Active) return; // idempotent
            if (_state != CallSessionState.Pending &&
                _state != CallSessionState.Ringing &&
                _state != CallSessionState.Waiting &&
                _state != CallSessionState.Confirming &&
                _state != CallSessionState.MediaConnecting)
                throw new InvalidOperationException("MarkActive: expected state Pending|Ringing|Waiting|Confirming|MediaConnecting; was " + _state);
            CallSessionState previous = _state;
            _fingerprint = fingerprint;
            _endpoints = new List<CallEndpoint>(endpoints == null ? new CallEndpoint[0] : endpoints);
            _state = CallSessionState.Active;
            _lastActivityAt = now;
            _activeAt = now;
            Stage(new CallActive(_callId, fingerprint, now));
            Stage(new CallStateChanged(_callId, previous, _state, now));
        }

        /// <summary>
        /// Terminal transition. Stages <see cref="CallDiscarded"/>. Idempotent —
        /// repeat calls after the first are no-ops so peer-discarded +
        /// local-discarded races are safe (Telegram delivers both via
        /// <c>updatePhoneCall</c> + RPC ack, in either order).
        /// </summary>
        public void Discard(DiscardReason reason, DateTime now)
        {
            if (_state == CallSessionState.Discarded) return;
            CallSessionState previous = _state;
            // Compute talk-time when ending an Active call; for calls
            // discarded before activation, duration stays zero.
            if (_activeAt != DateTime.MinValue)
            {
                _duration = CallDuration.FromInterval(_activeAt, now);
            }
            _discardReason = reason;
            _state = CallSessionState.Discarded;
            _lastActivityAt = now;
            Stage(new CallDiscarded(_callId, reason, _duration, now));
            Stage(new CallStateChanged(_callId, previous, _state, now));
        }

        // ---- event staging -------------------------------------------------

        /// <summary>
        /// Drain pending domain events for the handler to publish post-
        /// persistence. Transactional with the state change.
        /// </summary>
        public IList<IDomainEvent> DequeuePendingEvents()
        {
            if (_pending.Count == 0) return new IDomainEvent[0];
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        private void Stage(IDomainEvent evt)
        {
            if (evt != null) _pending.Add(evt);
        }
    }
}
