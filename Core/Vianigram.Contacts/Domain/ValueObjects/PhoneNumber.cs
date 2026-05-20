// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Contacts.Domain.ValueObjects
{
    /// <summary>
    /// E.164-style phone number used for contact import and lookup. Validation
    /// rules are intentionally lenient compared to Account.PhoneNumber: the
    /// Telegram contact-import endpoint accepts non-E.164 strings (e.g. local
    /// formats) and matches them server-side. We strip cosmetic separators and
    /// require at least 5 digits, but do NOT mandate a leading '+'.
    ///
    /// Immutable; equality is by raw E.164 form (case- and ordinal-equal).
    /// </summary>
    public sealed class PhoneNumber : IEquatable<PhoneNumber>
    {
        private readonly string _value;

        private PhoneNumber(string value)
        {
            _value = value;
        }

        public string Value { get { return _value; } }

        /// <summary>
        /// Returns null when the input cannot be normalized to a non-empty
        /// digit-only (optional leading '+') sequence of at least 5 digits.
        /// Callers wrap a null into <see cref="ContactsError"/>.
        /// </summary>
        public static PhoneNumber TryParse(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;

            string trimmed = raw.Trim();
            char[] buffer = new char[trimmed.Length];
            int outIdx = 0;
            bool plusSeen = false;
            for (int i = 0; i < trimmed.Length; i++)
            {
                char c = trimmed[i];
                if (c == ' ' || c == '-' || c == '(' || c == ')' || c == '.') continue;
                if (c == '+')
                {
                    if (plusSeen || outIdx > 0) return null;
                    plusSeen = true;
                    buffer[outIdx++] = '+';
                    continue;
                }
                if (c < '0' || c > '9') return null;
                buffer[outIdx++] = c;
            }

            if (outIdx == 0) return null;
            int digitCount = plusSeen ? outIdx - 1 : outIdx;
            if (digitCount < 5 || digitCount > 15) return null;

            return new PhoneNumber(new string(buffer, 0, outIdx));
        }

        public bool Equals(PhoneNumber other)
        {
            if (ReferenceEquals(other, null)) return false;
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PhoneNumber);
        }

        public override int GetHashCode()
        {
            return _value == null ? 0 : _value.GetHashCode();
        }

        public override string ToString() { return _value ?? string.Empty; }
    }
}
