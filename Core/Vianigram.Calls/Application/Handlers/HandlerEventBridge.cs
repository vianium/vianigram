// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.Events;
using Vianigram.Kernel.Events;

namespace Vianigram.Calls.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="CallSession"/>
    /// and publishes each on the <see cref="IEventBus"/>. Centralized so
    /// every handler routes events identically.
    ///
    /// The bus dispatches by static type (one <c>Subscribe&lt;T&gt;</c>
    /// table per concrete event), so we must call <c>Publish</c> with the
    /// runtime type — a generic helper would only reach the
    /// <see cref="IDomainEvent"/> subscriber list.
    /// </summary>
    internal static class HandlerEventBridge
    {
        public static void Drain(CallSession session, IEventBus bus)
        {
            if (session == null || bus == null) return;
            IList<IDomainEvent> events = session.DequeuePendingEvents();
            for (int i = 0; i < events.Count; i++)
            {
                Dispatch(events[i], bus);
            }
        }

        private static void Dispatch(IDomainEvent evt, IEventBus bus)
        {
            var requested = evt as CallRequested; if (requested != null) { bus.Publish(requested); return; }
            var received = evt as CallReceived; if (received != null) { bus.Publish(received); return; }
            var accepted = evt as CallAccepted; if (accepted != null) { bus.Publish(accepted); return; }
            var active = evt as CallActive; if (active != null) { bus.Publish(active); return; }
            var discarded = evt as CallDiscarded; if (discarded != null) { bus.Publish(discarded); return; }
            var stateChanged = evt as CallStateChanged; if (stateChanged != null) { bus.Publish(stateChanged); return; }
            var stats = evt as CallStatsUpdated; if (stats != null) { bus.Publish(stats); return; }
            // Unknown domain event; intentionally drop — the bus dispatches
            // by static type only, so a generic fallback would not reach
            // typed subscribers.
        }
    }
}
