// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CreateChannelHandler.cs — Vianigram.Chats.Application.Handlers
// Wraps channels.createChannel: creates a broadcast / megagroup channel, optionally
// public with a reserved username; upserts the resulting Dialog aggregate.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.Events;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Creates a Telegram channel (broadcast or megagroup) via
    /// <c>channels.createChannel</c>. If <see cref="CreateChannelCommand.IsPublic"/>
    /// is set and a username is provided, the username is reserved as part of the
    /// same call.
    ///
    /// On success, the returned <see cref="RawDialog"/> is materialised as a
    /// <see cref="Dialog"/> aggregate, upserted, and a <see cref="DialogAdded"/>
    /// event is published.
    /// </summary>
    public sealed class CreateChannelHandler
    {
        private readonly IDialogRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;

        public CreateChannelHandler(IDialogRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Chats.CreateChannel");
        }

        public async Task<Result<Dialog, ChatError>> HandleAsync(CreateChannelCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Dialog, ChatError>.Fail(ChatError.Unknown("null command"));

            _log.Info("creating channel title='" + cmd.Title + "' public=" + cmd.IsPublic + " username='" + cmd.Username + "'");

            var rpcResult = await _rpc.ChannelsCreateChannelAsync(cmd.Title, cmd.Description, cmd.IsPublic, cmd.Username, ct)
                                      .ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _log.Warn("channels.createChannel failed: " + rpcResult.Error);
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
            _log.Info("channel created peer=" + raw.Peer);
            return Result<Dialog, ChatError>.Ok(dialog);
        }
    }
}
