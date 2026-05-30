// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// Persistent peer-avatar byte cache. The chat list cold-start path
    /// hits ~16 avatars in parallel after login; each fetch is a
    /// 6-13 KB JPEG via upload.getFile over an MTProto socket and costs
    /// 150-300 ms per RPC on a Lumia on 4G. Persisting the raw bytes
    /// turns the next login into a pure-local SQLite read of a few KB
    /// per row (<5 ms each) and drops the avatar-fetch wall time on
    /// repeated logins from ~5 s to under 200 ms aggregate.
    ///
    /// Implementations must:
    ///   - Be safe to call from any thread (the SQLite adapter uses
    ///     the shared SqliteDatabase.Gate; the store does not require
    ///     external locking).
    ///   - Be cheap on the hot path. <see cref="TryLoadAsync"/> runs
    ///     once per dialog row at cold start; <see cref="SaveAsync"/>
    ///     runs once per successful HD download.
    ///   - Eat their own errors. A cache miss / unavailable DB returns
    ///     null from TryLoad and a silently swallowed SaveAsync — the
    ///     fetcher must still fall back to the live upload.getFile
    ///     path so the user keeps seeing avatars.
    ///   - <see cref="EvictOlderThanAsync"/> is opt-in LRU pruning,
    ///     intended to be called nightly from a background task. The
    ///     v1 storage layer does not wire this hook automatically.
    /// </summary>
    public interface IAvatarCacheStore
    {
        Task<byte[]> TryLoadAsync(long photoId, CancellationToken ct);
        Task SaveAsync(long photoId, int dcId, byte[] bytes, string format, CancellationToken ct);
        Task EvictOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct);
    }
}
