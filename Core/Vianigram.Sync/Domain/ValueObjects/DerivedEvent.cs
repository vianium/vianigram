// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Kernel.Events;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Marker for the derived (cross-context) events that SyncState.Apply emits
    /// as a side-effect of folding an UpdatesEnvelope. These are concrete types
    /// in Domain/Events/SyncEvents.cs (RemoteMessageReceived, etc.) and all
    /// implement IDomainEvent so they ride the same Vianigram.Kernel event bus.
    ///
    /// This is a transparent alias — semantically identical to IDomainEvent but
    /// scoped to "what Sync emits outward". Callers that consume ApplyResult
    /// can route these to IEventBus.Publish&lt;TEvent&gt; without reflection.
    /// </summary>
    public interface IDerivedEvent : IDomainEvent
    {
    }
}
