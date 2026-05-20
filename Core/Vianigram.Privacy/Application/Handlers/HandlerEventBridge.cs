// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Privacy.Domain.Entities;
using Vianigram.Privacy.Domain.Events;

namespace Vianigram.Privacy.Application.Handlers
{
    /// <summary>
    /// Drains the staged domain events from a <see cref="PrivacyProfile"/>
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
        public static void Drain(PrivacyProfile profile, IEventBus bus)
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
            var ruleChanged = evt as PrivacyRuleChanged; if (ruleChanged != null) { bus.Publish(ruleChanged); return; }
            var sessionsLoaded = evt as SessionsLoaded; if (sessionsLoaded != null) { bus.Publish(sessionsLoaded); return; }
            var sessionTerminated = evt as SessionTerminated; if (sessionTerminated != null) { bus.Publish(sessionTerminated); return; }
            var allOther = evt as AllOtherSessionsTerminated; if (allOther != null) { bus.Publish(allOther); return; }
            var passcodeChanged = evt as PasscodeChanged; if (passcodeChanged != null) { bus.Publish(passcodeChanged); return; }
            var unlocked = evt as PasscodeUnlocked; if (unlocked != null) { bus.Publish(unlocked); return; }
            var failed = evt as PasscodeFailedAttempt; if (failed != null) { bus.Publish(failed); return; }
            // Unknown domain event; intentionally drop. The bus dispatches by
            // static type only, so a generic fallback would not reach typed
            // subscribers.
        }
    }
}
