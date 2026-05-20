// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Ports.Outbound
{
    /// <summary>
    /// Outbound port for persisting <see cref="Dialog"/> aggregates.
    /// V1 implementation is in-memory (<c>InMemoryDialogRepository</c>); a SQLite-backed
    /// adapter will land in Vianigram.Storage and be hot-swapped at composition time —
    /// the application layer is unaware of the storage substrate.
    ///
    /// All operations are async to keep storage swap painless even though the in-memory
    /// implementation completes synchronously today.
    /// </summary>
    public interface IDialogRepository
    {
        /// <summary>Returns the aggregate for <paramref name="peer"/>, or null if absent.</summary>
        Task<Dialog> GetAsync(PeerId peer, CancellationToken ct);

        /// <summary>Snapshot of every persisted dialog. Cheap for in-memory; bounded for storage adapters.</summary>
        Task<IList<Dialog>> GetAllAsync(CancellationToken ct);

        /// <summary>Insert-or-update by <see cref="Dialog.Peer"/>.</summary>
        Task UpsertAsync(Dialog dialog, CancellationToken ct);

        /// <summary>Remove by peer. No-op if absent.</summary>
        Task DeleteAsync(PeerId peer, CancellationToken ct);

        /// <summary>Bulk upsert; for cold-sync / list refresh paths.</summary>
        Task UpsertManyAsync(IEnumerable<Dialog> dialogs, CancellationToken ct);
    }
}
