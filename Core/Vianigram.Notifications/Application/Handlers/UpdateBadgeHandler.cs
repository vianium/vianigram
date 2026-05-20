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
    /// Pushes the requested <see cref="BadgeCount"/> to the
    /// <see cref="IPlatformNotifier"/> and updates the aggregate's stored
    /// count. A platform sink failure is surfaced as the result; the local
    /// aggregate is still updated so the next sink attempt re-emits the same
    /// value.
    /// </summary>
    internal sealed class UpdateBadgeHandler
    {
        private readonly INotificationProfileRepository _repo;
        private readonly IPlatformNotifier _notifier;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public UpdateBadgeHandler(
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
            _log = new TimestampedLogger(log, "Notifications.UpdateBadge");
            _clock = clock;
        }

        public async Task<Result<Unit, NotificationsError>> HandleAsync(UpdateBadgeCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, NotificationsError>.Fail(NotificationsError.Unknown("null command"));

            NotificationProfile profile = await _repo.LoadAsync(ct).ConfigureAwait(false);
            DateTime now = _clock.UtcNow;

            profile.SetBadge(cmd.Count, now);
            var sinkResult = await _notifier.UpdateBadgeAsync(cmd.Count.Count, ct).ConfigureAwait(false);
            await _repo.SaveAsync(profile, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(profile, _bus);

            if (sinkResult.IsFail)
            {
                _log.Warn("platform badge update failed: " + sinkResult.Error);
                return Result<Unit, NotificationsError>.Fail(sinkResult.Error);
            }
            return Result<Unit, NotificationsError>.Ok(Unit.Value);
        }
    }
}
