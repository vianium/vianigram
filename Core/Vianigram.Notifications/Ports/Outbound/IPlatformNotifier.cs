// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Ports.Outbound
{
    /// <summary>
    /// Outbound port wrapping the WP8.1 platform notification surface
    /// (toasts, tile, badge). The stub
    /// <see cref="Vianigram.Notifications.Infrastructure.StubPlatformNotifier"/>
    /// only logs; the host application layer wires the real implementation
    /// against <c>Windows.UI.Notifications.ToastNotificationManager</c>,
    /// <c>TileUpdateManager</c>, and <c>BadgeUpdateManager</c>.
    ///
    /// Contract:
    ///   - All operations return <c>Result&lt;Unit, NotificationsError&gt;</c>
    ///     and never throw across the port.
    ///   - <c>deepLink</c> is the launch URI parsed by <c>App.OnLaunched</c>
    ///     to navigate to the originating chat (e.g.
    ///     <c>vianigram://chat/{peerKey}/{messageId}</c>).
    /// </summary>
    public interface IPlatformNotifier
    {
        /// <summary>Show a toast (title + body) with the supplied launch deep link.</summary>
        Task<Result<Unit, NotificationsError>> ShowToastAsync(string title, string body, string deepLink, CancellationToken ct);

        /// <summary>Refresh the live tile with the supplied summary text and unread count.</summary>
        Task<Result<Unit, NotificationsError>> UpdateTileAsync(string text, int unreadCount, CancellationToken ct);

        /// <summary>Set the platform badge to the supplied count (clamp [0, 99]).</summary>
        Task<Result<Unit, NotificationsError>> UpdateBadgeAsync(int count, CancellationToken ct);

        /// <summary>Clear any pending toasts associated with the supplied deep link / peer.</summary>
        Task<Result<Unit, NotificationsError>> ClearAsync(string deepLink, CancellationToken ct);
    }
}
