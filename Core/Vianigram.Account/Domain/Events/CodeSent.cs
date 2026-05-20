// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Account.Domain.Events
{
    /// <summary>auth.sentCode confirmed by the server; UI can show the code-entry screen.</summary>
    public sealed class CodeSent : IDomainEvent
    {
        public AccountId AccountId { get; private set; }
        public SentCodeType Type { get; private set; }
        public SentCodeType? NextType { get; private set; }
        public DateTime AtUtc { get; private set; }

        public CodeSent(AccountId accountId, SentCodeType type, SentCodeType? nextType, DateTime atUtc)
        {
            AccountId = accountId;
            Type = type;
            NextType = nextType;
            AtUtc = atUtc;
        }
    }
}
