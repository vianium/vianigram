// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Account.Domain.Events
{
    /// <summary>Terminal failure of an auth attempt (FloodWait, code invalid, network, etc.).</summary>
    public sealed class AuthFailed : IDomainEvent
    {
        public AccountId AccountId { get; private set; }
        public AccountError Error { get; private set; }
        public DateTime AtUtc { get; private set; }

        public AuthFailed(AccountId accountId, AccountError error, DateTime atUtc)
        {
            AccountId = accountId;
            Error = error;
            AtUtc = atUtc;
        }
    }
}
