// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Contacts.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Contacts bounded context (V1 shape).
    /// Every method is async, takes a <see cref="CancellationToken"/>, and
    /// returns <c>Result&lt;T, ContactsError&gt;</c>; no exceptions cross this
    /// boundary.
    ///
    /// Consumers: presentation/ViewModels, other contexts via ACL adapters,
    /// composition root for wiring.
    /// </summary>
    public interface IContactsApi
    {
        /// <summary>Full sync from the server (<c>contacts.getContacts</c>).</summary>
        Task<Result<IList<Contact>, ContactsError>> SyncContactsAsync(CancellationToken ct);

        /// <summary>
        /// Returns the locally-cached contact list (no MTProto round-trip).
        /// Differs from <see cref="SyncContactsAsync"/> which always hits the
        /// wire. Reads the persisted <see cref="ContactBook"/> via the repository
        /// port and returns its <c>Snapshot()</c>; an empty list is returned when
        /// the cache has not yet been hydrated. Cache hydration is the caller's
        /// responsibility — typically by invoking <see cref="SyncContactsAsync"/>
        /// once after sign-in.
        /// </summary>
        Task<Result<IList<Contact>, ContactsError>> GetContactsAsync(CancellationToken ct);

        /// <summary>Bulk import phone contacts (<c>contacts.importContacts</c>).</summary>
        Task<Result<IList<Contact>, ContactsError>> ImportContactsAsync(
            IList<ContactImportRequest> requests, CancellationToken ct);

        /// <summary>Server-side search by name, username or phone (<c>contacts.search</c>).</summary>
        Task<Result<IList<Contact>, ContactsError>> SearchAsync(string query, int limit, CancellationToken ct);

        /// <summary>Block a user (<c>contacts.block</c>).</summary>
        Task<Result<Unit, ContactsError>> BlockAsync(long userId, CancellationToken ct);

        /// <summary>Unblock a user (<c>contacts.unblock</c>).</summary>
        Task<Result<Unit, ContactsError>> UnblockAsync(long userId, CancellationToken ct);

        /// <summary>Fetch the blocked-user list (<c>contacts.getBlocked</c>).</summary>
        Task<Result<IList<long>, ContactsError>> GetBlockedListAsync(CancellationToken ct);

        /// <summary>
        /// CLR event raised whenever the contact book or blocked set changes.
        /// Subscribers receive a <see cref="ContactsChangedEventArgs"/>
        /// describing what happened. Multicast from the implementation;
        /// thread-safe add/remove.
        /// </summary>
        event EventHandler<ContactsChangedEventArgs> ContactsChanged;
    }
}
