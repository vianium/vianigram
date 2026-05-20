// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Outbound;

namespace Vianigram.Chats.Infrastructure
{
    /// <summary>
    /// In-memory repository: keeps all dialogs in a process-local
    /// <see cref="Dictionary{TKey, TValue}"/> guarded by a single monitor.
    ///
    /// Sufficient for cold-start, sync, and UI consumption while the SQLite-backed
    /// repository in <c>Vianigram.Storage</c> is built. Hot-swap point: replace
    /// the binding in <see cref="Vianigram.Chats.Composition.ChatsCompositionRoot"/>
    /// with the persistent adapter and the application layer is unchanged.
    ///
    /// Thread-safety: all read/write paths take a lock on a private gate object.
    /// We intentionally avoid <c>ConcurrentDictionary</c> here so iteration in
    /// <see cref="GetAllAsync"/> can return a stable snapshot under the same lock.
    /// </summary>
    public sealed class InMemoryDialogRepository : IDialogRepository
    {
        private readonly object _gate = new object();
        private readonly Dictionary<PeerId, Dialog> _store = new Dictionary<PeerId, Dialog>();

        public Task<Dialog> GetAsync(PeerId peer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                Dialog d;
                _store.TryGetValue(peer, out d);
                return Task.FromResult(d);
            }
        }

        public Task<IList<Dialog>> GetAllAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                var list = new List<Dialog>(_store.Count);
                foreach (var kv in _store) list.Add(kv.Value);
                return Task.FromResult<IList<Dialog>>(list);
            }
        }

        public Task UpsertAsync(Dialog dialog, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (dialog == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                _store[dialog.Peer] = dialog;
            }
            return Task.FromResult<object>(null);
        }

        public Task DeleteAsync(PeerId peer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (peer == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                _store.Remove(peer);
            }
            return Task.FromResult<object>(null);
        }

        public Task UpsertManyAsync(IEnumerable<Dialog> dialogs, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (dialogs == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                foreach (var d in dialogs)
                {
                    if (d == null) continue;
                    _store[d.Peer] = d;
                }
            }
            return Task.FromResult<object>(null);
        }
    }
}
