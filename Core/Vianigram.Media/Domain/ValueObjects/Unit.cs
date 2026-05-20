// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Media.Domain.ValueObjects
{
    /// <summary>
    /// Empty value type used as the success payload of operations that return
    /// nothing. Equivalent to <c>void</c> but composable through Result&lt;T,E&gt;.
    /// </summary>
    public struct Unit
    {
        public static readonly Unit Value = new Unit();
    }
}
