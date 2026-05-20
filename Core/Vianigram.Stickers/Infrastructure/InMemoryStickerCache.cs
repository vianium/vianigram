// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Domain.ValueObjects;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Infrastructure
{
    /// <summary>
    /// In-memory <see cref="IStickerCachePort"/>. Backs the composition root
    /// when the SQLite + LocalFolder adapter is not wired. Intentionally
    /// simple — no eviction, no quota — because the production cache lives in
    /// <c>Vianigram.Storage</c>.
    ///
    /// Thread-safe: all read/write paths take a lock on a private gate.
    /// </summary>
    public sealed class InMemoryStickerCache : IStickerCachePort
    {
        private readonly Dictionary<StickerCacheKey, byte[]> _entries;
        private readonly object _gate;

        public InMemoryStickerCache()
        {
            _entries = new Dictionary<StickerCacheKey, byte[]>();
            _gate = new object();
        }

        public Task<byte[]> TryGetAsync(StickerCacheKey key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[] payload = null;
            lock (_gate)
            {
                _entries.TryGetValue(key, out payload);
            }
            return TaskFromResult(payload);
        }

        public Task<Result<Unit, StickersError>> PutAsync(StickerCacheKey key, byte[] payload, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (payload == null)
                return TaskFromResult(Result<Unit, StickersError>.Fail(StickersError.NotInExpectedState("payload null")));

            lock (_gate)
            {
                _entries[key] = payload;
            }
            return TaskFromResult(Result<Unit, StickersError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, StickersError>> EvictAsync(StickerCacheKey key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _entries.Remove(key);
            }
            return TaskFromResult(Result<Unit, StickersError>.Ok(Unit.Value));
        }

        public Task<Result<Unit, StickersError>> EvictPackAsync(StickerSetId setId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var toRemove = new List<StickerCacheKey>();
                foreach (var kv in _entries)
                {
                    if (kv.Key.SetId == setId.Value) toRemove.Add(kv.Key);
                }
                for (int i = 0; i < toRemove.Count; i++)
                {
                    _entries.Remove(toRemove[i]);
                }
            }
            return TaskFromResult(Result<Unit, StickersError>.Ok(Unit.Value));
        }

        private static Task<T> TaskFromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
