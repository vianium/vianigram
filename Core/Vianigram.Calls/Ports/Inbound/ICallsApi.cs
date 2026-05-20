// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Calls bounded context. Every method is async,
    /// takes a <see cref="CancellationToken"/>, and returns
    /// <c>Result&lt;T, CallError&gt;</c>; no exceptions cross this boundary.
    ///
    /// <para>Consumers: presentation/ViewModels, the smoke harness, and the
    /// composition root. The view-model layer subscribes to
    /// <see cref="StateChanged"/> for lifecycle UI updates and to
    /// <see cref="IncomingCall"/> to surface the ringer screen.</para>
    /// </summary>
    public interface ICallsApi
    {
        /// <summary>
        /// True only when the app can complete the Telegram call handshake
        /// and start a real local media plane. UI should disable call entry
        /// points when false.
        /// </summary>
        bool IsCallingAvailable { get; }

        /// <summary>Human-readable diagnostic for disabled calls.</summary>
        string CallingUnavailableReason { get; }

        /// <summary>Initiate a new outgoing call (<c>phone.requestCall</c>).</summary>
        Task<Result<CallSession, CallError>> RequestCallAsync(long participantUserId, bool video, CancellationToken ct);

        /// <summary>
        /// Accept an incoming <c>phoneCallRequested</c> using
        /// <c>phone.acceptCall</c>.
        /// </summary>
        Task<Result<CallSession, CallError>> AcceptCallAsync(CallId id, CancellationToken ct);

        /// <summary>
        /// Discard a session using <c>phone.discardCall</c>. Used for
        /// hangup and reject flows; idempotent.
        /// </summary>
        Task<Result<Unit, CallError>> DiscardAsync(CallId id, DiscardReason reason, CancellationToken ct);

        /// <summary>
        /// Mute or unmute the local microphone for an active call. Routed
        /// to the local <c>IVoipMediaPort</c>; does not hit MTProto.
        /// </summary>
        Task<Result<Unit, CallError>> SetMutedAsync(CallId id, bool muted, CancellationToken ct);

        /// <summary>
        /// Toggle speakerphone routing for an active call. Routed to the
        /// local <c>IVoipMediaPort</c>; does not hit MTProto.
        /// </summary>
        Task<Result<Unit, CallError>> SetSpeakerAsync(CallId id, bool on, CancellationToken ct);

        /// <summary>
        /// Flip between front and rear cameras for an active video call.
        /// Routed to the local <c>IVoipMediaPort</c>; does not hit MTProto.
        /// </summary>
        Task<Result<Unit, CallError>> FlipCameraAsync(CallId id, CancellationToken ct);

        /// <summary>
        /// Synchronously fetch the persisted session, or null if none is
        /// known. Returns the live aggregate; callers must not mutate it.
        /// </summary>
        CallSession GetSession(CallId id);

        /// <summary>
        /// Returns a host-specific playback source for remote call audio
        /// when media is active. UWP/WP hosts cast this to
        /// Windows.Media.Core.MediaStreamSource and attach it to a
        /// MediaElement.
        /// </summary>
        object CreatePlaybackSource(CallId id);

        /// <summary>
        /// Returns the recent in-process call sessions. This is the
        /// app-facing read model for the calls list until a durable service
        /// message projection is available.
        /// </summary>
        Task<Result<IList<CallSession>, CallError>> ListRecentAsync(CancellationToken ct);

        /// <summary>
        /// Raised on every session lifecycle change (Requested, Accepted,
        /// Active, Discarded, FingerprintMismatch). Multicast; thread-safe
        /// add/remove.
        /// </summary>
        event EventHandler<CallStateChangedEventArgs> StateChanged;

        /// <summary>
        /// Raised when Sync delivers a <c>phoneCallRequested</c> from a peer.
        /// The UI should ring and surface the incoming call screen.
        /// </summary>
        event EventHandler<CallReceivedEventArgs> IncomingCall;
    }
}
