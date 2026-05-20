// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Calls.Domain.Events
{
    /// <summary>
    /// Emitted when the local user has issued <c>phone.requestCall</c> and
    /// the server returned <c>phoneCallWaiting</c>. Session state at emission
    /// time is <see cref="CallSessionState.Waiting"/>.
    /// </summary>
    public sealed class CallRequested : IDomainEvent
    {
        public CallId CallId { get; private set; }
        public long PeerUserId { get; private set; }
        public bool Video { get; private set; }
        public DateTime At { get; private set; }

        public CallRequested(CallId callId, long peerUserId, bool video, DateTime at)
        {
            CallId = callId;
            PeerUserId = peerUserId;
            Video = video;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when Sync delivered an <c>updatePhoneCall</c> wrapping a
    /// <c>phoneCallRequested</c> from the peer. Session state at emission
    /// time is <see cref="CallSessionState.Receiving"/> — the ringer should
    /// fire and incoming-call UI should surface.
    /// </summary>
    public sealed class CallReceived : IDomainEvent
    {
        public CallId CallId { get; private set; }
        public long FromUserId { get; private set; }
        public bool Video { get; private set; }
        public DateTime At { get; private set; }

        public CallReceived(CallId callId, long fromUserId, bool video, DateTime at)
        {
            CallId = callId;
            FromUserId = fromUserId;
            Video = video;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the local user has accepted an incoming
    /// <c>phoneCallRequested</c> by issuing <c>phone.acceptCall</c>. Session
    /// state at emission time is <see cref="CallSessionState.Pending"/>;
    /// transitions to <see cref="CallSessionState.Active"/> arrive on
    /// <see cref="CallActive"/> when the peer's <c>phoneCall</c> lands.
    /// </summary>
    public sealed class CallAccepted : IDomainEvent
    {
        public CallId CallId { get; private set; }
        public DateTime At { get; private set; }

        public CallAccepted(CallId callId, DateTime at)
        {
            CallId = callId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the call has reached <see cref="CallSessionState.Active"/>:
    /// both peers have the shared key and the connection endpoints. The
    /// VoIP media plane is starting up. Carries the key fingerprint for
    /// out-of-band verification UX.
    /// </summary>
    public sealed class CallActive : IDomainEvent
    {
        public CallId CallId { get; private set; }
        public KeyFingerprint Fingerprint { get; private set; }
        public DateTime At { get; private set; }

        public CallActive(CallId callId, KeyFingerprint fingerprint, DateTime at)
        {
            CallId = callId;
            Fingerprint = fingerprint;
            At = at;
        }
    }

    /// <summary>
    /// Emitted on terminal call shutdown — either side issued discard, or a
    /// security check (fingerprint mismatch, protocol error) aborted the
    /// call. Carries the wall-clock duration so call-history UI can display
    /// the talk-time without re-querying.
    /// </summary>
    public sealed class CallDiscarded : IDomainEvent
    {
        public CallId CallId { get; private set; }
        public DiscardReason Reason { get; private set; }
        public CallDuration Duration { get; private set; }
        public DateTime At { get; private set; }

        public CallDiscarded(CallId callId, DiscardReason reason, CallDuration duration, DateTime at)
        {
            CallId = callId;
            Reason = reason;
            Duration = duration;
            At = at;
        }
    }

    /// <summary>
    /// Coarse-grained "session changed" pulse, raised on every state
    /// transition the aggregate makes. Subscribers that don't need the
    /// fine-grained <see cref="CallRequested"/>/<see cref="CallAccepted"/>/...
    /// flow can listen here for a single source of truth.
    /// </summary>
    public sealed class CallStateChanged : IDomainEvent
    {
        public CallId CallId { get; private set; }
        public CallSessionState Previous { get; private set; }
        public CallSessionState Current { get; private set; }
        public DateTime At { get; private set; }

        public CallStateChanged(CallId callId, CallSessionState previous, CallSessionState current, DateTime at)
        {
            CallId = callId;
            Previous = previous;
            Current = current;
            At = at;
        }
    }

    /// <summary>
    /// Emitted at the cadence the native VoIP plane reports new audio
    /// telemetry (audio levels, packet loss, RTT, bitrate, underruns). UI
    /// renders signal-quality bars and audio-level meters from this stream.
    /// The source is <c>VianiumVoIP</c> in production hosts; smoke harnesses
    /// can still drive it manually through the outbound port.
    /// </summary>
    public sealed class CallStatsUpdated : IDomainEvent
    {
        public CallId CallId { get; private set; }
        public CallStats Stats { get; private set; }
        public DateTime At { get; private set; }

        public CallStatsUpdated(CallId callId, CallStats stats, DateTime at)
        {
            CallId = callId;
            Stats = stats;
            At = at;
        }
    }
}
