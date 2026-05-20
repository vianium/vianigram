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
    /// Unpins a dialog server-side (messages.toggleDialogPin#a731e257 with pinned=false)
    /// and updates the local aggregate.
    /// </summary>
    public sealed class UnpinDialogHandler
    {
        private readonly IDialogRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;

        public UnpinDialogHandler(IDialogRepository repo, IMtProtoRpcPort rpc, IEventBus bus)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
        }

        public async Task<Result<Unit, ChatError>> HandleAsync(UnpinDialogCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, ChatError>.Fail(ChatError.Unknown("null command"));

            var dialog = await _repo.GetAsync(cmd.Peer, ct).ConfigureAwait(false);
            if (dialog == null) return Result<Unit, ChatError>.Fail(ChatError.PeerNotFound(cmd.Peer.ToString()));

            try
            {
                byte[] payload = TlEncoder.EncodeToggleDialogPin(cmd.Peer, /*pinned*/ false);
                await _rpc.CallAsync(payload, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, ChatError>.Fail(ChatError.NetworkError("toggleDialogPin failed", ex));
            }

            DateTime now = DateTime.UtcNow;
            dialog.Unpin(now);
            await _repo.UpsertAsync(dialog, ct).ConfigureAwait(false);
            PublishPending(dialog);
            return Result<Unit, ChatError>.Ok(Unit.Value);
        }

        private void PublishPending(Domain.Entities.Dialog dialog)
        {
            IList<IDomainEvent> events = dialog.DequeuePendingEvents();
            for (int i = 0; i < events.Count; i++)
            {
                var u = events[i] as Domain.Events.DialogUpdated; if (u != null) { _bus.Publish(u); continue; }
                var a = events[i] as Domain.Events.DialogAdded;   if (a != null) { _bus.Publish(a); continue; }
                var r = events[i] as Domain.Events.DialogRemoved; if (r != null) { _bus.Publish(r); continue; }
            }
        }
    }
}
