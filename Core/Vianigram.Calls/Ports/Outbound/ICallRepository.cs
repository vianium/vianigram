// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Ports.Outbound
{
    /// <summary>
    /// Outbound port for persisting <see cref="CallSession"/> aggregates.
    /// The current implementation is in-memory
    /// (<see cref="Vianigram.Calls.Infrastructure.InMemoryCallRepository"/>).
    /// The "Recent calls" UI surface reads through this same port.
    ///
    /// <para>Per <c>docs/managed-architecture/07-calls.md §9</c> calls
    /// themselves are essentially ephemeral; the durable artifact is a
    /// service message in the relevant 1:1 dialog (handled by
    /// <c>Vianigram.Messages</c>). The repository here is for in-process
    /// state — multi-step DH, retries, and last-N-call quick-history.
    /// A future revision may upgrade to SQLite-backed storage to retain a
    /// deeper "Recent calls" projection across cold starts.</para>
    ///
    /// <para>All operations are async to keep the storage swap painless
    /// even though the in-memory implementation completes synchronously
    /// today. Implementations MUST be thread-safe.</para>
    /// </summary>
    public interface ICallRepository
    {
        /// <summary>
        /// Returns the session keyed by <paramref name="callId"/>, or null
        /// if none. Returning the live aggregate is encouraged (handlers
        /// mutate in place); storage adapters that re-hydrate per-call are
        /// also valid as long as <see cref="SaveAsync"/> commits the same
        /// reference.
        /// </summary>
        Task<CallSession> FindAsync(CallId callId, CancellationToken ct);

        /// <summary>
        /// Persist (insert or update) the supplied aggregate.
        /// </summary>
        Task SaveAsync(CallSession session, CancellationToken ct);

        /// <summary>
        /// Wipe the row for <paramref name="callId"/>. Idempotent.
        /// </summary>
        Task DeleteAsync(CallId callId, CancellationToken ct);

        /// <summary>
        /// Snapshot of every session currently persisted. Used by the
        /// Presentation layer for the "Recent calls" sidebar and at startup
        /// to detect orphaned aggregates that need
        /// <see cref="DiscardReason.LocalShutdown"/>. Order is
        /// implementation-defined.
        /// </summary>
        Task<IList<CallSession>> ListAsync(CancellationToken ct);

        /// <summary>
        /// Convenience helper enforcing the "one active call per device"
        /// invariant — returns the first non-Discarded session if any, else
        /// null. The application layer calls this before
        /// <see cref="Vianigram.Calls.Domain.Entities.CallSession.StartOutgoing"/>
        /// to fail-fast with <see cref="Domain.CallErrorKind.AlreadyInCall"/>.
        /// </summary>
        Task<CallSession> FindActiveAsync(CancellationToken ct);
    }
}
