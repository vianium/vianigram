// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="INotificationsApi.MuteRuleChanged"/>
    /// whenever a <see cref="MuteRule"/> changes (locally or via server sync).
    /// Mirrors the <c>MuteRuleChanged</c> domain event in a CLR-event shape so
    /// XAML / UI layers that don't take an <c>IEventBus</c> dependency can
    /// still subscribe.
    /// </summary>
    public sealed class MuteRuleChangedEventArgs : EventArgs
    {
        public string PeerKey { get; private set; }
        public MuteRule NewRule { get; private set; }
        public DateTime At { get; private set; }

        public MuteRuleChangedEventArgs(string peerKey, MuteRule newRule, DateTime at)
        {
            PeerKey = peerKey ?? string.Empty;
            NewRule = newRule;
            At = at;
        }
    }
}
