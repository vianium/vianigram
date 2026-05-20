// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Infrastructure
{
    /// <summary>
    /// Fallback <see cref="IVoipMediaPort"/> for hosts that deliberately
    /// compile without the native VoIP runtime. It logs a warning on
    /// construction and reports calls as unavailable before any Telegram
    /// call signaling is sent.
    ///
    /// <para><b>StartAsync</b> returns a
    /// <see cref="CallErrorKind.MediaPlaneFailed"/> error. The application
    /// layer also reads <see cref="IVoipMediaCapabilityPort"/> and blocks
    /// user-initiated calls before a Telegram request is sent.</para>
    ///
    /// <para><b>StopAsync</b> always returns Ok — stop on a stopped plane
    /// is a no-op everywhere.</para>
    ///
    /// <para><b>MuteAsync</b> returns
    /// <see cref="CallErrorKind.MediaPlaneFailed"/> because there's no
    /// active plane to mute.</para>
    ///
    /// <para><b>GetStatsAsync</b> returns a zero-valued sample because no
    /// native media session exists.</para>
    ///
    /// <para>The <see cref="MediaEvent"/> event is declared but never
    /// raised by the stub; the smoke harness can drive it manually via
    /// reflection if needed for lifecycle tests.</para>
    /// </summary>
    public sealed class StubVoipMediaPort : IVoipMediaPort, IVoipMediaCapabilityPort
    {
        public const string Reason =
            "native VoIP media plane is unavailable in this fallback build; bind VianiumVoIP for the production adapter";

        private readonly IComponentLogger _log;

        public event EventHandler<CallMediaEventArgs> MediaEvent;
        public event EventHandler<CallSignalingDataProducedEventArgs> SignalingDataProduced;
        public event EventHandler<CallMediaStateChangedEventArgs> MediaStateChanged;

        public bool CanStartCalls { get { return false; } }
        public string UnavailableReason { get { return Reason; } }

        public StubVoipMediaPort(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            _log = new TimestampedLogger(logger, "Calls.StubVoipMediaPort");
            _log.Warn("StubVoipMediaPort active - real calls are disabled: " + Reason + ".");
        }

        public Task<Result<Unit, CallError>> StartAsync(CallId id, CallKeyHandle keyHandle, IList<CallEndpoint> endpoints, CancellationToken ct)
        {
            return StartAsync(new CallStartContext(
                id,
                0L,
                false,
                false,
                CallProtocol.Default,
                0L,
                keyHandle,
                endpoints,
                string.Empty), ct);
        }

        public Task<Result<Unit, CallError>> StartAsync(CallStartContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context == null)
                return Task.FromResult(Result<Unit, CallError>.Fail(CallError.MediaPlaneFailed("missing call start context")));
            IList<CallEndpoint> endpoints = context.Endpoints;
            int endpointCount = endpoints == null ? 0 : endpoints.Count;
            string handle = context.KeyHandle == null ? "(null)" : context.KeyHandle.ToString();
            _log.Info("StubVoipMediaPort.StartAsync called - callId=" + context.Id + " keyHandle=" + handle + " endpoints=" + endpointCount
                + ". Returning MediaPlaneFailed because this host has no native runtime.");
            RaiseMediaState(context.Id, CallMediaStateKind.Failed, Reason);
            return Task.FromResult(Result<Unit, CallError>.Fail(
                CallError.MediaPlaneFailed(Reason)));
        }

        public Task<Result<Unit, CallError>> ReceiveSignalingDataAsync(CallId id, byte[] data, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Debug("StubVoipMediaPort.ReceiveSignalingDataAsync(" + id + ", "
                + (data == null ? 0 : data.Length) + "B) - no media plane.");
            RaiseMediaState(id, CallMediaStateKind.Failed, "no active native signaling bridge (fallback adapter)");
            return Task.FromResult(Result<Unit, CallError>.Fail(
                CallError.MediaPlaneFailed("no active native signaling bridge (fallback adapter)")));
        }

        public Task<Result<Unit, CallError>> StopAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Debug("StubVoipMediaPort.StopAsync called - no-op.");
            return Task.FromResult(Result<Unit, CallError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, CallError>> MuteAsync(bool mute, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Debug("StubVoipMediaPort.MuteAsync(" + mute + ") called - no media plane.");
            return Task.FromResult(Result<Unit, CallError>.Fail(
                CallError.MediaPlaneFailed("no active media plane (stub adapter)")));
        }

        public Task<Result<Unit, CallError>> SetMutedAsync(CallId id, bool muted, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Debug("StubVoipMediaPort.SetMutedAsync(" + id + ", muted=" + muted + ") - no media plane.");
            return Task.FromResult(Result<Unit, CallError>.Fail(
                CallError.MediaPlaneFailed("no active media plane (fallback adapter)")));
        }

        public Task<Result<Unit, CallError>> SetSpeakerAsync(CallId id, bool on, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Debug("StubVoipMediaPort.SetSpeakerAsync(" + id + ", on=" + on + ") - no media plane.");
            return Task.FromResult(Result<Unit, CallError>.Fail(
                CallError.MediaPlaneFailed("no active media plane (fallback adapter)")));
        }

        public Task<Result<Unit, CallError>> FlipCameraAsync(CallId id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Debug("StubVoipMediaPort.FlipCameraAsync(" + id + ") - no media plane.");
            return Task.FromResult(Result<Unit, CallError>.Fail(
                CallError.MediaPlaneFailed("video capture is unavailable in this host")));
        }

        public Task<CallStats> GetStatsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(CallStats.Empty);
        }

        private void RaiseMediaState(CallId id, CallMediaStateKind state, string detail)
        {
            EventHandler<CallMediaStateChangedEventArgs> h = MediaStateChanged;
            if (h == null) return;
            try
            {
                h(this, new CallMediaStateChangedEventArgs(id, state, detail, DateTime.UtcNow));
            }
            catch
            {
                // Stub event delivery is diagnostic only.
            }
        }

        /// <summary>
        /// Test helper for the smoke harness — fakes a <see cref="MediaEvent"/>
        /// emission. Not part of the production interface; the smoke
        /// harness reaches for this via the concrete type.
        /// </summary>
        public void RaiseMediaEventForTest(MediaEventKind kind, CallStats stats, string detail)
        {
            EventHandler<CallMediaEventArgs> h = MediaEvent;
            if (h == null) return;
            try
            {
                h(this, new CallMediaEventArgs(kind, stats, detail, DateTime.UtcNow));
            }
            catch
            {
                // Swallow downstream subscriber faults — stub is best-effort.
            }
        }

        public void RaiseSignalingDataProducedForTest(CallId id, byte[] data)
        {
            EventHandler<CallSignalingDataProducedEventArgs> h = SignalingDataProduced;
            if (h == null) return;
            try
            {
                h(this, new CallSignalingDataProducedEventArgs(id, data, DateTime.UtcNow));
            }
            catch
            {
                // Stub event delivery is diagnostic only.
            }
        }
    }
}
