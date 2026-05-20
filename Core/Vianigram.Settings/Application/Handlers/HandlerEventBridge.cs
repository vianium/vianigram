// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Settings.Domain.Entities;
using Vianigram.Settings.Domain.Events;

namespace Vianigram.Settings.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="UserPreferences"/>
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
        public static void Drain(UserPreferences prefs, IEventBus bus)
        {
            if (prefs == null || bus == null) return;
            IList<IDomainEvent> events = prefs.DequeuePendingEvents();
            for (int i = 0; i < events.Count; i++)
            {
                Dispatch(events[i], bus);
            }
        }

        private static void Dispatch(IDomainEvent evt, IEventBus bus)
        {
            var changed = evt as PreferenceChanged; if (changed != null) { bus.Publish(changed); return; }
            var theme = evt as ThemeChanged; if (theme != null) { bus.Publish(theme); return; }
            var lang = evt as LanguageChanged; if (lang != null) { bus.Publish(lang); return; }
            var data = evt as DataPolicyChanged; if (data != null) { bus.Publish(data); return; }
            var reset = evt as PreferencesReset; if (reset != null) { bus.Publish(reset); return; }
            // Unknown domain event; intentionally drop. The bus dispatches by
            // static type only, so a generic fallback would not reach typed
            // subscribers.
        }
    }
}
