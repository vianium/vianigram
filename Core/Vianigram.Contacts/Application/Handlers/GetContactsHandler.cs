// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Local-cache read for IContactsApi.GetContactsAsync — no MTProto traffic.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Application.UseCases;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Contacts.Application.Handlers
{
    /// <summary>
    /// Returns a snapshot of the locally-cached <see cref="ContactBook"/>.
    /// Distinct from <see cref="SyncContactsHandler"/>: this handler does not
    /// touch the MTProto port — it only reads the in-memory aggregate via
    /// <see cref="IContactRepository.LoadAsync"/>. When the book has never
    /// been hydrated the repository synthesizes an empty aggregate (per its
    /// contract) so callers always see a non-null, possibly-empty list.
    ///
    /// Errors:
    ///   - Cancellation bubbles up as <see cref="OperationCanceledException"/>.
    ///   - Repository faults are surfaced as <see cref="ContactsError.Unknown"/>.
    /// </summary>
    internal sealed class GetContactsHandler
    {
        private readonly IContactRepository _repo;
        private readonly IComponentLogger _log;

        public GetContactsHandler(IContactRepository repo, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _log = new TimestampedLogger(log, "Contacts.GetContacts");
        }

        public async Task<Result<IList<Contact>, ContactsError>> HandleAsync(GetContactsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("null command"));

            try
            {
                ContactBook book = await _repo.LoadAsync(ct).ConfigureAwait(false);
                IList<Contact> snapshot = book.Snapshot();
                _log.Debug("local snapshot returned " + snapshot.Count + " contact(s)");
                return Result<IList<Contact>, ContactsError>.Ok(snapshot);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("local contacts read failed", ex));
            }
        }
    }
}
