// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Search.Domain.Entities;
using Vianigram.Search.Domain.Events;

namespace Vianigram.Search.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="SearchSession"/>
    /// aggregate and publishes each on the <see cref="IEventBus"/>. Centralized
    /// here so all handlers route events identically.
    ///
    /// The bus dispatches by static type (one <c>Subscribe&lt;T&gt;</c> table
    /// per concrete event), so we must call <c>Publish</c> with the runtime
    /// type — a generic helper would only reach the <see cref="IDomainEvent"/>
    /// subscriber list.
    /// </summary>
    internal static class HandlerEventBridge
    {
        public static void Drain(SearchSession session, IEventBus bus)
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
            var started = evt as SearchStarted; if (started != null) { bus.Publish(started); return; }
            var arrived = evt as ResultsArrived; if (arrived != null) { bus.Publish(arrived); return; }
            var completed = evt as SearchCompleted; if (completed != null) { bus.Publish(completed); return; }
            var cancelled = evt as SearchCancelled; if (cancelled != null) { bus.Publish(cancelled); return; }
            var failed = evt as SearchFailed; if (failed != null) { bus.Publish(failed); return; }
            // Unknown domain event; intentionally drop. The bus dispatches by
            // static type only, so a generic fallback would not reach typed
            // subscribers.
        }
    }
}
