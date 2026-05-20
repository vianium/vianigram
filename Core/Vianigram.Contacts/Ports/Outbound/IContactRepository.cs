// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Domain.Entities;

namespace Vianigram.Contacts.Ports.Outbound
{
    /// <summary>
    /// Outbound port for persisting the <see cref="ContactBook"/> aggregate.
    /// V1 implementation is in-memory (<c>InMemoryContactRepository</c>); a
    /// SQLite-backed adapter will land in <c>Vianigram.Storage</c> and be
    /// hot-swapped at composition time — the application layer is unaware of
    /// the storage substrate.
    ///
    /// All operations are async to keep storage swap painless even though the
    /// in-memory implementation completes synchronously today.
    ///
    /// Implementations MUST be thread-safe: the application uses a single
    /// aggregate per active account, and handlers may run on the thread pool.
    /// </summary>
    public interface IContactRepository
    {
        /// <summary>
        /// Returns the current aggregate. Never null — the repository synthesizes
        /// an empty <see cref="ContactBook"/> on first access.
        /// </summary>
        Task<ContactBook> LoadAsync(CancellationToken ct);

        /// <summary>Persist the supplied aggregate (typically the same reference returned by <see cref="LoadAsync"/>).</summary>
        Task SaveAsync(ContactBook book, CancellationToken ct);

        /// <summary>Wipe the aggregate (used on logout / account switch).</summary>
        Task DeleteAsync(CancellationToken ct);
    }
}
