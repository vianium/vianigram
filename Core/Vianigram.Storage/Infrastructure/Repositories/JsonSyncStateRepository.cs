// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Storage.Application;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Repositories
{
    /// <summary>
    /// File-backed implementation of <see cref="ISyncStateRepository"/>.
    /// <para>
    /// Encryption: <b>off</b>. Sync state (pts/qts/seq cursors, last-sync
    /// timestamp) is non-sensitive and explicitly classified outside the
    /// Critical/Sensitive bands of policy §2. Storing it plaintext also
    /// avoids a chicken-and-egg startup dependency on the data protector.
    /// </para>
    /// <para>
    /// Storage path: <c>LocalFolder/sync_state.bin</c>.
    /// </para>
    /// </summary>
    public sealed class JsonSyncStateRepository : ISyncStateRepository
    {
        private const string FileName = "sync_state.bin";

        private readonly IObjectStore<SyncStateRepositoryState> _store;

        public JsonSyncStateRepository()
        {
            _store = new JsonObjectStore<SyncStateRepositoryState>(FileName, false /* unencrypted */, null);
        }

        /// <summary>
        /// DI constructor: receives an already-configured object store
        /// (typically <see cref="Vianigram.Storage.Infrastructure.Sqlite.SqliteObjectStore{T}"/>).
        /// </summary>
        public JsonSyncStateRepository(IObjectStore<SyncStateRepositoryState> store)
        {
            if (store == null) throw new ArgumentNullException("store");
            _store = store;
        }

        public async Task<SyncStateRecord> LoadAsync(CancellationToken ct)
        {
            SyncStateRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            if (state == null || state.State == null) return new SyncStateRecord();
            return state.State;
        }

        public Task SaveAsync(SyncStateRecord state, CancellationToken ct)
        {
            if (state == null) throw new ArgumentNullException("state");
            SyncStateRepositoryState envelope = new SyncStateRepositoryState();
            envelope.State = state;
            return _store.SaveAsync(envelope, ct);
        }

        public Task ClearAsync(CancellationToken ct)
        {
            return _store.DeleteAsync(ct);
        }
    }
}
