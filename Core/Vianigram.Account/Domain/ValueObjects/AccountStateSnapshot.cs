// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Read-only projection of <see cref="Entities.AccountIdentity"/> safe to
    /// hand to UI / external contexts. Avoids leaking the aggregate or its
    /// internal collections.
    /// </summary>
    public sealed class AccountStateSnapshot
    {
        public AccountId Id { get; private set; }
        public string Phone { get; private set; }
        public AuthState.AuthStateKind StateKind { get; private set; }
        public long? UserId { get; private set; }
        public int? DcId { get; private set; }
        public SentCodeType? SentCodeType { get; private set; }
        public SentCodeType? NextCodeType { get; private set; }
        public DateTime? CodeExpiresAtUtc { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime LastActivityUtc { get; private set; }

        public AccountStateSnapshot(
            AccountId id,
            string phone,
            AuthState.AuthStateKind kind,
            long? userId,
            int? dcId,
            SentCodeType? sentCodeType,
            SentCodeType? nextCodeType,
            DateTime? codeExpiresAtUtc,
            DateTime createdAtUtc,
            DateTime lastActivityUtc)
        {
            Id = id;
            Phone = phone;
            StateKind = kind;
            UserId = userId;
            DcId = dcId;
            SentCodeType = sentCodeType;
            NextCodeType = nextCodeType;
            CodeExpiresAtUtc = codeExpiresAtUtc;
            CreatedAtUtc = createdAtUtc;
            LastActivityUtc = lastActivityUtc;
        }
    }
}
