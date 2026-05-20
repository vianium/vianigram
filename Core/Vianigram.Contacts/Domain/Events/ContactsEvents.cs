// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Contacts.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Contacts.Domain.Events
{
    /// <summary>
    /// Emitted at the end of a successful <c>contacts.getContacts</c> sync.
    /// Carries the contact count after sync so subscribers can validate
    /// expectations (UI badge, telemetry) without re-querying the repo.
    /// </summary>
    public sealed class ContactsSynced : IDomainEvent
    {
        public int ContactCount { get; private set; }
        public DateTime At { get; private set; }

        public ContactsSynced(int contactCount, DateTime at)
        {
            if (contactCount < 0) throw new ArgumentOutOfRangeException("contactCount", "must be >= 0");
            ContactCount = contactCount;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a brand-new contact enters the book (imported, resolved
    /// via username/search, or surfaced by a server sync).
    /// </summary>
    public sealed class ContactImported : IDomainEvent
    {
        public UserId UserId { get; private set; }
        public DateTime At { get; private set; }

        public ContactImported(UserId userId, DateTime at)
        {
            UserId = userId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when an existing contact's facets (phone, name, username,
    /// mutual flag) change. Subscribers re-read the aggregate via the repo
    /// for the new values.
    /// </summary>
    public sealed class ContactUpdated : IDomainEvent
    {
        public UserId UserId { get; private set; }
        public DateTime At { get; private set; }

        public ContactUpdated(UserId userId, DateTime at)
        {
            UserId = userId;
            At = at;
        }
    }

    /// <summary>Emitted when a contact leaves the book.</summary>
    public sealed class ContactRemoved : IDomainEvent
    {
        public UserId UserId { get; private set; }
        public DateTime At { get; private set; }

        public ContactRemoved(UserId userId, DateTime at)
        {
            UserId = userId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a user is blocked (either via <c>contacts.block</c> or
    /// surfaced by a <c>contacts.getBlocked</c> sync that revealed a new
    /// remote-side block).
    /// </summary>
    public sealed class UserBlocked : IDomainEvent
    {
        public UserId UserId { get; private set; }
        public DateTime At { get; private set; }

        public UserBlocked(UserId userId, DateTime at)
        {
            UserId = userId;
            At = at;
        }
    }

    /// <summary>Emitted when a user leaves the blocked set.</summary>
    public sealed class UserUnblocked : IDomainEvent
    {
        public UserId UserId { get; private set; }
        public DateTime At { get; private set; }

        public UserUnblocked(UserId userId, DateTime at)
        {
            UserId = userId;
            At = at;
        }
    }
}
