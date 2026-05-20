// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LeaveHandler.cs — Vianigram.Chats.Application.Handlers
// Wraps messages.deleteChatUser / channels.leaveChannel: leaves a peer and
// removes the local Dialog aggregate.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Events;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Leaves a basic group, megagroup, or channel. The outbound port dispatches
    /// the appropriate TL function based on <see cref="PeerId.Kind"/>:
    /// <c>messages.deleteChatUser(self)</c> for <c>Chat</c> and
    /// <c>channels.leaveChannel</c> for <c>Channel</c>.
    ///
    /// On success, the dialog is removed from the local repository and a
    /// <see cref="DialogRemoved"/> event is published. If the peer is unknown
    /// locally, a <see cref="ChatError.PeerNotFound"/> is returned without a
    /// roundtrip.
    /// </summary>
    public sealed class LeaveHandler
    {
        private readonly IDialogRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;

        public LeaveHandler(IDialogRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Chats.Leave");
        }

        public async Task<Result<Unit, ChatError>> HandleAsync(LeaveCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, ChatError>.Fail(ChatError.Unknown("null command"));
            if (cmd.Peer.Kind == PeerKind.User)
                return Result<Unit, ChatError>.Fail(ChatError.NotInExpectedState("cannot leave a 1:1 user peer"));

            _log.Info("leaving peer=" + cmd.Peer);

            var rpcResult = await _rpc.LeavePeerAsync(cmd.Peer, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _log.Warn("leave RPC failed: " + rpcResult.Error);
                return Result<Unit, ChatError>.Fail(rpcResult.Error);
            }

            try
            {
                await _repo.DeleteAsync(cmd.Peer, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Error("repo delete failed: " + ex.Message);
                return Result<Unit, ChatError>.Fail(ChatError.Unknown("repository delete failed", ex));
            }

            _bus.Publish(new DialogRemoved(cmd.Peer, DateTime.UtcNow));
            _log.Info("left peer=" + cmd.Peer);
            return Result<Unit, ChatError>.Ok(Unit.Value);
        }
    }
}
