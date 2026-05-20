// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Server-issued opaque token returned by <c>auth.sentCode</c>. Required to
    /// be echoed back in <c>auth.signIn</c> / <c>auth.resendCode</c> /
    /// <c>auth.cancelCode</c> so the server can correlate the SMS code session.
    /// Treated as opaque by the client.
    /// </summary>
    public sealed class PhoneCodeHash : IEquatable<PhoneCodeHash>
    {
        public string Value { get; private set; }

        public PhoneCodeHash(string value)
        {
            if (value == null) throw new ArgumentNullException("value");
            Value = value;
        }

        public bool Equals(PhoneCodeHash other)
        {
            if (ReferenceEquals(other, null)) return false;
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as PhoneCodeHash);
        }

        public override int GetHashCode()
        {
            return Value == null ? 0 : Value.GetHashCode();
        }

        public override string ToString()
        {
            return "phone_code_hash(" + (Value == null ? 0 : Value.Length) + " chars)";
        }
    }
}
