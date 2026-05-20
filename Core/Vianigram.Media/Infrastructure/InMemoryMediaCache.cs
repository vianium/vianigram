// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Media.Ports.Outbound;

namespace Vianigram.Media.Infrastructure
{
    /// <summary>
    /// In-memory <see cref="IMediaCache"/>. Backs the composition root when
    /// the SQLite + filesystem adapter is not wired. Intentionally simple —
    /// no eviction, no quota — because the production cache lives in
    /// <c>Vianigram.Storage</c>.
    /// </summary>
    public sealed class InMemoryMediaCache : IMediaCache
    {
        private readonly Dictionary<FileLocation, MediaCacheEntry> _entries;
        private readonly object _gate;

        public InMemoryMediaCache()
        {
            _entries = new Dictionary<FileLocation, MediaCacheEntry>();
            _gate = new object();
        }

        public Task<MediaCacheEntry> TryGetAsync(FileLocation location, CancellationToken ct)
        {
            MediaCacheEntry entry = null;
            if (location != null)
            {
                lock (_gate)
                {
                    _entries.TryGetValue(location, out entry);
                }
            }
            return TaskFromResult<MediaCacheEntry>(entry);
        }

        public Task<Result<MediaCacheEntry, MediaError>> PutAsync(FileLocation location, byte[] payload, FileType type, CancellationToken ct)
        {
            if (location == null)
                return TaskFromResult(Result<MediaCacheEntry, MediaError>.Fail(MediaError.InvalidArgument("location null")));
            if (payload == null)
                return TaskFromResult(Result<MediaCacheEntry, MediaError>.Fail(MediaError.InvalidArgument("payload null")));

            var entry = new MediaCacheEntry(location, type, payload.Length, payload, string.Empty);
            lock (_gate)
            {
                _entries[location] = entry;
            }
            return TaskFromResult(Result<MediaCacheEntry, MediaError>.Ok(entry));
        }

        public Task<Result<Domain.ValueObjects.Unit, MediaError>> EvictAsync(FileLocation location, CancellationToken ct)
        {
            if (location == null)
                return TaskFromResult(Result<Domain.ValueObjects.Unit, MediaError>.Fail(MediaError.InvalidArgument("location null")));
            lock (_gate)
            {
                _entries.Remove(location);
            }
            return TaskFromResult(Result<Domain.ValueObjects.Unit, MediaError>.Ok(Domain.ValueObjects.Unit.Value));
        }

        private static Task<T> TaskFromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
