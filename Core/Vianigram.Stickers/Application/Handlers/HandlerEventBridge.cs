// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Domain.Events;

namespace Vianigram.Stickers.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="StickerLibrary"/> and
    /// publishes each on the <see cref="IEventBus"/>. Centralized here so all
    /// handlers route events identically.
    ///
    /// The bus dispatches by static type (one <c>Subscribe&lt;T&gt;</c> table
    /// per concrete event), so we must call <c>Publish</c> with the runtime
    /// type — a generic helper would only reach the <see cref="IDomainEvent"/>
    /// subscriber list.
    /// </summary>
    internal static class HandlerEventBridge
    {
        public static void Drain(StickerLibrary library, IEventBus bus)
        {
            if (library == null || bus == null) return;
            IList<IDomainEvent> events = library.DequeuePendingEvents();
            for (int i = 0; i < events.Count; i++)
            {
                Dispatch(events[i], bus);
            }
        }

        private static void Dispatch(IDomainEvent evt, IEventBus bus)
        {
            var installed = evt as StickerSetInstalled; if (installed != null) { bus.Publish(installed); return; }
            var uninstalled = evt as StickerSetUninstalled; if (uninstalled != null) { bus.Publish(uninstalled); return; }
            var reordered = evt as StickerSetReordered; if (reordered != null) { bus.Publish(reordered); return; }
            var used = evt as StickerUsedRecently; if (used != null) { bus.Publish(used); return; }
            var favorited = evt as StickerFavorited; if (favorited != null) { bus.Publish(favorited); return; }
            var synced = evt as StickersSynced; if (synced != null) { bus.Publish(synced); return; }
            // Unknown domain event; intentionally drop. The bus dispatches by
            // static type only, so a generic fallback would not reach typed
            // subscribers.
        }
    }
}
