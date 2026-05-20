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
    /// Apply a server-pushed UpdatesEnvelope to <see cref="SyncState"/>.
    ///
    /// Hot path: called once per inbound TL Updates container (or short variant).
    /// Decodes the payload, folds it into the aggregate via SyncState.Apply, then
    /// publishes derived events on the kernel <see cref="IEventBus"/> in the
    /// order the aggregate emitted them — preserving causal ordering for
    /// downstream subscribers (Messages, Chats, Contacts).
    ///
    /// Threading: single-dispatcher invocation is the caller's responsibility
    /// (see <see cref="Vianigram.Sync.Application.SyncApplication"/> for the
    /// queue/lock that serializes calls).
    ///
    /// On <see cref="ApplyResult.NeedsGetDifference"/> or NeedsChannelDifference,
    /// the handler signals the application layer (which kicks off the
    /// recovery RPC). The handler itself is synchronous-feeling: one envelope in,
    /// one apply pass out, plus event fan-out.
    /// </summary>
    public sealed class ProcessUpdatesHandler
    {
        private readonly SyncState _state;
        private readonly ISyncStateRepository _repo;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ProcessUpdatesHandler(SyncState state, ISyncStateRepository repo, IEventBus bus, ILogger logger, IClock clock)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (repo == null) throw new ArgumentNullException("repo");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");
            _state = state;
            _repo = repo;
            _bus = bus;
            _log = new TimestampedLogger(logger, "Sync.ProcessUpdates");
            _clock = clock;
        }

        public async Task<Result<ApplyResult, SyncError>> HandleAsync(ProcessUpdatesCommand cmd, CancellationToken ct)
        {
            if (cmd == null || cmd.Envelope == null)
            {
                return Result<ApplyResult, SyncError>.Fail(SyncError.Make(SyncError.Unknown, "null command or envelope"));
            }

            ApplyResult result;
            try
            {
                result = _state.Apply(cmd.Envelope);
            }
            catch (Exception ex)
            {
                return Result<ApplyResult, SyncError>.Fail(SyncError.From(ex, SyncError.Unknown));
            }

            // Publish derived events in order. We swallow per-event publish
            // failures so a single broken subscriber does not block the rest.
            int published = 0;
            if (result.Events != null)
            {
                for (int i = 0; i < result.Events.Count; i++)
                {
                    IDomainEvent ev = result.Events[i];
                    try
                    {
                        PublishDynamic(ev);
                        published++;
                        // Diagnostic: log the kind of event that just
                        // hit the bus. Low volume (one per actual update)
                        // and the only way to verify Sync → bus → bridges
                        // is alive without attaching a debugger.
                        _log.Info("sync.published " + ev.GetType().Name);
                    }
                    catch (Exception ex)
                    {
                        _log.Warn("sync.publish: " + ev.GetType().Name + " failed: " + ex.Message);
                    }
                }
            }
            else
            {
                _log.Info("sync.applied 0 events (envelope produced no derived events)");
            }

            // Persist the new cursor (best-effort; in-memory state remains correct).
            try
            {
                await _repo.SaveCursorAsync(_state.Common, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("sync.persist: cursor save failed: " + ex.Message);
            }

            // UpdatesApplied event for diagnostics / SyncApplication's CLR event fan-out.
            try
            {
                _bus.Publish(new UpdatesApplied(published, _clock.UtcNow));
            }
            catch (Exception ex)
            {
                _log.Warn("sync.publish: UpdatesApplied failed: " + ex.Message);
            }

            return Result<ApplyResult, SyncError>.Ok(result);
        }

        // IEventBus.Publish<TEvent> uses a generic constraint; to dispatch a runtime-typed
        // IDomainEvent we need to use reflection or a small per-type cache. Reflection on
        // the hot path is undesirable; instead, we cast through known sealed event types
        // emitted by SyncState. Anything new added must land here.
        private void PublishDynamic(IDomainEvent ev)
        {
            RemoteMessageReceived rmr = ev as RemoteMessageReceived;
            if (rmr != null)
            {
                // Include the peer key so traces show whether channel:N
                // messages reached the bus or got dropped upstream.
                _log.Info("sync.emit RemoteMessageReceived peer=" +
                    (rmr.PeerKey ?? "?") +
                    " id=" + (rmr.Message != null ? rmr.Message.Id : 0));
                _bus.Publish(rmr);
                return;
            }

            // Edits surface as their own typed event so the bridge in
            // MessagesUpdatesProcessor can distinguish them from inserts.
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

            // Unknown derived event type — log so we don't silently drop.
            _log.Warn("sync.publish: unknown derived event " + ev.GetType().Name);
        }

        /// <summary>
        /// Helper for SyncApplication's IUpdatesPort wiring: decode raw TL bytes
        /// into an UpdatesEnvelope, then dispatch the apply path. Used by the
        /// updates loop subscriber to keep TL/decode concerns out of the
        /// IUpdatesPort contract.
        /// </summary>
        public async Task<Result<ApplyResult, SyncError>> HandleRawAsync(byte[] rawTlBody, CancellationToken ct)
        {
            if (rawTlBody == null || rawTlBody.Length == 0)
            {
                return Result<ApplyResult, SyncError>.Fail(
                    SyncError.Make(SyncError.TlDecodeFailure, "empty raw body"));
            }

            UpdatesEnvelope env;
            try
            {
                env = TlDecoder.DecodeUpdatesEnvelope(rawTlBody);
            }
            catch (Exception ex)
            {
                return Result<ApplyResult, SyncError>.Fail(SyncError.From(ex, SyncError.TlDecodeFailure));
            }

            if (env == null)
            {
                // Unknown supertype constructor — preserve the apply contract by
                // returning an empty result (no derived events, no gaps); caller
                // logs and continues.
                return Result<ApplyResult, SyncError>.Ok(ApplyResult.Empty());
            }

            return await HandleAsync(new ProcessUpdatesCommand(env), ct).ConfigureAwait(false);
        }

        // Pulled out for tests / future direct invokers; not part of the public ISyncApi.
        internal IList<IDomainEvent> TestApply(UpdatesEnvelope envelope)
        {
            ApplyResult r = _state.Apply(envelope);
            return r.Events;
        }
    }
}
