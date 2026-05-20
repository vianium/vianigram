// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Ports.Outbound
{
    /// <summary>
    /// Local cache for completed downloads. The default adapter is in-memory
    /// (<c>InMemoryMediaCache</c>); a SQLite + filesystem implementation can be
    /// swapped in (see <c>docs/managed-architecture/05-media.md</c>
    /// section 9).
    ///
    /// <para>The cache stores raw bytes for downloads so handlers can verify
    /// integrity before publishing <c>TransferCompleted</c>; production
    /// implementations should stream to disk and return only the path.</para>
    /// </summary>
    public interface IMediaCache
    {
        /// <summary>
        /// Look up a previously cached file by its server-side
        /// <see cref="FileLocation"/>. Returns <c>null</c> on miss; never
        /// throws.
        /// </summary>
        Task<MediaCacheEntry> TryGetAsync(FileLocation location, CancellationToken ct);

        /// <summary>
        /// Store the assembled bytes for a completed download. Implementations
        /// SHOULD be idempotent — duplicate stores for the same location must
        /// not corrupt the cache.
        /// </summary>
        Task<Result<MediaCacheEntry, MediaError>> PutAsync(FileLocation location, byte[] payload, FileType type, CancellationToken ct);

        /// <summary>
        /// Drop a single entry (manual eviction). Returns success even if the
        /// entry was already absent.
        /// </summary>
        Task<Result<Domain.ValueObjects.Unit, MediaError>> EvictAsync(FileLocation location, CancellationToken ct);
    }

    /// <summary>
    /// Snapshot of a cached entry. <c>LocalPath</c> is empty in the in-memory
    /// adapter; a filesystem-backed adapter returns the on-disk file path.
    /// </summary>
    public sealed class MediaCacheEntry
    {
        public MediaCacheEntry(FileLocation location, FileType type, long sizeBytes, byte[] payload, string localPath)
        {
            Location = location;
            Type = type;
            SizeBytes = sizeBytes;
            Payload = payload ?? new byte[0];
            LocalPath = localPath ?? string.Empty;
        }

        public FileLocation Location { get; private set; }
        public FileType Type { get; private set; }
        public long SizeBytes { get; private set; }
        public byte[] Payload { get; private set; }
        public string LocalPath { get; private set; }
    }
}
