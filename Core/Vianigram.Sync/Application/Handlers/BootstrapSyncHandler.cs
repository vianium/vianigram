// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Sync.Application.Commands;
using Vianigram.Sync.Domain.Entities;
using Vianigram.Sync.Domain.Errors;
using Vianigram.Sync.Domain.Events;
using Vianigram.Sync.Domain.ValueObjects;
using Vianigram.Sync.Infrastructure;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Sync.Application.Handlers
{
    /// <summary>
    /// Cold-start the sync engine.
    ///
    /// Flow:
    ///   1. Load the persisted SyncCursor (if any).
    ///   2. If the cursor is initial (cold start), call updates.getState to seed it.
    ///      Otherwise, call updates.getDifference to catch up everything missed
    ///      while the app was offline.
    ///   3. Apply the response (cursor advance for getState; full envelope fold
    ///      via SyncState.Apply for getDifference).
    ///   4. Persist the new cursor.
    ///   5. Publish <see cref="SyncReady"/>.
    ///
    /// All errors are translated to <see cref="SyncError"/>; nothing throws.
    /// </summary>
    public sealed class BootstrapSyncHandler
    {
        private readonly SyncState _state;
        private readonly ISyncStateRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public BootstrapSyncHandler(
            SyncState state,
            ISyncStateRepository repo,
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _state = state;
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(logger, "Sync.BootstrapSync");
            _clock = clock;
        }

        public async Task<Result<Unit, SyncError>> HandleAsync(BootstrapSyncCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
            {
                return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Unknown, "null command"));
            }

            try
            {
                // 1. Load persisted cursor.
                SyncCursor persisted;
                try
                {
                    persisted = await _repo.LoadCursorAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    return Result<Unit, SyncError>.Fail(SyncError.From(ex, SyncError.CursorLoadFailure));
                }

                if (persisted == null) persisted = SyncCursor.Initial();
                _state.Reseed(persisted);

                // 2. Decide getState vs getDifference.
                if (persisted.IsInitial)
                {
                    Result<Unit, SyncError> seedResult = await SeedViaGetStateAsync(ct).ConfigureAwait(false);
                    if (seedResult.IsFail) return seedResult;
                }
                else
                {
                    Result<Unit, SyncError> diffResult = await CatchUpViaGetDifferenceAsync(ct).ConfigureAwait(false);
                    if (diffResult.IsFail) return diffResult;
                }

                // 4. Persist the (possibly new) cursor.
                try
                {
                    await _repo.SaveCursorAsync(_state.Common, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Warn("sync.bootstrap: cursor save failed: " + ex.Message);
                    // Non-fatal; the in-memory state is still correct for this session.
                }

                // 5. Publish SyncReady.
                _bus.Publish(new SyncReady(_clock.UtcNow));
                return Result<Unit, SyncError>.Ok(Unit.Value);
            }
            catch (OperationCanceledException)
            {
                return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Cancelled, "bootstrap cancelled"));
            }
            catch (Exception ex)
            {
                return Result<Unit, SyncError>.Fail(SyncError.From(ex, SyncError.Unknown));
            }
        }

        private async Task<Result<Unit, SyncError>> SeedViaGetStateAsync(CancellationToken ct)
        {
            byte[] body = TlEncoder.EncodeGetState();
            Result<byte[], MtProtoRpcError> rpcResult =
                await _rpc.InvokeAsync(body, "updates.getState", ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                return Result<Unit, SyncError>.Fail(MapRpcError(rpcResult.Error, "updates.getState"));
            }

            SyncCursor seeded = TlDecoder.DecodeUpdatesState(rpcResult.Value);
            if (seeded == null)
            {
                return Result<Unit, SyncError>.Fail(
                    SyncError.Make(SyncError.TlDecodeFailure, "updates.State decode returned null"));
            }
            _state.Reseed(seeded);
            return Result<Unit, SyncError>.Ok(Unit.Value);
        }

        /// <summary>
        /// Exposed to SyncApplication so a runtime gap signal
        /// (ApplyResult.NeedsGetDifference) can self-heal by calling
        /// updates.getDifference. Coalescing is the caller's responsibility
        /// — see SyncApplication for the in-flight flag.
        /// </summary>
        internal async Task<Result<Unit, SyncError>> CatchUpViaGetDifferenceAsync(CancellationToken ct)
        {
            // getDifference is encoded but the response body decoding for
            // updates.differenceXxx (full state fold across messages, chats,
            // users, other_updates) is intentionally minimal. The handler:
            //   - Issues the call.
            //   - On differenceTooLong/differenceEmpty, treats as "cursor was reset
            //     by server" or "nothing to catch up".
            //   - On a slice / full difference, decodes only the cursor advance
            //     (state.* tail) for now. Other_updates folding lands later
            //     once the message-typed decoders are tightened.
            byte[] body = TlEncoder.EncodeGetDifference(
                pts: _state.Common.Pts,
                date: _state.Common.Date,
                qts: _state.Common.Qts,
                ptsTotalLimit: null);

            Result<byte[], MtProtoRpcError> rpcResult =
                await _rpc.InvokeAsync(body, "updates.getDifference", ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                MtProtoRpcError err = rpcResult.Error;
                // AUTH_KEY_UNREGISTERED → caller must re-bootstrap from scratch via Resync.
                if (err != null && err.Message != null &&
                    err.Message.IndexOf("AUTH_KEY_UNREGISTERED", StringComparison.Ordinal) >= 0)
                {
                    return Result<Unit, SyncError>.Fail(
                        SyncError.Make(SyncError.AuthRequired, err.Message));
                }
                return Result<Unit, SyncError>.Fail(MapRpcError(err, "updates.getDifference"));
            }

            // For now we only inspect the leading constructor id to
            // determine whether the server sent us a "tooLong" signal that requires
            // a full reseed. Full updates.difference fold lands when the difference
            // decoders are filled in — see the deferral note above.
            byte[] respBody = rpcResult.Value;
            if (respBody != null && respBody.Length >= 4)
            {
                int p = 0;
                uint ctor = TlDecoder.ReadUInt32(respBody, ref p);
                if (ctor == TlDecoder.UpdatesDifferenceTooLongId)
                {
                    // Server says our cursor is hopelessly stale — drop everything,
                    // re-bootstrap. We surface this as "auth-not-required, but you
                    // should call ResyncAsync next."
                    int newPts = TlDecoder.ReadInt32Safe(respBody, ref p);
                    _state.Reseed(_state.Common.WithPts(newPts));
                }
                // Other branches (differenceEmpty, difference, differenceSlice) leave
                // the cursor as-is for now; the next pushed update or explicit Resync
                // will close the loop. This is the documented deferral.
            }

            return Result<Unit, SyncError>.Ok(Unit.Value);
        }

        internal static SyncError MapRpcError(MtProtoRpcError err, string method)
        {
            if (err == null)
            {
                return SyncError.Make(SyncError.TransportFailure, method + " returned null error");
            }
            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase) ||
                message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int wait = err.Parameter;
                if (wait <= 0 && message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
                {
                    int parsed;
                    if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out parsed)) wait = parsed;
                }
                return SyncError.Flood(wait, method);
            }

            if (string.Equals(kind, "AuthRestart", StringComparison.OrdinalIgnoreCase) ||
                message.IndexOf("AUTH_KEY_UNREGISTERED", StringComparison.Ordinal) >= 0 ||
                message.IndexOf("SESSION_REVOKED", StringComparison.Ordinal) >= 0)
            {
                return SyncError.Make(SyncError.AuthRequired, method + ": " + message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return SyncError.Make(SyncError.TransportFailure, method + ": " + message);
            }

            return SyncError.Make(SyncError.TransportFailure,
                method + " kind=" + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
