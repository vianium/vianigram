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
    /// File-backed implementation of <see cref="IMessageRepository"/>.
    /// <para>
    /// Encryption: <b>off by default</b>. Constructor supports an opt-in
    /// encrypted variant for Local Passcode mode (class <i>Bulk</i>
    /// per policy §2). With the JSON backend the entire history is
    /// loaded into memory on every read — use the SQLite repository for
    /// production-scale dialog history.
    /// </para>
    /// <para>
    /// Storage path: <c>LocalFolder/messages.bin</c>.
    /// </para>
    /// </summary>
    public sealed class JsonMessageRepository : IMessageRepository
    {
        private const string FileName = "messages.bin";

        private readonly IObjectStore<MessageRepositoryState> _store;
        private readonly object _gate = new object();

        public JsonMessageRepository()
            : this(false, null)
        {
        }

        public JsonMessageRepository(bool encrypted, IDataProtector protector)
        {
            _store = new JsonObjectStore<MessageRepositoryState>(FileName, encrypted, protector);
        }

        /// <summary>
        /// DI constructor: receives an already-configured object store
        /// (typically <see cref="Vianigram.Storage.Infrastructure.Sqlite.SqliteObjectStore{T}"/>).
        /// </summary>
        public JsonMessageRepository(IObjectStore<MessageRepositoryState> store)
        {
            if (store == null) throw new ArgumentNullException("store");
            _store = store;
        }

        public async Task<IList<MessageRecord>> ListAsync(long peerId, int beforeMessageId, int limit, CancellationToken ct)
        {
            if (limit <= 0) return new List<MessageRecord>();

            MessageRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            List<MessageRecord> results = new List<MessageRecord>();
            lock (_gate)
            {
                if (state == null || state.Messages == null) return results;

                // Collect peer messages strictly older than beforeMessageId
                // (or every message if beforeMessageId <= 0).
                List<MessageRecord> matches = new List<MessageRecord>();
                foreach (MessageRecord m in state.Messages)
                {
                    if (m == null || m.PeerId != peerId) continue;
                    if (beforeMessageId > 0 && m.MessageId >= beforeMessageId) continue;
                    matches.Add(m);
                }

                // Sort by MessageId descending using Comparison<T> (no LINQ in WP8.1 hot path).
                matches.Sort(CompareByMessageIdDesc);

                int take = matches.Count < limit ? matches.Count : limit;
                for (int i = 0; i < take; i++) results.Add(matches[i]);
            }
            return results;
        }

        public async Task<MessageRecord> GetAsync(long peerId, int messageId, CancellationToken ct)
        {
            MessageRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (state == null || state.Messages == null) return null;
                foreach (MessageRecord m in state.Messages)
                {
                    if (m != null && m.PeerId == peerId && m.MessageId == messageId) return m;
                }
                return null;
            }
        }

        public async Task UpsertAsync(MessageRecord message, CancellationToken ct)
        {
            if (message == null) throw new ArgumentNullException("message");

            MessageRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            lock (_gate)
            {
                if (state.Messages == null) state.Messages = new List<MessageRecord>();
                bool replaced = false;
                for (int i = 0; i < state.Messages.Count; i++)
                {
                    MessageRecord existing = state.Messages[i];
                    if (existing != null && existing.PeerId == message.PeerId && existing.MessageId == message.MessageId)
                    {
                        state.Messages[i] = message;
                        replaced = true;
                        break;
                    }
                }
                if (!replaced) state.Messages.Add(message);
            }
            await _store.SaveAsync(state, ct).ConfigureAwait(false);
        }

        public async Task DeleteAsync(long peerId, int messageId, CancellationToken ct)
        {
            MessageRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            bool changed = false;
            lock (_gate)
            {
                if (state != null && state.Messages != null)
                {
                    for (int i = 0; i < state.Messages.Count; i++)
                    {
                        MessageRecord m = state.Messages[i];
                        if (m != null && m.PeerId == peerId && m.MessageId == messageId)
                        {
                            state.Messages.RemoveAt(i);
                            changed = true;
                            break;
                        }
                    }
                }
            }
            if (changed) await _store.SaveAsync(state, ct).ConfigureAwait(false);
        }

        public async Task DeleteByPeerAsync(long peerId, CancellationToken ct)
        {
            MessageRepositoryState state = await _store.LoadAsync(ct).ConfigureAwait(false);
            bool changed = false;
            lock (_gate)
            {
                if (state != null && state.Messages != null)
                {
                    for (int i = state.Messages.Count - 1; i >= 0; i--)
                    {
                        MessageRecord m = state.Messages[i];
                        if (m != null && m.PeerId == peerId)
                        {
                            state.Messages.RemoveAt(i);
                            changed = true;
                        }
                    }
                }
            }
            if (changed) await _store.SaveAsync(state, ct).ConfigureAwait(false);
        }

        public Task ClearAsync(CancellationToken ct)
        {
            return _store.DeleteAsync(ct);
        }

        private static int CompareByMessageIdDesc(MessageRecord a, MessageRecord b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return b.MessageId.CompareTo(a.MessageId);
        }
    }
}
