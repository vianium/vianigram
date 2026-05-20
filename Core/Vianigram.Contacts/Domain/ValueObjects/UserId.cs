// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Contacts.Domain.ValueObjects
{
    /// <summary>
    /// Telegram-issued user identifier (long). Wrapped to prevent accidental
    /// arithmetic and to namespace it from other identifiers.
    ///
    /// Defined locally per context (Account also has its own <c>UserId</c> for the
    /// same reason — bounded contexts do not share value objects to keep their
    /// ubiquitous languages independent).
    /// </summary>
    public struct UserId : IEquatable<UserId>
    {
        private readonly long _value;

        public UserId(long value)
        {
            if (value <= 0) throw new ArgumentException("user id must be positive", "value");
            _value = value;
        }

        public long Value { get { return _value; } }

        public bool Equals(UserId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is UserId && Equals((UserId)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "user:" + _value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(UserId a, UserId b) { return a.Equals(b); }
        public static bool operator !=(UserId a, UserId b) { return !a.Equals(b); }
    }
}
