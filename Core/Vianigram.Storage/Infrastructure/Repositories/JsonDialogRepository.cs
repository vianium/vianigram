// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Storage.Application;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Repositories
{
    /// <summary>
    /// File-backed implementation of <see cref="IDialogRepository"/>.
    /// <para>
    /// Encryption: <b>off by default</b> (class <i>Personal</i> per policy §2).
    /// Constructor overload accepts a <see cref="IDataProtector"/> and an
    /// <c>encrypted</c> flag so the Local Passcode flow can flip it on
    /// without changing call sites.
    /// </para>
    /// <para>
    /// Storage path: <c>LocalFolder/dialogs.bin</c>.
    /// </para>
    /// </summary>
    public sealed class JsonDialogRepository : IDialogRepository
    {
        private const string FileName = "dialogs.bin";

        private readonly IObjectStore<DialogRepositoryState> _store;
        private readonly object _gate = new object();

        /// <summary>Plaintext default (JSON-on-disk).</summary>
        public JsonDialogRepository()
            : this(false, null)
        {
        }

        /// <summary>
        /// Encrypted variant for Local Passcode mode. When
        /// <paramref name="encrypted"/> is <c>true</c>, <paramref name="protector"/>
        /// must be non-null. JSON-on-disk.
        /// </summary>
        public JsonDialogRepository(bool encrypted, IDataProtector protector)
        {
            _store = new JsonObjectStore<DialogRepositoryState>(FileName, encrypted, protector);
        }

        /// <summary>
        /// DI constructor: receives an already-configured object store
        /// (typically <see cref="Vianigram.Storage.Infrastructure.Sqlite.SqliteObjectStore{T}"/>).
        /// </summary>
        public JsonDialogRepository(IObjectStore<DialogRepositoryState> store)
        {
            if (store == null) throw new ArgumentNullException("store");
            _store = store;
        }

        public async Task<IList<DialogRecord>> ListAsync(CancellationToken ct)
        {
            DialogRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (state == null || state.Dialogs == null) return new List<DialogRecord>();
                return new List<DialogRecord>(state.Dialogs);
            }
        }

        public async Task<DialogRecord> GetAsync(long peerId, CancellationToken ct)
        {
            DialogRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (state == null || state.Dialogs == null) return null;
                for (int i = 0; i < state.Dialogs.Count; i++)
                {
                    DialogRecord d = state.Dialogs[i];
                    if (d != null && d.PeerId == peerId) return d;
                }
                return null;
            }
        }

        public async Task UpsertAsync(DialogRecord dialog, CancellationToken ct)
        {
            if (dialog == null) throw new ArgumentNullException("dialog");

            DialogRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (state.Dialogs == null) state.Dialogs = new List<DialogRecord>();
                bool replaced = false;
                for (int i = 0; i < state.Dialogs.Count; i++)
                {
                    if (state.Dialogs[i] != null && state.Dialogs[i].PeerId == dialog.PeerId)
                    {
                        state.Dialogs[i] = dialog;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) state.Dialogs.Add(dialog);
            }
            await _store.SaveAsync(state, ct).ConfigureAwait(false);
        }

        public async Task DeleteAsync(long peerId, CancellationToken ct)
        {
            DialogRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            bool removed = false;
            lock (_gate)
            {
                if (state != null && state.Dialogs != null)
                {
                    for (int i = 0; i < state.Dialogs.Count; i++)
                    {
                        if (state.Dialogs[i] != null && state.Dialogs[i].PeerId == peerId)
                        {
                            state.Dialogs.RemoveAt(i);
                            removed = true;
                            break;
                        }
                    }
                }
            }
            if (removed)
            {
                await _store.SaveAsync(state, ct).ConfigureAwait(false);
            }
        }

        public Task ClearAsync(CancellationToken ct)
        {
            return _store.DeleteAsync(ct);
        }
    }
}
