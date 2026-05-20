// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Account.Domain.Events
{
    /// <summary>auth.logOut completed (server-side ack or local-only) — auth keys cleared.</summary>
    public sealed class LoggedOut : IDomainEvent
    {
        public AccountId AccountId { get; private set; }
        public DateTime AtUtc { get; private set; }

        public LoggedOut(AccountId accountId, DateTime atUtc)
        {
            AccountId = accountId;
            AtUtc = atUtc;
        }
    }
}
