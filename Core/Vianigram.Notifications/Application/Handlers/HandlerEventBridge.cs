// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Notifications.Domain.Entities;
using Vianigram.Notifications.Domain.Events;

namespace Vianigram.Notifications.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="NotificationProfile"/>
    /// and publishes each on the <see cref="IEventBus"/>. Centralized here so
    /// all handlers route events identically.
    ///
    /// The bus dispatches by static type (one <c>Subscribe&lt;T&gt;</c> table
    /// per concrete event), so we must call <c>Publish</c> with the runtime
    /// type — a generic helper would only reach the <see cref="IDomainEvent"/>
    /// subscriber list.
    /// </summary>
    internal static class HandlerEventBridge
    {
        public static void Drain(NotificationProfile profile, IEventBus bus)
        {
            if (profile == null || bus == null) return;
            IList<IDomainEvent> events = profile.DequeuePendingEvents();
            for (int i = 0; i < events.Count; i++)
            {
                Dispatch(events[i], bus);
            }
        }

        private static void Dispatch(IDomainEvent evt, IEventBus bus)
        {
            var queued = evt as IncomingNotificationQueued; if (queued != null) { bus.Publish(queued); return; }
            var delivered = evt as NotificationDelivered; if (delivered != null) { bus.Publish(delivered); return; }
            var ruleChanged = evt as MuteRuleChanged; if (ruleChanged != null) { bus.Publish(ruleChanged); return; }
            var badge = evt as BadgeUpdated; if (badge != null) { bus.Publish(badge); return; }
            // Unknown domain event; intentionally drop. The bus dispatches by
            // static type only, so a generic fallback would not reach typed
            // subscribers.
        }
    }
}
