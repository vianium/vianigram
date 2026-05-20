// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Local-only identity for an account aggregate. GUID-based so we can host a
    /// pending (not yet authorized) identity before knowing the Telegram user_id.
    /// Distinct from <see cref="UserId"/> which is server-issued.
    /// </summary>
    public struct AccountId : IEquatable<AccountId>
    {
        private readonly Guid _value;

        private AccountId(Guid value)
        {
            _value = value;
        }

        public static AccountId New()
        {
            return new AccountId(Guid.NewGuid());
        }

        public static AccountId FromGuid(Guid value)
        {
            return new AccountId(value);
        }

        public bool IsEmpty
        {
            get { return _value == Guid.Empty; }
        }

        public Guid Value
        {
            get { return _value; }
        }

        public bool Equals(AccountId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is AccountId && Equals((AccountId)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "acct:" + _value.ToString("N");
        }
    }
}
