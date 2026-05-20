// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Application.UseCases
{
    /// <summary>
    /// Fetch the value stored under <see cref="Key"/> as <typeparamref name="T"/>.
    /// The handler resolves a catalog default when no user value is present and
    /// surfaces a typed <c>SettingsError.TypeMismatch</c> when the stored
    /// canonical string cannot be coerced to the requested CLR type.
    /// </summary>
    public sealed class GetPreferenceCommand<T>
    {
        public PreferenceKey Key { get; private set; }
        public T Default { get; private set; }

        public GetPreferenceCommand(PreferenceKey key, T defaultValue)
        {
            Key = key;
            Default = defaultValue;
        }
    }
}
