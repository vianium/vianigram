// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Sync.Application.Commands;
using Vianigram.Sync.Application.Handlers;
using Vianigram.Sync.Domain.Entities;
using Vianigram.Sync.Domain.Errors;
using Vianigram.Sync.Domain.Events;
using Vianigram.Sync.Domain.ValueObjects;
using Vianigram.Sync.Infrastructure;
using Vianigram.Sync.Ports.Inbound;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Sync.Application
{
    /// <summary>
    /// <see cref="ISyncApi"/> implementation. Owns the in-memory <see cref="SyncState"/>
    /// aggregate, the four command handlers, the <see cref="IUpdatesPort"/>
    /// subscription, and the bus-to-CLR-event bridge for <see cref="UpdatesApplied"/>.
    ///
    /// Threading model:
    /// - <see cref="BootstrapAsync"/> / <see cref="ResyncAsync"/> are caller-driven.
    /// - The IUpdatesPort delivers raw bytes; we serialize their application via a
    ///   single <see cref="SemaphoreSlim"/> so derived events publish in order.
    ///   That dispatcher is the *only* writer to <see cref="SyncState"/>.
    ///
    /// Cancellation:
    /// - The application creates its own <see cref="CancellationTokenSource"/>
    ///   (linked with the caller's tokens) so logout / suspend can stop the loop
    ///   via <see cref="Dispose"/>.
    /// </summary>
    public sealed class SyncApplication : ISyncApi, IDisposable
    {
        private readonly SyncState _state;
        private readonly ISyncStateRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IUpdatesPort _updatesPort;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        private readonly BootstrapSyncHandler _bootstrap;
        private readonly ResyncSyncHandler _resync;
        private readonly ProcessUpdatesHandler _process;
        private readonly GetChannelDifferenceHandler _channelDiff;

        // Single-dispatcher queue for ProcessUpdates calls. We use a SemaphoreSlim
        // (initialCount=1) instead of a full queue because back-pressure on the
        // updates path is fine — bursts during reconnect are handled by
        // getDifference, not by buffering.
        private readonly SemaphoreSlim _applyGate = new SemaphoreSlim(1, 1);

        // Coalescing flag for ApplyResult.NeedsGetDifference.
        // Without this every push that lands in a gap window would fire its
        // own getDifference, multiplying the round-trip count and the
        // server pressure. We set the flag to 1 when a catch-up starts and
        // back to 0 when it returns; concurrent gap signals during a
        // running catch-up just no-op (the running catch-up already covers
        // their gap).
        private int _getDifferenceInFlight;

        private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();

        private readonly IDisposable _subUpdatesApplied;
        private IDisposable _updatesSubscription; // assigned in BootstrapAsync

        private bool _bootstrapped;
        private int _disposed; // 0 = alive, 1 = disposed (Interlocked-guarded)

        public event EventHandler<UpdatesAppliedEventArgs> UpdatesApplied;

        // Optional delegate the composition root supplies so SyncApplication
        // can resolve a channel's access_hash before issuing
        // updates.getChannelDifference. Without this, the call goes
        // out with access_hash=0 and the server returns
        // CHANNEL_INVALID — channel cursors never recover from gaps,
        // so messages from those channels disappear permanently.
        // Returns 0 when the channel hasn't been observed yet
        // (caller falls back to the legacy behaviour, which is
        // best-effort and may still fail gracefully).
        private readonly Func<long, long> _channelAccessHashResolver;

        public SyncApplication(
            IMtProtoRpcPort rpc,
            IUpdatesPort updatesPort,
            ISyncStateRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock)
            : this(rpc, updatesPort, repo, bus, logger, clock, channelAccessHashResolver: null)
        {
        }

        public SyncApplication(
            IMtProtoRpcPort rpc,
            IUpdatesPort updatesPort,
            ISyncStateRepository repo,
            IEventBus bus,
            ILogger logger,
            IClock clock,
            Func<long, long> channelAccessHashResolver)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (updatesPort == null) throw new ArgumentNullException("updatesPort");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _rpc = rpc;
            _updatesPort = updatesPort;
            _repo = repo;
            _bus = bus;
            _log = new TimestampedLogger(logger, "Sync.Application");
            _clock = clock;
            _channelAccessHashResolver = channelAccessHashResolver;

            _state = new SyncState(clock);
            _bootstrap = new BootstrapSyncHandler(_state, repo, rpc, bus, logger, clock);
            _resync = new ResyncSyncHandler(_state, repo, _bootstrap);
            _process = new ProcessUpdatesHandler(_state, repo, bus, logger, clock);
            // Pass the bus so the handler can emit RemoteMessageReceived
            // for each channel message in the diff response. Without this,
            // channels stay silent because the cursor advances but messages
            // never reach the bus.
            _channelDiff = new GetChannelDifferenceHandler(_state, repo, rpc, bus, logger, clock);

            // Bus → CLR event bridge for UpdatesApplied.
            _subUpdatesApplied = _bus.Subscribe<UpdatesApplied>(OnUpdatesApplied);
        }

        // ---- ISyncApi ----------------------------------------------------------

        public SyncCursor CurrentCursor
        {
            get { return _state.Common; }
        }

        public bool IsCaughtUp
        {
            get { return _bootstrapped; }
        }

        public async Task<Result<Unit, SyncError>> BootstrapAsync(CancellationToken ct)
        {
            ThrowIfDisposed();

            if (_bootstrapped)
            {
                // Idempotent — second call is a no-op.
                return Result<Unit, SyncError>.Ok(Unit.Value);
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token))
            {
                Result<Unit, SyncError> r;
                try
                {
                    r = await _bootstrap.HandleAsync(BootstrapSyncCommand.Instance, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Cancelled, "bootstrap cancelled"));
                }

                if (r.IsOk)
                {
                    _bootstrapped = true;
                    SubscribeUpdatesPort();
                }
                return r;
            }
        }

        public async Task<Result<Unit, SyncError>> ResyncAsync(CancellationToken ct)
        {
            ThrowIfDisposed();

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token))
            {
                _bootstrapped = false;

                Result<Unit, SyncError> r;
                try
                {
                    r = await _resync.HandleAsync(ResyncCommand.Instance, linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Cancelled, "resync cancelled"));
                }

                if (r.IsOk)
                {
                    _bootstrapped = true;
                    SubscribeUpdatesPort();
                }
                return r;
            }
        }

        public async Task<Result<Unit, SyncError>> AcknowledgePolledDifferenceAsync(
            byte[] rawDifferenceBody,
            int handledOtherUpdatesCount,
            CancellationToken ct)
        {
            ThrowIfDisposed();

            if (rawDifferenceBody == null || rawDifferenceBody.Length == 0)
            {
                return Result<Unit, SyncError>.Fail(
                    SyncError.Make(SyncError.TlDecodeFailure, "empty polled difference body"));
            }
            if (handledOtherUpdatesCount < 0)
            {
                return Result<Unit, SyncError>.Fail(
                    SyncError.Make(SyncError.Unknown, "handledOtherUpdatesCount must be non-negative"));
            }

            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _shutdownCts.Token))
            {
                try
                {
                    await _applyGate.WaitAsync(linked.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Cancelled, "difference ack cancelled"));
                }

                try
                {
                    // Before deciding whether to ack the cursor, decode and
                    // apply every Update sitting in the diff response's
                    // `other_updates` vector. Channel messages
                    // (updateNewChannelMessage, updateEditChannelMessage,
                    // etc.) only arrive via this path for inactive
                    // sessions — the live push stream skips them.
                    int additionalHandled = TryApplyOtherUpdatesFromDifference(rawDifferenceBody);
                    int totalHandled = handledOtherUpdatesCount + additionalHandled;

                    SyncCursor next;
                    string reason;
                    bool canAdvance = TlDecoder.TryDecodeSafeDifferenceCursor(
                        rawDifferenceBody,
                        totalHandled,
                        _state.Common,
                        out next,
                        out reason);

                    // Byte-scan path: when the structural decode
                    // (TryDecodeSafeDifferenceCursor) can't navigate past
                    // `new_messages` (e.g.
                    // it contains regular messages our skip routine
                    // doesn't handle) but the byte-scan DID apply
                    // updates, force-advance using the trailing state.
                    // Without this the same diff body comes back every
                    // poll, byte-scan re-applies the same updates, and
                    // the user sees duplicate notifications.
                    if (!canAdvance && additionalHandled > 0)
                    {
                        SyncCursor forced;
                        if (TlDecoder.TryDecodeTrailingDifferenceState(rawDifferenceBody, out forced))
                        {
                            _log.Info("sync.diff-ack forced via trailing state " +
                                "(byte-scan applied=" + additionalHandled + ", " +
                                "structural reason=" + reason + ")");
                            next = forced;
                            canAdvance = true;
                        }
                    }

                    if (!canAdvance)
                    {
                        _log.Info("sync.diff-ack skipped: " + reason +
                            " (additional=" + additionalHandled + ")");
                        return Result<Unit, SyncError>.Ok(Unit.Value);
                    }

                    if (!_state.Common.Equals(next))
                    {
                        SyncCursor prior = _state.Common;
                        _state.Reseed(next);
                        try
                        {
                            await _repo.SaveCursorAsync(_state.Common, linked.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            _log.Warn("sync.diff-ack: cursor save failed: " + ex.Message);
                        }

                        _log.Info("sync.diff-ack advanced " + prior + " -> " + _state.Common);
                    }
                    else
                    {
                        _log.Info("sync.diff-ack unchanged " + _state.Common);
                    }

                    return Result<Unit, SyncError>.Ok(Unit.Value);
                }
                catch (OperationCanceledException)
                {
                    return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Cancelled, "difference ack cancelled"));
                }
                catch (Exception ex)
                {
                    return Result<Unit, SyncError>.Fail(SyncError.From(ex, SyncError.Unknown));
                }
                finally
                {
                    _applyGate.Release();
                }
            }
        }

        /// <summary>
        /// Walk the <c>other_updates:Vector&lt;Update&gt;</c> field of an
        /// <c>updates.difference</c> / <c>differenceSlice</c> response
        /// and apply every entry through SyncState. Returns the number
        /// of updates we successfully decoded so the caller can
        /// reconcile against TryDecodeSafeDifferenceCursor's count.
        ///
        /// Caller MUST already hold <c>_applyGate</c> (we're called
        /// from inside <see cref="AcknowledgePolledDifferenceAsync"/>'s
        /// guarded section — re-acquiring would deadlock).
        ///
        /// On any decode hiccup we return what we managed to apply
        /// rather than failing the whole ack — partial progress is
        /// better than none. The caller's safe-cursor check enforces
        /// "all-or-nothing" cursor advance, so partial application
        /// just means the cursor stays where it is for now and the
        /// next poll re-fetches the same data — idempotent.
        /// </summary>
        private int TryApplyOtherUpdatesFromDifference(byte[] rawDifferenceBody)
        {
            if (rawDifferenceBody == null || rawDifferenceBody.Length < 4) return 0;

            // Path A: structural navigation (fast, exact). Works when
            // new_messages contains 0 entries or only phoneCall service
            // messages — the only Message shapes we know how to skip.
            IList<Update> updates = null;
            int p;
            if (TlDecoder.TryAdvanceToOtherUpdates(rawDifferenceBody, out p))
            {
                updates = TlDecoder.TryDecodeOtherUpdatesVector(rawDifferenceBody, ref p);
            }

            // Path B: byte-scan fallback. When new_messages contains
            // regular messages (channel posts, DMs in basic groups,
            // etc.) we can't navigate past them without a full Message
            // skip routine. Scan the body for known Update ctor IDs
            // instead — each match is validated by attempting a strict
            // structural decode, and matches are de-duplicated by
            // (ctor + message-id) so false-positive byte alignments
            // don't double-apply.
            if (updates == null || updates.Count == 0)
            {
                updates = TlDecoder.ScanAndDecodeUpdatesInBody(rawDifferenceBody);
                if (updates != null && updates.Count > 0)
                {
                    _log.Info("sync.diff-other byte-scan found=" + updates.Count);
                }
            }

            // Path C: raw-Message scan. Channel posts and most diff
            // messages live as bare Message ctors inside
            // new_messages:Vector<Message>, NOT wrapped in
            // updateNewChannelMessage. The Update-ctor scan misses
            // them entirely. ScanAndDecodeMessagesInBody finds them
            // and synthesizes UpdateNewChannelMessage / UpdateNewMessage
            // with pts=0 so SyncState emits RemoteMessageReceived
            // without touching the cursor (which is already updated
            // by the surrounding state-trailing decode).
            IList<Update> rawMsgUpdates = TlDecoder.ScanAndDecodeMessagesInBody(rawDifferenceBody);
            if (rawMsgUpdates != null && rawMsgUpdates.Count > 0)
            {
                _log.Info("sync.diff-other raw-msg-scan found=" + rawMsgUpdates.Count);
                if (updates == null) updates = new List<Update>(rawMsgUpdates.Count);
                for (int k = 0; k < rawMsgUpdates.Count; k++) updates.Add(rawMsgUpdates[k]);
            }

            if (updates == null || updates.Count == 0) return 0;

            int applied = 0;
            for (int i = 0; i < updates.Count; i++)
            {
                Update u = updates[i];
                if (u == null) continue;
                try
                {
                    // Wrap the single update into an envelope shape that
                    // SyncState.Apply consumes. Use UpdatesEnvelopeShort
                    // which is exactly { update, date }.
                    var envelope = new UpdatesEnvelopeShort(u, _state.Common.Date);
                    ApplyResult result = _state.Apply(envelope);
                    if (result != null && result.Events != null)
                    {
                        for (int j = 0; j < result.Events.Count; j++)
                        {
                            try { PublishDynamicByType(result.Events[j]); }
                            catch (Exception pubEx)
                            {
                                _log.Warn("sync.diff-other.publish " +
                                    pubEx.GetType().Name + ": " + pubEx.Message);
                            }
                        }
                    }
                    applied++;
                }
                catch (Exception ex)
                {
                    _log.Warn("sync.diff-other.apply " + u.GetType().Name +
                        " threw " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            _log.Info("sync.diff-other applied=" + applied + " of=" + updates.Count);
            return applied;
        }

        // Mirror of ProcessUpdatesHandler.PublishDynamic — couldn't
        // reuse directly because that method is private. Cast each
        // event to its concrete type and publish via the bus.
        private void PublishDynamicByType(IDomainEvent ev)
        {
            if (ev == null) return;
            RemoteMessageReceived rmr = ev as RemoteMessageReceived;
            if (rmr != null) { _bus.Publish(rmr); return; }
            RemoteMessageEdited rme = ev as RemoteMessageEdited;
            if (rme != null) { _bus.Publish(rme); return; }
            RemoteMessageDeleted rmd = ev as RemoteMessageDeleted;
            if (rmd != null) { _bus.Publish(rmd); return; }
            RemoteMessageRead rrd = ev as RemoteMessageRead;
            if (rrd != null) { _bus.Publish(rrd); return; }
            RemoteUserStatusChanged rus = ev as RemoteUserStatusChanged;
            if (rus != null) { _bus.Publish(rus); return; }
            RemoteUserTypingChanged rut = ev as RemoteUserTypingChanged;
            if (rut != null) { _bus.Publish(rut); return; }
            RemoteMessageIdAssigned rmi = ev as RemoteMessageIdAssigned;
            if (rmi != null) { _bus.Publish(rmi); return; }
            RemoteUserNameChanged run = ev as RemoteUserNameChanged;
            if (run != null) { _bus.Publish(run); return; }
            RemoteUserPhoneChanged rup = ev as RemoteUserPhoneChanged;
            if (rup != null) { _bus.Publish(rup); return; }
            RemoteUserPhotoChanged rupo = ev as RemoteUserPhotoChanged;
            if (rupo != null) { _bus.Publish(rupo); return; }
            RemoteNotifySettingsChanged rns = ev as RemoteNotifySettingsChanged;
            if (rns != null) { _bus.Publish(rns); return; }
            RemoteMessageReactionsChanged rmrc = ev as RemoteMessageReactionsChanged;
            if (rmrc != null) { _bus.Publish(rmrc); return; }
        }

        // ---- Updates loop ------------------------------------------------------

        private void SubscribeUpdatesPort()
        {
            // Idempotent: if we already subscribed (e.g. Bootstrap then Resync),
            // dispose the previous subscription first.
            IDisposable prior = _updatesSubscription;
            if (prior != null) { try { prior.Dispose(); } catch { } }

            _updatesSubscription = _updatesPort.Subscribe(OnRawUpdateAsync);
        }

        // The IUpdatesPort handler is invoked by the transport thread. We immediately
        // hand off into _applyGate so all work happens on a serialized async path.
        private async Task OnRawUpdateAsync(byte[] rawTlBody)
        {
            if (rawTlBody == null) return;
            if (Volatile.Read(ref _disposed) != 0) return;

            CancellationToken ct = _shutdownCts.Token;
            try
            {
                await _applyGate.WaitAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                Result<ApplyResult, SyncError> r = await _process.HandleRawAsync(rawTlBody, ct).ConfigureAwait(false);
                if (r.IsFail)
                {
                    _log.Warn("sync.apply: " + r.Error.ToString());
                    return;
                }

                ApplyResult applied = r.Value;
                if (applied.NeedsReseed)
                {
                    // Fire-and-forget resync so we don't block the update path.
                    Task ignoredResync = ResyncAsync(ct);
                    GC.KeepAlive(ignoredResync);
                }
                else if (applied.NeedsGetDifference)
                {
                    // Fire updates.getDifference, coalesced via
                    // _getDifferenceInFlight so a burst of
                    // gap signals doesn't multiply the round-trip count.
                    // Without this, downstream contexts (CallsUpdatePoller)
                    // poll getDifference themselves at ~1 Hz to compensate
                    // — wasteful and battery-draining.
                    if (Interlocked.CompareExchange(ref _getDifferenceInFlight, 1, 0) == 0)
                    {
                        _log.Info("sync.gap: common box gap detected — firing getDifference");
                        Task ignored = RunCatchUpAsync(ct);
                        GC.KeepAlive(ignored);
                    }
                    else
                    {
                        _log.Info("sync.gap: common box gap detected — catch-up already in flight, coalesced");
                    }
                }
                if (applied.NeedsChannelDifference != null)
                {
                    for (int i = 0; i < applied.NeedsChannelDifference.Count; i++)
                    {
                        long channelId = applied.NeedsChannelDifference[i];
                        // Resolve the access_hash via the composition-
                        // supplied resolver (delegates to IPeerCache). Without
                        // it the server returns CHANNEL_INVALID and
                        // the cursor never recovers from a gap. When
                        // resolution fails (channel hasn't landed in
                        // the cache yet) we still fire the request —
                        // SyncState.TryAdvanceChannelPts now fail-opens
                        // and emits the gap-message, so the user keeps
                        // seeing live activity even if the server-side
                        // catch-up RPC ultimately fails.
                        long accessHash = 0L;
                        if (_channelAccessHashResolver != null)
                        {
                            try { accessHash = _channelAccessHashResolver(channelId); }
                            catch { accessHash = 0L; }
                        }
                        _log.Info("sync.channel-diff: requesting channelId=" +
                            channelId + " accessHash=" +
                            (accessHash == 0L ? "<unknown>" : "0x" + accessHash.ToString("x16")));
                        Task ignoredCh = _channelDiff.HandleAsync(new GetChannelDifferenceCommand(channelId, accessHash), ct);
                        GC.KeepAlive(ignoredCh);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("sync.apply: unexpected " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                _applyGate.Release();
            }
        }

        // ---- getDifference catch-up ---------------------------------------------

        private async Task RunCatchUpAsync(CancellationToken ct)
        {
            try
            {
                var r = await _bootstrap.CatchUpViaGetDifferenceAsync(ct).ConfigureAwait(false);
                if (r.IsFail)
                {
                    _log.Warn("sync.gap: catch-up failed: " + r.Error);
                }
                else
                {
                    _log.Info("sync.gap: catch-up ok");
                }
            }
            catch (OperationCanceledException)
            {
                _log.Info("sync.gap: catch-up cancelled");
            }
            catch (Exception ex)
            {
                _log.Warn("sync.gap: catch-up threw " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _getDifferenceInFlight, 0);
            }
        }

        // ---- Bus → CLR event bridge ----------------------------------------------

        private void OnUpdatesApplied(UpdatesApplied e)
        {
            EventHandler<UpdatesAppliedEventArgs> h = UpdatesApplied;
            if (h != null)
            {
                try
                {
                    h(this, new UpdatesAppliedEventArgs(e.EventCount, e.TimestampUtc));
                }
                catch (Exception ex)
                {
                    _log.Warn("sync.evt: UpdatesApplied subscriber threw " + ex.Message);
                }
            }
        }

        // ---- IDisposable -------------------------------------------------------

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            try { _shutdownCts.Cancel(); } catch { }

            IDisposable sub = _updatesSubscription;
            if (sub != null) { try { sub.Dispose(); } catch { } }

            try { _subUpdatesApplied.Dispose(); } catch { }

            try { _shutdownCts.Dispose(); } catch { }
            try { _applyGate.Dispose(); } catch { }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException("SyncApplication");
            }
        }
    }
}
