// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Kernel.Events
{
    /// <summary>
    /// In-process pub/sub for <see cref="IDomainEvent"/> messages.
    /// Subscribers receive events synchronously on the publisher's thread.
    /// </summary>
    public interface IEventBus
    {
        void Publish<TEvent>(TEvent e) where TEvent : IDomainEvent;

        IDisposable Subscribe<TEvent>(Action<TEvent> handler) where TEvent : IDomainEvent;
    }
}
