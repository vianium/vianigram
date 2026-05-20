// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Domain.ValueObjects
{
    /// <summary>
    /// Empty success payload for <c>Result&lt;Unit, PrivacyError&gt;</c>
    /// operations that produce no value but can still fail. Singleton via
    /// <see cref="Value"/>.
    ///
    /// Defined locally per context to avoid coupling Vianigram.Kernel to a Unit
    /// type. Mirrors the pattern in Vianigram.Settings, Vianigram.Search,
    /// Vianigram.Notifications and other contexts.
    /// </summary>
    public sealed class Unit
    {
        public static readonly Unit Value = new Unit();
        private Unit() { }
        public override string ToString() { return "()"; }
    }
}
