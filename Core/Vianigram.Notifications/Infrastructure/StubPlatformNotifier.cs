// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.ValueObjects;
using Vianigram.Notifications.Ports.Outbound;

namespace Vianigram.Notifications.Infrastructure
{
    /// <summary>
    /// Placeholder for <see cref="IPlatformNotifier"/>: every method
    /// logs the intent (e.g. "[Notifications] would show toast: &lt;title&gt;")
    /// and succeeds. The real implementation lives in the App layer where
    /// the host wires <c>Windows.UI.Notifications.ToastNotificationManager</c>,
    /// <c>TileUpdateManager</c>, and <c>BadgeUpdateManager</c>.
    ///
    /// Keeping the stub here lets the bounded context build and run end-to-end
    /// (composition root, smoke tests) without a WinRT dependency in
    /// <c>Vianigram.Notifications</c> itself.
    /// </summary>
    public sealed class StubPlatformNotifier : IPlatformNotifier
    {
        private readonly IComponentLogger _log;

        public StubPlatformNotifier(ILogger log)
        {
            if (log == null) throw new ArgumentNullException("log");
            _log = new TimestampedLogger(log, "Notifications.StubPlatformNotifier");
        }

        public Task<Result<Unit, NotificationsError>> ShowToastAsync(string title, string body, string deepLink, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Info("would show toast: " + (title ?? string.Empty));
            return Task.FromResult(Result<Unit, NotificationsError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, NotificationsError>> UpdateTileAsync(string text, int unreadCount, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Info("would update tile: " + (text ?? string.Empty) + " (" + unreadCount + ")");
            return Task.FromResult(Result<Unit, NotificationsError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, NotificationsError>> UpdateBadgeAsync(int count, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Info("would update badge: " + count);
            return Task.FromResult(Result<Unit, NotificationsError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, NotificationsError>> ClearAsync(string deepLink, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            _log.Info("would clear: " + (deepLink ?? string.Empty));
            return Task.FromResult(Result<Unit, NotificationsError>.Ok(Unit.Value));
        }
    }
}
