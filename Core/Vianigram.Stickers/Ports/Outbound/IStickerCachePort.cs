// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Ports.Outbound
{
    /// <summary>
    /// Outbound port for storing decoded sticker blobs. V1 implementation is
    /// in-memory (<c>InMemoryStickerCache</c>); the production adapter in
    /// <c>Vianigram.Storage</c> persists to <c>LocalFolder/stickers/{packId}/{stickerId}.bin</c>
    /// and exposes the same shape — see <c>docs/managed-architecture/09-stickers.md §9</c>.
    ///
    /// Why a dedicated cache port (separate from the metadata repository):
    ///   * Blobs are large (30–60 KB per sticker) and pack-evict-able.
    ///   * Metadata is small and queried by emoji / set id (different access
    ///     pattern; favors a different storage substrate).
    ///   * Lazy load: a pack's metadata is fetched eagerly on install but its
    ///     blobs only when the user opens the pack.
    ///
    /// Implementations MUST be thread-safe.
    /// </summary>
    public interface IStickerCachePort
    {
        /// <summary>
        /// Returns the cached payload for <paramref name="key"/>, or
        /// <c>null</c> if the entry is absent. Absent is NOT an error.
        /// </summary>
        Task<byte[]> TryGetAsync(StickerCacheKey key, CancellationToken ct);

        /// <summary>Insert or replace the cached payload.</summary>
        Task<Result<Unit, StickersError>> PutAsync(StickerCacheKey key, byte[] payload, CancellationToken ct);

        /// <summary>Evict a single sticker blob.</summary>
        Task<Result<Unit, StickersError>> EvictAsync(StickerCacheKey key, CancellationToken ct);

        /// <summary>
        /// Evict every blob owned by the given set (used on uninstall —
        /// SQLite-backed adapters can implement this as a folder-wide
        /// rmdir).
        /// </summary>
        Task<Result<Unit, StickersError>> EvictPackAsync(StickerSetId setId, CancellationToken ct);
    }
}
