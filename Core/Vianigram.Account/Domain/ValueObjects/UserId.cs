// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Telegram-issued user identifier (long). Wrapped to prevent accidental
    /// arithmetic and to namespace it from <see cref="AccountId"/>.
    /// </summary>
    public struct UserId : IEquatable<UserId>
    {
        private readonly long _value;

        public UserId(long value)
        {
            _value = value;
        }

        public long Value
        {
            get { return _value; }
        }

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
            return "user:" + _value;
        }
    }
}
