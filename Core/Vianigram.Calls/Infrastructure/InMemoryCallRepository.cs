// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Outbound;

namespace Vianigram.Calls.Infrastructure
{
    /// <summary>
    /// In-memory repository: keeps every <see cref="CallSession"/>
    /// aggregate in process memory keyed by <see cref="CallId"/>, guarded
    /// by a private monitor.
    ///
    /// <para>Sufficient for outbound/inbound signaling, the active-call
    /// invariant, and "Recent calls" UI consumption while a persistent
    /// adapter (SQLite or Messages-projection-based) is built. Hot-swap
    /// point: replace the binding in
    /// <see cref="Vianigram.Calls.Composition.CallsCompositionRoot"/> with
    /// the persistent adapter and the application layer is unchanged.</para>
    ///
    /// <para>Thread-safety: all read/write paths take a lock on a private
    /// gate object. We hand back the live aggregate (NOT a copy) so
    /// handlers can mutate in place — the lock here only serializes the
    /// lookup transitions, not domain mutations. The application layer
    /// single-threads command handling per
    /// <see cref="Application.CallsApplication"/>.</para>
    ///
    /// <para>Per the <c>docs/managed-architecture/07-calls.md §9</c>
    /// guidance, calls themselves are essentially ephemeral — the durable
    /// artifact for "call history" is a service message in
    /// <c>Vianigram.Messages</c>. This repository's job is hot-state for
    /// the live call and last-N quick-history; nothing here ever hits
    /// disk.</para>
    /// </summary>
    public sealed class InMemoryCallRepository : ICallRepository
    {
        private readonly object _gate = new object();
        private readonly Dictionary<long, CallSession> _sessions = new Dictionary<long, CallSession>();

        public Task<CallSession> FindAsync(CallId callId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                CallSession s;
                _sessions.TryGetValue(callId.Value, out s);
                return Task.FromResult(s);
            }
        }

        public Task SaveAsync(CallSession session, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (session == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                _sessions[session.CallId.Value] = session;
            }
            return Task.FromResult<object>(null);
        }

        public Task DeleteAsync(CallId callId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _sessions.Remove(callId.Value);
            }
            return Task.FromResult<object>(null);
        }

        public Task<IList<CallSession>> ListAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                IList<CallSession> snapshot = new List<CallSession>(_sessions.Count);
                foreach (var kv in _sessions) snapshot.Add(kv.Value);
                return Task.FromResult(snapshot);
            }
        }

        public Task<CallSession> FindActiveAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                foreach (var kv in _sessions)
                {
                    CallSession s = kv.Value;
                    if (s != null && !s.IsTerminal)
                    {
                        return Task.FromResult(s);
                    }
                }
                return Task.FromResult<CallSession>(null);
            }
        }
    }
}
