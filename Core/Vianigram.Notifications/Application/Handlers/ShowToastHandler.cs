// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Notifications.Application.UseCases;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.Entities;
using Vianigram.Notifications.Domain.ValueObjects;
using Vianigram.Notifications.Ports.Outbound;

namespace Vianigram.Notifications.Application.Handlers
{
    /// <summary>
    /// Resolves the effective <see cref="MuteRule"/> for the supplied peer,
    /// stages an <c>IncomingNotificationQueued</c> event, and (when not
    /// muted) hands the toast to <see cref="IPlatformNotifier"/>. The toast
    /// body is replaced with a generic "New message" string when the rule
    /// disables previews.
    ///
    /// The handler always stages the queued event regardless of suppression
    /// outcome — telemetry / observers see every inbound. Delivery is staged
    /// only when the platform sink succeeds.
    /// </summary>
    internal sealed class ShowToastHandler
    {
        private readonly INotificationProfileRepository _repo;
        private readonly IPlatformNotifier _notifier;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ShowToastHandler(
            INotificationProfileRepository repo,
            IPlatformNotifier notifier,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (notifier == null) throw new ArgumentNullException("notifier");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _notifier = notifier;
            _bus = bus;
            _log = new TimestampedLogger(log, "Notifications.ShowToast");
            _clock = clock;
        }

        public async Task<Result<Unit, NotificationsError>> HandleAsync(ShowToastCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("null command"));
            if (string.IsNullOrEmpty(cmd.PeerKey))
                return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("peerKey required"));

            NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);
            DateTime now = _clock.UtcNow;

            // Always stage the queued event so subscribers can count inbounds.
            profile.RecordQueued(cmd.Kind, cmd.PeerKey, cmd.Body, now);

            MuteRule rule = profile.Resolve(cmd.PeerKey);
            if (rule.IsMutedAt(now))
            {
                _log.Info("suppressed (muted): " + cmd.PeerKey);
                await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
                HandlerEventBridge.Drain(profile, _bus);
                return Result<Unit, NotificationsError>.Ok(Unit.Value);
            }

            string body = rule.ShowPreviews ? cmd.Body : "New message";
            string deepLink = string.IsNullOrEmpty(cmd.DeepLink)
                ? "vianigram://chat/" + cmd.PeerKey
                : cmd.DeepLink;

            var toastResult = await _notifier.ShowToastAsync(cmd.Title, body, deepLink, ct).ConfigureAwait(false);
            if (toastResult.IsFail)
            {
                _log.Warn("platform toast failed: " + toastResult.Error);
                await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
                HandlerEventBridge.Drain(profile, _bus);
                return Result<Unit, NotificationsError>.Fail(toastResult.Error);
            }

            profile.RecordDelivered(cmd.Kind, cmd.PeerKey, now);
            await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(profile, _bus);
            return Result<Unit, NotificationsError>.Ok(Unit.Value);
        }
    }
}
