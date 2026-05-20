// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GetForumTopicsHandler.cs — Vianigram.Chats.Application.Handlers
// Wraps channels.getForumTopics — listing topics for a forum-enabled channel.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Lists topics inside a forum-enabled channel by issuing
    /// <c>channels.getForumTopics</c>. Read-only.
    /// </summary>
    public sealed class GetForumTopicsHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;

        public GetForumTopicsHandler(IMtProtoRpcPort rpc, ILogger log)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Chats.GetForumTopics");
        }

        public async Task<Result<IList<ForumTopic>, ChatError>> HandleAsync(GetForumTopicsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<ForumTopic>, ChatError>.Fail(ChatError.Unknown("null command"));

            _log.Info("listing topics channel=" + cmd.Channel);

            var rpcResult = await _rpc.ChannelsGetForumTopicsAsync(cmd.Channel, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _log.Warn("channels.getForumTopics failed: " + rpcResult.Error);
                return Result<IList<ForumTopic>, ChatError>.Fail(rpcResult.Error);
            }

            IList<RawForumTopic> raws = rpcResult.Value;
            var topics = new List<ForumTopic>(raws.Count);
            for (int i = 0; i < raws.Count; i++)
            {
                var r = raws[i];
                topics.Add(new ForumTopic(
                    r.TopicId,
                    r.ChannelPeer,
                    r.Title,
                    r.IconEmoji,
                    r.UnreadCount,
                    r.TopMessageId,
                    r.CreatedAt));
            }

            _log.Info("channel=" + cmd.Channel + " topics=" + topics.Count);
            return Result<IList<ForumTopic>, ChatError>.Ok((IList<ForumTopic>)topics);
        }
    }
}
