// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Infrastructure
{
    /// <summary>
    /// In-memory repository: keeps every <see cref="SecretSession"/>
    /// aggregate in process memory keyed by <see cref="SecretChatId"/>,
    /// guarded by a private monitor.
    ///
    /// <para>Sufficient for cold-start, DH negotiation, send/receive, and
    /// UI consumption until the encrypted SQLite-backed repository in
    /// <c>Vianigram.Storage</c> is built. Hot-swap point:
    /// replace the binding in
    /// <see cref="Vianigram.SecretChats.Composition.SecretChatsCompositionRoot"/>
    /// with the persistent adapter and the application layer is unchanged.</para>
    ///
    /// <para>Thread-safety: all read/write paths take a lock on a private
    /// gate object. We hand back the live aggregate (NOT a copy) so handlers
    /// can mutate in place — the lock here only serializes the lookup
    /// transitions, not domain mutations. The application layer single-
    /// threads command handling per
    /// <see cref="Application.SecretChatsApplication"/>.</para>
    ///
    /// <para>Per-rule M3: this in-memory adapter holds the live
    /// <see cref="AuthKey"/> reference. A persistent adapter MUST route the
    /// auth_key through <c>ISecretCryptoPort</c>'s wrap/unwrap helpers and
    /// never write the raw bytes to disk — see
    /// <c>docs/managed-architecture/08-secret-chats.md §9</c>.</para>
    /// </summary>
    public sealed class InMemorySecretChatRepository : ISecretChatRepository
    {
        private readonly object _gate = new object();
        private readonly Dictionary<int, SecretSession> _sessions = new Dictionary<int, SecretSession>();

        public Task<SecretSession> FindAsync(SecretChatId chatId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                SecretSession s;
                _sessions.TryGetValue(chatId.Value, out s);
                return Task.FromResult(s);
            }
        }

        public Task SaveAsync(SecretSession session, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (session == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                _sessions[session.ChatId.Value] = session;
            }
            return Task.FromResult<object>(null);
        }

        public Task DeleteAsync(SecretChatId chatId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                SecretSession existing;
                if (_sessions.TryGetValue(chatId.Value, out existing))
                {
                    // Defense in depth: ensure the auth_key is wiped even if
                    // the aggregate hasn't been formally discarded.
                    existing.Discard(DiscardReason.LocalLogout, System.DateTime.UtcNow);
                    existing.DequeuePendingEvents(); // drop staged events; nothing to publish on Delete
                    _sessions.Remove(chatId.Value);
                }
            }
            return Task.FromResult<object>(null);
        }

        public Task<IList<SecretSession>> ListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                IList<SecretSession> snapshot = new List<SecretSession>(_sessions.Count);
                foreach (var kv in _sessions) snapshot.Add(kv.Value);
                return Task.FromResult(snapshot);
            }
        }
    }
}
