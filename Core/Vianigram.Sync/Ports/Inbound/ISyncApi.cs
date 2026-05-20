// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Sync.Domain.Errors;
using Vianigram.Sync.Domain.ValueObjects;

namespace Vianigram.Sync.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Sync bounded context.
    ///
    /// Per principle M6, this interface DOES NOT expose pts/qts/seq/date directly.
    /// Callers may inspect the cursor for diagnostics only — no other context may
    /// drive it. The cross-context contract is the IDomainEvent stream emitted
    /// onto the kernel IEventBus (see <see cref="UpdatesApplied"/> notification).
    /// </summary>
    public interface ISyncApi
    {
        /// <summary>
        /// Cold-start the sync engine: load persisted cursor, call updates.getState
        /// if cursor is empty, call updates.getDifference to catch up if not.
        /// Idempotent: a second BootstrapAsync after success is a no-op.
        /// Must be called before <see cref="ResyncAsync"/> or before consuming
        /// derived events meaningfully.
        /// </summary>
        Task<Result<Unit, SyncError>> BootstrapAsync(CancellationToken ct);

        /// <summary>
        /// Discard the current cursor and re-bootstrap. Used on auth-key rotation,
        /// after long suspension, or when the server returns updatesTooLong.
        /// </summary>
        Task<Result<Unit, SyncError>> ResyncAsync(CancellationToken ct);

        /// <summary>
        /// Acknowledge a raw updates.getDifference response fetched by a narrow
        /// backup poller. Sync remains the only cursor writer: implementations
        /// must validate that the response contains no unhandled message payload
        /// before advancing the common cursor.
        /// </summary>
        Task<Result<Unit, SyncError>> AcknowledgePolledDifferenceAsync(
            byte[] rawDifferenceBody,
            int handledOtherUpdatesCount,
            CancellationToken ct);

        /// <summary>
        /// Read-only snapshot of the cursor for diagnostics. Always returns a
        /// non-null value — defaults to <see cref="SyncCursor.Initial"/> before
        /// bootstrap.
        /// </summary>
        SyncCursor CurrentCursor { get; }

        /// <summary>
        /// True after BootstrapAsync has completed and there is no outstanding
        /// gap-fill request in flight.
        /// </summary>
        bool IsCaughtUp { get; }

        /// <summary>
        /// Fired after each UpdatesEnvelope is folded into SyncState. Subscribers
        /// receive the count of derived events emitted; the actual derived events
        /// flow on IEventBus.
        /// </summary>
        event EventHandler<UpdatesAppliedEventArgs> UpdatesApplied;
    }
}
