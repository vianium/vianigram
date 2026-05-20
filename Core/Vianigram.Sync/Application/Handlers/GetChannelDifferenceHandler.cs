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
using Vianigram.Sync.Domain.Entities;
using Vianigram.Sync.Domain.Errors;
using Vianigram.Sync.Domain.Events;
using Vianigram.Sync.Domain.ValueObjects;
using Vianigram.Sync.Infrastructure;
using Vianigram.Sync.Ports.Outbound;

namespace Vianigram.Sync.Application.Handlers
{
    /// <summary>
    /// Issue updates.getChannelDifference for a specific channel — used when
    /// SyncState detected a per-channel pts gap, or when the user joins a channel
    /// for the first time and we need a starting cursor.
    ///
    /// Note: this handler used to update only the channel cursor and discard
    /// <c>new_messages</c> / <c>other_updates</c> from the response — which
    /// silently dropped EVERY channel message that
    /// arrived via the diff path (i.e. most of them, since channel pushes
    /// usually carry just <c>updateChannel#635b4c09</c> as a "fetch me"
    /// signal). We now byte-scan the response body for embedded Updates AND
    /// raw Messages and emit them through the bus so notifications + chat
    /// list bumps fire end-to-end.
    /// </summary>
    public sealed class GetChannelDifferenceHandler
    {
        private readonly SyncState _state;
        private readonly ISyncStateRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public GetChannelDifferenceHandler(
            SyncState state,
            ISyncStateRepository repo,
            IMtProtoRpcPort rpc,
            ILogger logger,
            IClock clock)
            : this(state, repo, rpc, bus: null, logger: logger, clock: clock)
        {
        }

        public GetChannelDifferenceHandler(
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
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");
            _state = state;
            _repo = repo;
            _rpc = rpc;
            _bus = bus; // null-tolerant — back-compat with older composition roots
            _log = new TimestampedLogger(logger, "Sync.GetChannelDifference");
            _clock = clock;
        }

        public async Task<Result<Unit, SyncError>> HandleAsync(GetChannelDifferenceCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
            {
                return Result<Unit, SyncError>.Fail(SyncError.Make(SyncError.Unknown, "null command"));
            }

            int currentPts = 0;
            ChannelCursor existing;
            if (_state.Channels.TryGetValue(cmd.ChannelId, out existing))
            {
                currentPts = existing.Pts;
            }

            byte[] body;
            try
            {
                body = TlEncoder.EncodeGetChannelDifference(
                    channelId: cmd.ChannelId,
                    accessHash: cmd.AccessHash,
                    pts: currentPts,
                    limit: 100,
                    force: false);
            }
            catch (Exception ex)
            {
                return Result<Unit, SyncError>.Fail(SyncError.From(ex, SyncError.TlEncodeFailure));
            }

            Result<byte[], MtProtoRpcError> rpcResult;
            try
            {
                rpcResult = await _rpc.InvokeAsync(body, "updates.getChannelDifference", ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SyncError>.Fail(SyncError.From(ex, SyncError.TransportFailure));
            }

            if (rpcResult.IsFail)
            {
                return Result<Unit, SyncError>.Fail(
                    BootstrapSyncHandler.MapRpcError(rpcResult.Error, "updates.getChannelDifference"));
            }

            // Inspect the response header. We accept three constructors:
            //   updates.channelDifferenceEmpty#3e11affb { flags, pts, timeout? }
            //   updates.channelDifference#2064674e        { flags, pts, timeout?, ... }
            //   updates.channelDifferenceTooLong#a4bcc6fe { flags, timeout?, dialog, messages, chats, users }
            //
            // For the tooLong case we drop the in-memory channel cursor and let the
            // host trigger a per-channel reseed. For the other two, we extract the
            // returned pts and store it.
            byte[] resp = rpcResult.Value;
            if (resp == null || resp.Length < 4)
            {
                return Result<Unit, SyncError>.Fail(
                    SyncError.Make(SyncError.TlDecodeFailure, "empty channel difference body"));
            }

            int p = 0;
            uint ctor = TlDecoder.ReadUInt32(resp, ref p);

            const uint ChannelDifferenceEmptyId = 0x3e11affbu;
            const uint ChannelDifferenceId = 0x2064674eu;
            const uint ChannelDifferenceTooLongId = 0xa4bcc6feu;

            if (ctor == ChannelDifferenceTooLongId)
            {
                // Cannot incrementally apply; remove the cursor so the next pushed
                // message triggers a fresh "first sync" path via this same handler.
                _state.RemoveChannel(cmd.ChannelId);
                try
                {
                    await _repo.RemoveChannelCursorAsync(cmd.ChannelId, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warn("sync.channelDiff.remove failed for channel=" + cmd.ChannelId + ": " + ex.Message);
                }
                return Result<Unit, SyncError>.Fail(
                    SyncError.Make(SyncError.ChannelDifferenceTooLong,
                                   "channel " + cmd.ChannelId + " requires reseed"));
            }

            if (ctor == ChannelDifferenceEmptyId || ctor == ChannelDifferenceId)
            {
                // Both share the leading shape: flags(uint32) [final:flags.0?true is implicit]
                // pts(int32) timeout:flags.1?int.
                uint flags = TlDecoder.ReadUInt32Safe(resp, ref p);
                int pts = TlDecoder.ReadInt32Safe(resp, ref p);
                if ((flags & (1u << 1)) != 0)
                {
                    TlDecoder.ReadInt32Safe(resp, ref p); // timeout (not consumed)
                }

                _state.SetChannelCursor(cmd.ChannelId, pts);
                try
                {
                    await _repo.SaveChannelCursorAsync(
                        new ChannelCursor(cmd.ChannelId, pts, _clock.UtcNow), ct)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _log.Warn("sync.channelDiff.save failed for channel=" + cmd.ChannelId + ": " + ex.Message);
                }

                // Now extract the payload. updates.channelDifference#2064674e
                // ships new_messages:Vector<Message> +
                // other_updates:Vector<Update> — we scan the body for both
                // and emit derived events.
                // Without this, channel messages silently never reached
                // the bus and PushNotificationsService never saw them,
                // even when the cursor advance succeeded.
                if (ctor == ChannelDifferenceId)
                {
                    int emitted = ApplyChannelDifferenceBody(resp, cmd.ChannelId);
                    if (emitted > 0)
                    {
                        _log.Info("sync.channelDiff applied=" + emitted +
                            " channel=" + cmd.ChannelId + " newPts=" + pts);
                    }
                }
                return Result<Unit, SyncError>.Ok(Unit.Value);
            }

            return Result<Unit, SyncError>.Fail(
                SyncError.Make(SyncError.TlDecodeFailure,
                               "unknown channel difference constructor 0x" + ctor.ToString("x8")));
        }

        /// <summary>
        /// Extract messages + updates from a
        /// <c>updates.channelDifference#2064674e</c> response body and emit
        /// derived events through the bus. We use the
        /// same byte-scan approach SyncApplication's
        /// AcknowledgePolledDifferenceAsync uses on the common
        /// updates.difference body — it's brittle structurally because
        /// of the variable-size Message / Chat / User vectors, but a
        /// strict ctor whitelist + per-update validation makes the
        /// scan reliable in practice.
        ///
        /// Both scan paths run on the WHOLE body (including the leading
        /// header bytes we already consumed). False positives in the
        /// header region are essentially impossible given how strict
        /// the per-update / per-message validation is.
        /// </summary>
        private int ApplyChannelDifferenceBody(byte[] body, long channelId)
        {
            if (body == null || body.Length < 16) return 0;

            int total = 0;

            // Scan for wrapped Updates (e.g. updateNewChannelMessage,
            // updateDeleteChannelMessages, updateReadChannelInbox, etc.).
            IList<Update> updates = TlDecoder.ScanAndDecodeUpdatesInBody(body);
            if (updates != null && updates.Count > 0)
            {
                total += ApplyUpdates(updates);
            }

            // Scan for raw Messages — channel posts most commonly arrive
            // here, NOT wrapped in updateNewChannelMessage. The synthesizer
            // produces UpdateNewChannelMessage with pts=0 so SyncState
            // emits the message without touching the cursor.
            IList<Update> rawMsgUpdates = TlDecoder.ScanAndDecodeMessagesInBody(body);
            if (rawMsgUpdates != null && rawMsgUpdates.Count > 0)
            {
                total += ApplyUpdates(rawMsgUpdates);
            }

            return total;
        }

        private int ApplyUpdates(IList<Update> updates)
        {
            int applied = 0;
            for (int i = 0; i < updates.Count; i++)
            {
                Update u = updates[i];
                if (u == null) continue;
                try
                {
                    var envelope = new UpdatesEnvelopeShort(u, _state.Common.Date);
                    ApplyResult r = _state.Apply(envelope);
                    if (r != null && r.Events != null && _bus != null)
                    {
                        for (int j = 0; j < r.Events.Count; j++)
                        {
                            try { PublishDynamic(r.Events[j]); }
                            catch (Exception pubEx)
                            {
                                _log.Warn("sync.channelDiff.publish " +
                                    pubEx.GetType().Name + ": " + pubEx.Message);
                            }
                        }
                    }
                    applied++;
                }
                catch (Exception ex)
                {
                    _log.Warn("sync.channelDiff.apply " + u.GetType().Name +
                        " threw " + ex.GetType().Name + ": " + ex.Message);
                }
            }
            return applied;
        }

        // Mirror of ProcessUpdatesHandler.PublishDynamic — needs to know
        // every Sync.Domain.Events derived event we may emit. Anything
        // missing here is silently dropped (logged as warning) which is
        // fine for new event types we haven't wired yet — they show up
        // in logs without breaking the apply path.
        private void PublishDynamic(IDomainEvent ev)
        {
            if (ev == null || _bus == null) return;
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
            _log.Warn("sync.channelDiff.publish: unknown event " + ev.GetType().Name);
        }
    }
}
