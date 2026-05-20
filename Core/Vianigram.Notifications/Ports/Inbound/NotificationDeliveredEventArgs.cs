// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="INotificationsApi.Delivered"/> after
    /// a queued notification reaches the platform sink. Mirrors the
    /// <c>NotificationDelivered</c> domain event in a CLR-event shape so XAML /
    /// UI layers that don't take an <c>IEventBus</c> dependency can still
    /// subscribe.
    /// </summary>
    public sealed class NotificationDeliveredEventArgs : EventArgs
    {
        public NotificationKind Kind { get; private set; }
        public string PeerKey { get; private set; }
        public DateTime At { get; private set; }

        public NotificationDeliveredEventArgs(NotificationKind kind, string peerKey, DateTime at)
        {
            Kind = kind;
            PeerKey = peerKey ?? string.Empty;
            At = at;
        }
    }
}
