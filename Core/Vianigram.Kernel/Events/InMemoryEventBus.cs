// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Kernel.Events
{
    /// <summary>
    /// Simple thread-safe, in-process implementation of <see cref="IEventBus"/>.
    /// Handlers are invoked synchronously; exceptions are swallowed so a single
    /// faulty subscriber cannot break the chain.
    /// </summary>
    public sealed class InMemoryEventBus : IEventBus
    {
        private readonly object _gate = new object();
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public void Publish<TEvent>(TEvent e) where TEvent : IDomainEvent
        {
            if (e == null) throw new ArgumentNullException("e");

            Delegate[] snapshot;
            lock (_gate)
            {
                List<Delegate> list;
                if (!_handlers.TryGetValue(typeof(TEvent), out list) || list.Count == 0)
                    return;
                snapshot = list.ToArray();
            }

            for (int i = 0; i < snapshot.Length; i++)
            {
                var typed = snapshot[i] as Action<TEvent>;
                if (typed == null) continue;
                try { typed(e); }
                catch { /* swallow: do not let one subscriber poison the bus */ }
            }
        }

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent
        {
            if (handler == null) throw new ArgumentNullException("handler");

            lock (_gate)
            {
                List<Delegate> list;
                if (!_handlers.TryGetValue(typeof(TEvent), out list))
                {
                    list = new List<Delegate>();
                    _handlers[typeof(TEvent)] = list;
                }
                list.Add(handler);
            }

            return new Subscription(() =>
            {
                lock (_gate)
                {
                    List<Delegate> list;
                    if (_handlers.TryGetValue(typeof(TEvent), out list))
                        list.Remove(handler);
                }
            });
        }

        private sealed class Subscription : IDisposable
        {
            private Action _dispose;

            public Subscription(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                var d = _dispose;
                _dispose = null;
                if (d != null) d();
            }
        }
    }
}
