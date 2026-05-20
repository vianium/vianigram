// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Domain.Events
{
    /// <summary>
    /// Emitted when an inbound notification is queued for delivery (the
    /// application has decided to surface a toast). The actual platform
    /// dispatch happens via <c>IPlatformNotifier</c> — this event marks the
    /// decision point so subscribers (telemetry, tests) can observe it.
    /// </summary>
    public sealed class IncomingNotificationQueued : IDomainEvent
    {
        public NotificationKind Kind { get; private set; }
        public string PeerKey { get; private set; }
        public string Body { get; private set; }
        public DateTime At { get; private set; }

        public IncomingNotificationQueued(NotificationKind kind, string peerKey, string body, DateTime at)
        {
            Kind = kind;
            PeerKey = peerKey ?? string.Empty;
            Body = body ?? string.Empty;
            At = at;
        }
    }

    /// <summary>
    /// Emitted after a queued notification has been dispatched to the
    /// platform sink (the toast was shown / handed off to the OS).
    /// </summary>
    public sealed class NotificationDelivered : IDomainEvent
    {
        public NotificationKind Kind { get; private set; }
        public string PeerKey { get; private set; }
        public DateTime At { get; private set; }

        public NotificationDelivered(NotificationKind kind, string peerKey, DateTime at)
        {
            Kind = kind;
            PeerKey = peerKey ?? string.Empty;
            At = at;
        }
    }

    /// <summary>
    /// Emitted whenever a <see cref="MuteRule"/> changes (locally or via
    /// server sync). Carries the new rule; subscribers re-read the aggregate
    /// for additional context if needed.
    /// </summary>
    public sealed class MuteRuleChanged : IDomainEvent
    {
        public string PeerKey { get; private set; }
        public MuteRule NewRule { get; private set; }
        public DateTime At { get; private set; }

        public MuteRuleChanged(string peerKey, MuteRule newRule, DateTime at)
        {
            if (newRule == null) throw new ArgumentNullException("newRule");
            PeerKey = peerKey ?? string.Empty;
            NewRule = newRule;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the unread badge transitions to a new value. Subscribers
    /// (e.g. the platform tile/badge sink) translate this into the WP8.1
    /// BadgeUpdater call.
    /// </summary>
    public sealed class BadgeUpdated : IDomainEvent
    {
        public BadgeCount Count { get; private set; }
        public DateTime At { get; private set; }

        public BadgeUpdated(BadgeCount count, DateTime at)
        {
            Count = count;
            At = at;
        }
    }
}
