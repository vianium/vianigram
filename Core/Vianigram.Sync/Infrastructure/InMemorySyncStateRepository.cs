// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Sync.Domain.ValueObjects;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Sync.Infrastructure
{
    /// <summary>
    /// In-memory cursor repository. Holds the common cursor and a channel
    /// cursor map in process; nothing is durable. The Vianigram.Storage
    /// adapter (LocalSettings-backed) is described in
    /// docs/managed-architecture/06-sync.md §9.
    ///
    /// Thread-safety: simple coarse lock — mutation is rare (debounced 250 ms by
    /// the application layer in the production adapter; here we just take a lock
    /// on every call for safety in tests and during composition).
    /// </summary>
    public sealed class InMemorySyncStateRepository : ISyncStateRepository
    {
        private readonly object _gate = new object();
        private SyncCursor _cursor;
        private readonly Dictionary<long, ChannelCursor> _channels;

        public InMemorySyncStateRepository()
        {
            _cursor = SyncCursor.Initial();
            _channels = new Dictionary<long, ChannelCursor>();
        }

        public Task<SyncCursor> LoadCursorAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            SyncCursor snapshot;
            lock (_gate) { snapshot = _cursor; }
            return Task.FromResult(snapshot);
        }

        public Task SaveCursorAsync(SyncCursor cursor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate) { _cursor = cursor ?? SyncCursor.Initial(); }
            return EmptyTask();
        }

        public Task<IDictionary<long, ChannelCursor>> LoadChannelCursorsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            IDictionary<long, ChannelCursor> snapshot;
            lock (_gate)
            {
                snapshot = new Dictionary<long, ChannelCursor>(_channels);
            }
            return Task.FromResult(snapshot);
        }

        public Task SaveChannelCursorAsync(ChannelCursor cursor, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (cursor == null) return EmptyTask();
            lock (_gate) { _channels[cursor.ChannelId] = cursor; }
            return EmptyTask();
        }

        public Task RemoveChannelCursorAsync(long channelId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate) { _channels.Remove(channelId); }
            return EmptyTask();
        }

        public Task ClearAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _cursor = SyncCursor.Initial();
                _channels.Clear();
            }
            return EmptyTask();
        }

        private static Task EmptyTask()
        {
            // Task.CompletedTask is .NET 4.6+; WP8.1 targets .NET Core profile but
            // safer to materialize an already-completed Task<bool> for portability.
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);
            return tcs.Task;
        }
    }
}
