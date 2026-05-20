// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Domain.Events;
using Vianigram.Kernel.Events;

namespace Vianigram.Contacts.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="ContactBook"/> and
    /// publishes each on the <see cref="IEventBus"/>. Centralized here so all
    /// six handlers route events identically.
    ///
    /// The bus dispatches by static type (one <c>Subscribe&lt;T&gt;</c> table
    /// per concrete event), so we must call <c>Publish</c> with the runtime
    /// type — a generic helper would only reach the <see cref="IDomainEvent"/>
    /// subscriber list.
    /// </summary>
    internal static class HandlerEventBridge
    {
        public static void Drain(ContactBook book, IEventBus bus)
        {
            if (book == null || bus == null) return;
            IList<IDomainEvent> events = book.DequeuePendingEvents();
            for (int i = 0; i < events.Count; i++)
            {
                Dispatch(events[i], bus);
            }
        }

        private static void Dispatch(IDomainEvent evt, IEventBus bus)
        {
            var imported = evt as ContactImported; if (imported != null) { bus.Publish(imported); return; }
            var updated = evt as ContactUpdated; if (updated != null) { bus.Publish(updated); return; }
            var removed = evt as ContactRemoved; if (removed != null) { bus.Publish(removed); return; }
            var blocked = evt as UserBlocked; if (blocked != null) { bus.Publish(blocked); return; }
            var unblocked = evt as UserUnblocked; if (unblocked != null) { bus.Publish(unblocked); return; }
            var synced = evt as ContactsSynced; if (synced != null) { bus.Publish(synced); return; }
            // Unknown domain event; intentionally drop. The bus dispatches by
            // static type only, so a generic fallback would not reach typed
            // subscribers.
        }
    }
}
