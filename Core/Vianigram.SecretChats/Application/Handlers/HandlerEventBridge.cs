// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.Events;
using Vianigram.Kernel.Events;

namespace Vianigram.SecretChats.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="SecretSession"/>
    /// and publishes each on the <see cref="IEventBus"/>. Centralized so all
    /// six handlers route events identically.
    ///
    /// The bus dispatches by static type (one <c>Subscribe&lt;T&gt;</c>
    /// table per concrete event), so we must call <c>Publish</c> with the
    /// runtime type — a generic helper would only reach the
    /// <see cref="IDomainEvent"/> subscriber list.
    /// </summary>
    internal static class HandlerEventBridge
    {
        public static void Drain(SecretSession session, IEventBus bus)
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
            var requested = evt as SecretChatRequested; if (requested != null) { bus.Publish(requested); return; }
            var accepted = evt as SecretChatAccepted; if (accepted != null) { bus.Publish(accepted); return; }
            var established = evt as SecretChatEstablished; if (established != null) { bus.Publish(established); return; }
            var discarded = evt as SecretChatDiscarded; if (discarded != null) { bus.Publish(discarded); return; }
            var received = evt as SecretMessageReceived; if (received != null) { bus.Publish(received); return; }
            var sent = evt as SecretMessageSent; if (sent != null) { bus.Publish(sent); return; }
            var mismatch = evt as KeyFingerprintMismatch; if (mismatch != null) { bus.Publish(mismatch); return; }
            var rekeyed = evt as KeyRekeyed; if (rekeyed != null) { bus.Publish(rekeyed); return; }
            // Unknown domain event; intentionally drop — the bus dispatches
            // by static type only, so a generic fallback would not reach
            // typed subscribers.
        }
    }
}
