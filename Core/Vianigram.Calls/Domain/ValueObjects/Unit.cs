// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// Empty success payload for <c>Result&lt;Unit, CallError&gt;</c>
    /// operations that produce no value but can still fail. Singleton via
    /// <see cref="Value"/>.
    ///
    /// Defined locally per context (mirrors SecretChats, Contacts, Account
    /// etc.) so we don't couple <c>Vianigram.Kernel</c> to a Unit type.
    /// </summary>
    public sealed class Unit
    {
        public static readonly Unit Value = new Unit();
        private Unit() { }
        public override string ToString() { return "()"; }
    }
}
