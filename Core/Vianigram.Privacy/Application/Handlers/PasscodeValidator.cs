// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Privacy.Domain;

namespace Vianigram.Privacy.Application.Handlers
{
    /// <summary>
    /// PIN strength validator. The current rule is "4–6 digit numeric";
    /// the doc-spec'd <c>PasscodeStrengthPolicy</c> (Pin4 / Pin6 /
    /// Alphanumeric tiers) lands with the production hasher.
    /// Centralized so every passcode handler validates identically.
    /// </summary>
    internal static class PasscodeValidator
    {
        public const int MinLength = 4;
        public const int MaxLength = 16;

        public static PrivacyError ValidateOrNull(string pin)
        {
            if (string.IsNullOrEmpty(pin))
                return PrivacyError.InvalidValue("pin must not be empty");
            if (pin.Length < MinLength)
                return PrivacyError.InvalidValue("pin must be at least " + MinLength + " characters");
            if (pin.Length > MaxLength)
                return PrivacyError.InvalidValue("pin must not exceed " + MaxLength + " characters");
            // V1: any printable input is acceptable beyond length (the
            // alphanumeric tier ships later).
            return null;
        }
    }
}
