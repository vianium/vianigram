// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// UiEventBusExtensions.cs — Vianigram.Kernel.Concurrency
//
// IEventBus delivers handlers synchronously on the publisher's thread.
// View-model subscribers that update XAML state need to marshal onto the
// UI thread before mutating ObservableCollection / TextBlock state. The
// extension method here packages that pattern so VMs don't have to
// remember the marshal call site by site:
//
//   bus.SubscribeOnUi<MessageReceived>(uiDispatcher, OnMessageReceived);
//
// The returned IDisposable composes with the existing Subscribe<T>
// contract — disposing it removes the underlying subscription.

using System;
using Vianigram.Kernel.Events;

namespace Vianigram.Kernel.Concurrency
{
    public static class UiEventBusExtensions
    {
        /// <summary>
        /// Subscribe to <typeparamref name="TEvent"/> with automatic
        /// marshaling onto the UI thread before <paramref name="handler"/>
        /// runs. Same contract as <see cref="IEventBus.Subscribe{TEvent}"/>
        /// otherwise — disposing the returned handle unregisters the
        /// subscription.
        /// </summary>
        public static IDisposable SubscribeOnUi<TEvent>(
            this IEventBus bus,
            IUiDispatcher uiDispatcher,
            Action<TEvent> handler) where TEvent : IDomainEvent
        {
            if (bus == null) throw new ArgumentNullException("bus");
            if (handler == null) throw new ArgumentNullException("handler");

            // No dispatcher → fall back to inline delivery so the bus
            // contract is preserved even in degraded environments.
            IUiDispatcher dispatcher = uiDispatcher ?? InlineUiDispatcher.Instance;

            return bus.Subscribe<TEvent>(e =>
            {
                if (dispatcher.HasUiThreadAccess)
                {
                    try { handler(e); }
                    catch { /* match bus exception-swallow contract */ }
                    return;
                }

                var ignored = dispatcher.RunOnUiAsync(() =>
                {
                    try { handler(e); }
                    catch { /* match bus exception-swallow contract */ }
                });
                GC.KeepAlive(ignored);
            });
        }
    }
}
