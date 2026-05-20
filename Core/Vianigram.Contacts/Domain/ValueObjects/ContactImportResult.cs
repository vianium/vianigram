// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Contacts.Domain.ValueObjects
{
    /// <summary>
    /// Server-side outcome of a single <c>contacts.importContacts</c> row, as
    /// returned in the <c>importedContact</c> vector. Mirrors TL
    /// <c>importedContact#c13e3c50</c>: <c>(client_id, user_id)</c>.
    ///
    /// A row may not appear in the response (server hit rate limits, retry
    /// later) — the caller checks the input vs the returned imported list.
    /// Immutable.
    /// </summary>
    public sealed class ContactImportResult
    {
        private readonly long _clientId;
        private readonly long _userId;

        public ContactImportResult(long clientId, long userId)
        {
            _clientId = clientId;
            _userId = userId;
        }

        public long ClientId { get { return _clientId; } }
        public long UserId { get { return _userId; } }
    }

    /// <summary>
    /// Summary returned by a bulk import: list of resolved (client_id ->
    /// user_id) pairs, list of <c>retry_contacts</c> the server asked us to
    /// resend later, and the count of successfully imported entries.
    /// </summary>
    public sealed class ContactImportSummary
    {
        private readonly IList<ContactImportResult> _imported;
        private readonly IList<long> _retryClientIds;

        public ContactImportSummary(IList<ContactImportResult> imported, IList<long> retryClientIds)
        {
            _imported = imported ?? new ContactImportResult[0];
            _retryClientIds = retryClientIds ?? new long[0];
        }

        public IList<ContactImportResult> Imported { get { return _imported; } }
        public IList<long> RetryClientIds { get { return _retryClientIds; } }
        public int ImportedCount { get { return _imported.Count; } }
        public int RetryCount { get { return _retryClientIds.Count; } }
    }
}
