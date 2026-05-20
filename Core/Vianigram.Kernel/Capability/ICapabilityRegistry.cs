// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Kernel.Capability
{
    /// <summary>
    /// Read-only port for checking whether a named capability is enabled.
    /// Used by the composition root to gate optional features (e.g. SecretChats,
    /// Stickers) without spreading conditionals throughout business logic.
    /// </summary>
    public interface ICapabilityRegistry
    {
        bool IsEnabled(CapabilityId id);
    }
}
