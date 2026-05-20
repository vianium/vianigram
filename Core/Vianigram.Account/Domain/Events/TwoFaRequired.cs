// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Account.Domain.Events
{
    /// <summary>auth.signIn returned SESSION_PASSWORD_NEEDED; UI must collect the 2FA password.</summary>
    public sealed class TwoFaRequired : IDomainEvent
    {
        public AccountId AccountId { get; private set; }
        public DateTime AtUtc { get; private set; }

        public TwoFaRequired(AccountId accountId, DateTime atUtc)
        {
            AccountId = accountId;
            AtUtc = atUtc;
        }
    }
}
