// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Application.UseCases
{
    /// <summary>
    /// Persist <see cref="Value"/> under <see cref="Key"/>. The handler
    /// validates the value (range / enum / shape — V1 catalog applies a
    /// no-op validator for keys without an explicit one), stages a
    /// <c>PreferenceChanged</c> domain event when the value transitions, and
    /// writes the canonical string to the underlying store.
    /// </summary>
    public sealed class SetPreferenceCommand<T>
    {
        public PreferenceKey Key { get; private set; }
        public T Value { get; private set; }

        public SetPreferenceCommand(PreferenceKey key, T value)
        {
            Key = key;
            Value = value;
        }
    }
}
