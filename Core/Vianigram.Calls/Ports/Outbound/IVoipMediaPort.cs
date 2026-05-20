// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Ports.Outbound
{
    /// <summary>
    /// Bridge from the managed signaling context to the native VoIP media
    /// plane (<c>VianiumVoIP</c> - RTP, SRTP, Opus, jitter buffer,
    /// echo cancellation). Every method is async because the native runtime
    /// hops onto its own high-priority audio thread to start/stop streams.
    ///
    /// <para><b>Key isolation contract:</b> <see cref="StartAsync"/> receives
    /// only an opaque <see cref="CallKeyHandle"/>. Raw DH shared-key bytes
    /// remain in the native crypto/VoIP runtime and never pass through this
    /// managed signaling context.</para>
    ///
    /// <para>Production hosts bind this port through Composition's
    /// anti-corruption adapter over the <c>VianiumVoIP</c> WinMD.
    /// <see cref="StubVoipMediaPort"/> remains only as a fallback for
    /// tests or hosts that intentionally ship without calls.</para>
    /// </summary>
    public interface IVoipMediaPort
    {
        /// <summary>
        /// Start the media plane with the complete Telegram call context.
        /// This is the production entry point: it carries direction,
        /// protocol, key handle, endpoints and cached phone.getCallConfig
        /// data so native engines can select WebRTC/tgcalls or classic
        /// reflector paths without reaching back into MTProto.
        /// </summary>
        Task<Result<Unit, CallError>> StartAsync(CallStartContext context, CancellationToken ct);

        /// <summary>
        /// Start the media plane. <paramref name="keyHandle"/> points to
        /// native per-call key material; <paramref name="endpoints"/> is the
        /// reachable connection list from <c>phoneCall</c>.
        /// </summary>
        Task<Result<Unit, CallError>> StartAsync(CallId id, CallKeyHandle keyHandle, IList<CallEndpoint> endpoints, CancellationToken ct);

        /// <summary>
        /// Deliver server-side <c>updatePhoneCallSignalingData</c> bytes into
        /// the native signaling engine for this call.
        /// </summary>
        Task<Result<Unit, CallError>> ReceiveSignalingDataAsync(CallId id, byte[] data, CancellationToken ct);

        /// <summary>Stop the media plane and release the microphone /
        /// speaker. Idempotent. Returns Ok if nothing was running.</summary>
        Task<Result<Unit, CallError>> StopAsync(CancellationToken ct);

        /// <summary>Mute / unmute the local microphone. Returns
        /// <see cref="CallErrorKind.MediaPlaneFailed"/> if no call is
        /// active.</summary>
        Task<Result<Unit, CallError>> MuteAsync(bool mute, CancellationToken ct);

        /// <summary>
        /// Mute / unmute the local microphone for the call identified by
        /// <paramref name="id"/>. The <see cref="CallId"/> overload is the
        /// inbound-API path (the legacy <see cref="MuteAsync(bool, CancellationToken)"/>
        /// keeps the global form for the smoke harness and the
        /// <c>UpdateCallStateHandler</c> teardown path). Returns
        /// <see cref="CallErrorKind.MediaPlaneFailed"/> when no plane is
        /// active for that id.
        /// </summary>
        Task<Result<Unit, CallError>> SetMutedAsync(CallId id, bool muted, CancellationToken ct);

        /// <summary>
        /// Route the call audio to the speakerphone (<c>true</c>) or back
        /// to the earpiece / Bluetooth default (<c>false</c>). Idempotent.
        /// Returns <see cref="CallErrorKind.MediaPlaneFailed"/> when no
        /// plane is active.
        /// </summary>
        Task<Result<Unit, CallError>> SetSpeakerAsync(CallId id, bool on, CancellationToken ct);

        /// <summary>
        /// Switch between front-facing and rear cameras for an active
        /// video call. Idempotent on a still-image / audio-only call:
        /// returns <see cref="CallErrorKind.MediaPlaneFailed"/> when no
        /// camera capture is running.
        /// </summary>
        Task<Result<Unit, CallError>> FlipCameraAsync(CallId id, CancellationToken ct);

        /// <summary>Sample the current audio statistics. Returns a default-
        /// initialized struct if no call is active.</summary>
        Task<CallStats> GetStatsAsync(CancellationToken ct);

        /// <summary>Raised when the native VoIP plane changes state — codec
        /// negotiated, media plane connected, link quality dropped, audio
        /// underrun, etc. The application layer translates to a
        /// <see cref="Domain.Events.CallStatsUpdated"/>.</summary>
        event EventHandler<CallMediaEventArgs> MediaEvent;

        /// <summary>
        /// Raised when the native engine needs the managed MTProto layer to
        /// send <c>phone.sendSignalingData</c>.
        /// </summary>
        event EventHandler<CallSignalingDataProducedEventArgs> SignalingDataProduced;

        /// <summary>
        /// Raised for coarse native media state transitions that should be
        /// reflected in the call UI before/after Active.
        /// </summary>
        event EventHandler<CallMediaStateChangedEventArgs> MediaStateChanged;
    }

    /// <summary>
    /// Complete native media start descriptor. Kept in the outbound port
    /// layer because it is an adapter contract, not domain state.
    /// </summary>
    public sealed class CallStartContext
    {
        public CallStartContext(
            CallId id,
            long accessHash,
            bool initiator,
            bool video,
            CallProtocol protocol,
            long keyFingerprint,
            CallKeyHandle keyHandle,
            IList<CallEndpoint> endpoints,
            string callConfigJson)
        {
            Id = id;
            AccessHash = accessHash;
            IsInitiator = initiator;
            IsVideo = video;
            Protocol = protocol;
            KeyFingerprint = keyFingerprint;
            KeyHandle = keyHandle ?? CallKeyHandle.Empty;
            Endpoints = endpoints ?? new CallEndpoint[0];
            CallConfigJson = callConfigJson ?? string.Empty;
        }

        public CallId Id { get; private set; }
        public long AccessHash { get; private set; }
        public bool IsInitiator { get; private set; }
        public bool IsVideo { get; private set; }
        public CallProtocol Protocol { get; private set; }
        public long KeyFingerprint { get; private set; }
        public CallKeyHandle KeyHandle { get; private set; }
        public IList<CallEndpoint> Endpoints { get; private set; }
        public string CallConfigJson { get; private set; }
    }

    /// <summary>
    /// Optional capability surface for adapters that can explicitly say
    /// whether a real local media plane is available. Production adapters
    /// should return true only after the native VoIP runtime and audio
    /// capture/playback dependencies are wired.
    /// </summary>
    public interface IVoipMediaCapabilityPort
    {
        bool CanStartCalls { get; }
        string UnavailableReason { get; }
    }

    /// <summary>
    /// Optional UI bridge for media adapters that expose a platform playback
    /// source. The return type is object so the domain-facing Calls API does
    /// not force every host to reference Windows.Media.Core.
    /// </summary>
    public interface IVoipPlaybackSourcePort
    {
        object CreatePlaybackSource(CallId id);
    }

    /// <summary>
    /// Carrier for media-plane lifecycle events. Discriminated by
    /// <see cref="Kind"/>; <see cref="Stats"/> is populated on
    /// <see cref="MediaEventKind.StatsSample"/>.
    /// </summary>
    public sealed class CallMediaEventArgs : EventArgs
    {
        public MediaEventKind Kind { get; private set; }
        public CallStats Stats { get; private set; }
        public string Detail { get; private set; }
        public DateTime At { get; private set; }

        public CallMediaEventArgs(MediaEventKind kind, CallStats stats, string detail, DateTime at)
        {
            Kind = kind;
            Stats = stats;
            Detail = detail ?? string.Empty;
            At = at;
        }
    }

    public sealed class CallSignalingDataProducedEventArgs : EventArgs
    {
        public CallSignalingDataProducedEventArgs(CallId id, byte[] data, DateTime at)
        {
            Id = id;
            Data = data ?? new byte[0];
            At = at;
        }

        public CallId Id { get; private set; }
        public byte[] Data { get; private set; }
        public DateTime At { get; private set; }
    }

    public sealed class CallMediaStateChangedEventArgs : EventArgs
    {
        public CallMediaStateChangedEventArgs(CallId id, CallMediaStateKind state, string detail, DateTime at)
        {
            Id = id;
            State = state;
            Detail = detail ?? string.Empty;
            At = at;
        }

        public CallId Id { get; private set; }
        public CallMediaStateKind State { get; private set; }
        public string Detail { get; private set; }
        public DateTime At { get; private set; }
    }

    /// <summary>Distinguishes the native VoIP plane's lifecycle events.</summary>
    public enum MediaEventKind
    {
        /// <summary>Media plane finished negotiating, audio is flowing.</summary>
        Connected = 0,
        /// <summary>Codec changed mid-call (Opus bandwidth/bitrate adapt).</summary>
        CodecChanged = 1,
        /// <summary>Periodic statistics sample (audio levels, packet loss).</summary>
        StatsSample = 2,
        /// <summary>Quality dropped below an actionable threshold.</summary>
        QualityDegraded = 3,
        /// <summary>Connection lost — not yet terminal; the runtime is retrying.</summary>
        ConnectionLost = 4,
        /// <summary>Non-recoverable failure — the application should issue a discard.</summary>
        Failed = 5
    }

    public enum CallMediaStateKind
    {
        Idle = 0,
        Securing = 1,
        Connecting = 2,
        Connected = 3,
        Reconnecting = 4,
        Failed = 5,
        Stopped = 6
    }
}
