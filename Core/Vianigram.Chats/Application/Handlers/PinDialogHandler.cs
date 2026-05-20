// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Infrastructure;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Pins a dialog server-side (messages.toggleDialogPin#a731e257 with pinned=true)
    /// and updates the local aggregate. Persists, then drains and publishes the
    /// staged domain events.
    /// </summary>
    public sealed class PinDialogHandler
    {
        private readonly IDialogRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;

        public PinDialogHandler(IDialogRepository repo, IMtProtoRpcPort rpc, IEventBus bus)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
        }

        public async Task<Result<Unit, ChatError>> HandleAsync(PinDialogCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, ChatError>.Fail(ChatError.Unknown("null command"));

            var dialog = await _repo.GetAsync(cmd.Peer, ct).ConfigureAwait(false);
            if (dialog == null) return Result<Unit, ChatError>.Fail(ChatError.PeerNotFound(cmd.Peer.ToString()));

            try
            {
                byte[] payload = TlEncoder.EncodeToggleDialogPin(cmd.Peer, /*pinned*/ true);
                await _rpc.CallAsync(payload, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ChatError>.Fail(ChatError.NetworkError("toggleDialogPin failed", ex));
            }

            DateTime now = DateTime.UtcNow;
            dialog.Pin(now);
            await _repo.UpsertAsync(dialog, ct).ConfigureAwait(false);
            PublishPending(dialog);
            return Result<Unit, ChatError>.Ok(Unit.Value);
        }

        private void PublishPending(Domain.Entities.Dialog dialog)
        {
            IList<IDomainEvent> events = dialog.DequeuePendingEvents();
            for (int i = 0; i < events.Count; i++)
            {
                PublishOne(events[i]);
            }
        }

        private void PublishOne(IDomainEvent evt)
        {
            // The IEventBus.Publish<T> API requires a compile-time T; we dispatch by runtime type
            // using the well-known event types defined by Chats.
            var u = evt as Domain.Events.DialogUpdated; if (u != null) { _bus.Publish(u); return; }
            var a = evt as Domain.Events.DialogAdded;   if (a != null) { _bus.Publish(a); return; }
            var r = evt as Domain.Events.DialogRemoved; if (r != null) { _bus.Publish(r); return; }
            // Unknown domain event; intentionally drop. The bus dispatches by static type only,
            // so a generic fallback would not reach typed subscribers.
        }
    }
}
