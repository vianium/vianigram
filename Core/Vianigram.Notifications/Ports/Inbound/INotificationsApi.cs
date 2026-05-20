// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Notifications bounded context (V1 shape).
    /// Every method is async, takes a <see cref="CancellationToken"/>, and
    /// returns <c>Result&lt;T, NotificationsError&gt;</c>; no exceptions cross
    /// this boundary.
    ///
    /// Consumers: presentation/ViewModels (settings page, chat header),
    /// Vianigram.Messaging (RaiseToast on new inbound), Vianigram.Agent
    /// (HandlePushPayload), composition root for wiring.
    /// </summary>
    public interface INotificationsApi
    {
        /// <summary>
        /// Set / replace the mute rule for a peer
        /// (<c>account.updateNotifySettings#84be5b93</c>). Use
        /// <see cref="MuteRule.Global"/> for the default rule.
        /// </summary>
        Task<Result<Unit, NotificationsError>> SetMuteAsync(string peerKey, MuteRule rule, CancellationToken ct);

        /// <summary>Resolve the effective mute rule for a peer (override or global).</summary>
        Task<Result<MuteRule, NotificationsError>> GetMuteAsync(string peerKey, CancellationToken ct);

        /// <summary>
        /// Sync per-scope notification settings from the server
        /// (<c>account.getNotifySettings#12b3ad31</c>) into the local profile.
        /// </summary>
        Task<Result<Unit, NotificationsError>> SyncSettingsAsync(CancellationToken ct);

        /// <summary>
        /// Show a toast for an incoming event. Honors the resolved mute rule
        /// and the platform notifier's preview policy. Stages an
        /// <c>IncomingNotificationQueued</c> domain event regardless of
        /// platform outcome (telemetry).
        /// </summary>
        Task<Result<Unit, NotificationsError>> ShowAsync(
            NotificationKind kind,
            string peerKey,
            string title,
            string body,
            CancellationToken ct);

        /// <summary>Update the global unread badge.</summary>
        Task<Result<Unit, NotificationsError>> UpdateBadgeAsync(BadgeCount count, CancellationToken ct);

        /// <summary>
        /// Clear any platform notifications associated with a peer (used when
        /// the user opens the chat). Does NOT modify the mute rule.
        /// </summary>
        Task<Result<Unit, NotificationsError>> ClearForPeerAsync(string peerKey, CancellationToken ct);

        /// <summary>
        /// CLR event raised whenever a notification is delivered to the
        /// platform. Multicast, thread-safe add/remove.
        /// </summary>
        event EventHandler<NotificationDeliveredEventArgs> Delivered;

        /// <summary>
        /// CLR event raised whenever a mute rule changes. Multicast,
        /// thread-safe add/remove.
        /// </summary>
        event EventHandler<MuteRuleChangedEventArgs> MuteRuleChanged;
    }
}
