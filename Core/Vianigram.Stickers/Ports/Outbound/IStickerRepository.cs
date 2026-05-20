// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Stickers.Domain.Entities;

namespace Vianigram.Stickers.Ports.Outbound
{
    /// <summary>
    /// Outbound port for persisting the <see cref="StickerLibrary"/> aggregate.
    /// V1 implementation is in-memory (<c>InMemoryStickerRepository</c>); a
    /// SQLite-backed adapter can land in <c>Vianigram.Storage</c> and be
    /// hot-swapped at composition time — the application layer is unaware of
    /// the storage substrate.
    ///
    /// All operations are async to keep storage swap painless even though the
    /// in-memory implementation completes synchronously today.
    ///
    /// Implementations MUST be thread-safe: the application uses a single
    /// aggregate per active account, and handlers may run on the thread pool.
    /// </summary>
    public interface IStickerRepository
    {
        /// <summary>
        /// Returns the current aggregate. Never null — the repository synthesizes
        /// an empty <see cref="StickerLibrary"/> on first access.
        /// </summary>
        Task<StickerLibrary> LoadAsync(CancellationToken ct);

        /// <summary>Persist the supplied aggregate (typically the same reference returned by <see cref="LoadAsync"/>).</summary>
        Task SaveAsync(StickerLibrary library, CancellationToken ct);

        /// <summary>Wipe the aggregate (used on logout / account switch).</summary>
        Task DeleteAsync(CancellationToken ct);
    }
}
