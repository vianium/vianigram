// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Account.Domain.Events
{
    /// <summary>Aggregate transitioned from Anonymous → WaitingForCode (intent recorded).</summary>
    public sealed class PhoneAuthInitiated : IDomainEvent
    {
        public AccountId AccountId { get; private set; }
        public PhoneNumber Phone { get; private set; }
        public DateTime AtUtc { get; private set; }

        public PhoneAuthInitiated(AccountId accountId, PhoneNumber phone, DateTime atUtc)
        {
            AccountId = accountId;
            Phone = phone;
            AtUtc = atUtc;
        }
    }
}
