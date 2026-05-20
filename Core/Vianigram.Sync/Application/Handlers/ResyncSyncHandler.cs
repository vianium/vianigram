// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Sync.Application.Commands;
using Vianigram.Sync.Domain.Entities;
using Vianigram.Sync.Domain.Errors;
using Vianigram.Sync.Domain.ValueObjects;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Sync.Application.Handlers
{
    /// <summary>
    /// Discard the persisted cursor and re-bootstrap from scratch. Used on auth-key
    /// rotation, after the server returned updatesTooLong, or as a manual user-
    /// triggered "force resync" diagnostic.
    ///
    /// Implementation: clears the repository, resets the in-memory state to the
    /// initial cursor, then delegates to <see cref="BootstrapSyncHandler"/> for
    /// the cold-start path (updates.getState).
    /// </summary>
    public sealed class ResyncSyncHandler
    {
        private readonly SyncState _state;
        private readonly ISyncStateRepository _repo;
        private readonly BootstrapSyncHandler _bootstrap;

        public ResyncSyncHandler(SyncState state, ISyncStateRepository repo, BootstrapSyncHandler bootstrap)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bootstrap == null) throw new ArgumentNullException("bootstrap");
            _state = state;
            _repo = repo;
            _bootstrap = bootstrap;
        }

        public async Task<Result<Unit, SyncError>> HandleAsync(ResyncCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
            {
                return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Unknown, "null command"));
            }

            try
            {
                await _repo.ClearAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SyncError>.Fail(SyncError.From(ex, SyncError.CursorPersistFailure));
            }

            _state.Reseed(SyncCursor.Initial());
            return await _bootstrap.HandleAsync(BootstrapSyncCommand.Instance, ct).ConfigureAwait(false);
        }
    }
}
