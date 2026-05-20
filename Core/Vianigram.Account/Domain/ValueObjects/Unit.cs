// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Empty success payload for <c>Result&lt;Unit, AccountError&gt;</c>
    /// operations that produce no value but can still fail. Singleton via
    /// <see cref="Value"/>.
    ///
    /// Defined locally per context to avoid coupling
    /// <c>Vianigram.Kernel</c> to a Unit type (the kernel intentionally stays
    /// minimal). Other contexts may declare their own.
    /// </summary>
    public sealed class Unit
    {
        public static readonly Unit Value = new Unit();
        private Unit() { }
        public override string ToString() { return "()"; }
    }
}
