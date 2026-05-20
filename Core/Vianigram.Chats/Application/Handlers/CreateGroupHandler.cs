// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CreateGroupHandler.cs — Vianigram.Chats.Application.Handlers
// Wraps messages.createChat: validates inputs, dispatches to the outbound RPC,
// upserts the resulting Dialog aggregate, and publishes DialogAdded.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.Events;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Creates a basic Telegram group via <c>messages.createChat</c>. On success,
    /// builds a <see cref="Dialog"/> aggregate from the returned <see cref="RawDialog"/>,
    /// upserts it into the repository, and publishes a <see cref="DialogAdded"/>
    /// domain event onto the bus so the UI catalog refreshes.
    ///
    /// Errors are surfaced as <see cref="Result{T,TError}"/> failures; no exception
    /// crosses the boundary except <see cref="OperationCanceledException"/>.
    /// </summary>
    public sealed class CreateGroupHandler
    {
        private readonly IDialogRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;

        public CreateGroupHandler(IDialogRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Chats.CreateGroup");
        }

        public async Task<Result<Dialog, ChatError>> HandleAsync(CreateGroupCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Dialog, ChatError>.Fail(ChatError.Unknown("null command"));

            _log.Info("creating group title='" + cmd.Title + "' members=" + cmd.UserIds.Count);

            var rpcResult = await _rpc.MessagesCreateChatAsync(cmd.Title, cmd.UserIds, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _log.Warn("messages.createChat failed: " + rpcResult.Error);
                return Result<Dialog, ChatError>.Fail(rpcResult.Error);
            }

            RawDialog raw = rpcResult.Value;
            DateTime now = DateTime.UtcNow;
            var dialog = new Dialog(raw.Peer, raw.Title, raw.CreatedAt.UtcDateTime, /*lastMessageId*/ 0L, /*unreadCount*/ 0);

            try
            {
                await _repo.UpsertAsync(dialog, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Error("repo upsert failed: " + ex.Message);
                return Result<Dialog, ChatError>.Fail(ChatError.Unknown("repository upsert failed", ex));
            }

            _bus.Publish(new DialogAdded(raw.Peer, now));
            _log.Info("group created peer=" + raw.Peer);
            return Result<Dialog, ChatError>.Ok(dialog);
        }
    }
}
