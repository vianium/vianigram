// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Account.Domain.Events
{
    /// <summary>auth.signIn (or auth.checkPassword) returned auth.authorization.</summary>
    public sealed class AuthSucceeded : IDomainEvent
    {
        public AccountId AccountId { get; private set; }
        public UserId UserId { get; private set; }
        public int DcId { get; private set; }
        public DateTime AtUtc { get; private set; }

        public AuthSucceeded(AccountId accountId, UserId userId, int dcId, DateTime atUtc)
        {
            AccountId = accountId;
            UserId = userId;
            DcId = dcId;
            AtUtc = atUtc;
        }
    }
}
