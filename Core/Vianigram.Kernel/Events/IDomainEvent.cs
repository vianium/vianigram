// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Kernel.Events
{
    /// <summary>
    /// Marker interface for domain events. Event types implement this so the
    /// in-memory bus can dispatch them with type safety.
    /// </summary>
    public interface IDomainEvent
    {
    }
}
