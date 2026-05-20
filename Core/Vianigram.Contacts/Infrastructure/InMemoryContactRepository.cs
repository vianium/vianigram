// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Ports.Outbound;

namespace Vianigram.Contacts.Infrastructure
{
    /// <summary>
    /// In-memory repository: keeps the single <see cref="ContactBook"/>
    /// aggregate in process memory guarded by a private monitor.
    ///
    /// Sufficient for cold-start, sync, and UI consumption while the SQLite-backed
    /// repository in <c>Vianigram.Storage</c> is built. Hot-swap point: replace
    /// the binding in <see cref="Vianigram.Contacts.Composition.ContactsCompositionRoot"/>
    /// with the persistent adapter and the application layer is unchanged.
    ///
    /// Thread-safety: all read/write paths take a lock on a private gate object.
    /// We intentionally hand back the live aggregate (NOT a copy) so handlers
    /// can mutate it in place — the lock here only serializes the
    /// load/save/delete transitions, not domain mutations. Application-layer
    /// callers serialize their own aggregate access by single-threading
    /// command handling per <see cref="Application.ContactsApplication"/>.
    /// </summary>
    public sealed class InMemoryContactRepository : IContactRepository
    {
        private readonly object _gate = new object();
        private ContactBook _book;

        public Task<ContactBook> LoadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_book == null) _book = new ContactBook();
                return Task.FromResult(_book);
            }
        }

        public Task SaveAsync(ContactBook book, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (book == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                _book = book;
            }
            return Task.FromResult<object>(null);
        }

        public Task DeleteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _book = null;
            }
            return Task.FromResult<object>(null);
        }
    }
}
