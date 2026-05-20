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
    /// Clears outstanding platform notifications for a peer (toast / tile
    /// entries) and updates the badge to the new total. Does NOT modify the
    /// mute rule and does NOT issue an MTProto call — read receipts are owned
    /// by <c>Vianigram.Messages</c>; this handler only mirrors the
    /// notification surface.
    /// </summary>
    internal sealed class MarkAsReadHandler
    {
        private readonly INotificationProfileRepository _repo;
        private readonly IPlatformNotifier _notifier;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public MarkAsReadHandler(
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
            _log = new TimestampedLogger(log, "Notifications.MarkAsRead");
            _clock = clock;
        }

        public async Task<Result<Unit, NotificationsError>> HandleAsync(MarkAsReadCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("null command"));
            if (string.IsNullOrEmpty(cmd.PeerKey))
                return Result<Unit, NotificationsError>.Fail(NotificationsError.NotInExpectedState("peerKey required"));

            NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);
            DateTime now = _clock.UtcNow;

            string deepLink = "vianigram://chat/" + cmd.PeerKey;
            var clearResult = await _notifier.ClearAsync(deepLink, ct).ConfigureAwait(false);
            if (clearResult.IsFail)
            {
                _log.Warn("platform clear failed: " + clearResult.Error);
                // Soft-fail: still update the badge locally so UI stays consistent.
            }

            profile.ClearForPeer(cmd.PeerKey, cmd.NewBadge, now);

            var badgeResult = await _notifier.UpdateBadgeAsync(cmd.NewBadge.Count, ct).ConfigureAwait(false);
            if (badgeResult.IsFail)
            {
                _log.Warn("platform badge update failed: " + badgeResult.Error);
            }

            await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(profile, _bus);
            return Result<Unit, NotificationsError>.Ok(Unit.Value);
        }
    }
}
