// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Sync.Domain.ValueObjects;

namespace Vianigram.Sync.Ports.Outbound
{
    /// <summary>
    /// Persistence port for sync cursor state. The adapter chooses the storage
    /// medium (LocalSettings, SQLite, in-memory for tests). Per the architecture
    /// doc §9, <see cref="LocalSettings"/> is preferred for the common cursor
    /// because writes are debounced and frequent (potentially every few seconds).
    ///
    /// Returning <see cref="SyncCursor.Initial"/> for a missing cursor is the
    /// contract for cold-start; adapters MUST NOT throw on first launch.
    /// </summary>
    public interface ISyncStateRepository
    {
        Task<SyncCursor> LoadCursorAsync(CancellationToken ct);

        Task SaveCursorAsync(SyncCursor cursor, CancellationToken ct);

        Task<IDictionary<long, ChannelCursor>> LoadChannelCursorsAsync(CancellationToken ct);

        Task SaveChannelCursorAsync(ChannelCursor cursor, CancellationToken ct);

        Task RemoveChannelCursorAsync(long channelId, CancellationToken ct);

        /// <summary>
        /// Wipe all persisted state. Called on logout or destructive resync.
        /// </summary>
        Task ClearAsync(CancellationToken ct);
    }
}
