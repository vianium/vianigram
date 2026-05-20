// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CreateForumTopicHandler.cs — Vianigram.Chats.Application.Handlers
// Wraps channels.createForumTopic — creates a topic with title and optional icon emoji.

using System;
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
    /// Creates a new topic in a forum-enabled channel by issuing
    /// <c>channels.createForumTopic</c>. The returned <see cref="ForumTopic"/>
    /// reflects the server-assigned topic id.
    ///
    /// No local repository state is mutated — topics belong to the channel; the
    /// next list refresh will pick the new entry up.
    /// </summary>
    public sealed class CreateForumTopicHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;

        public CreateForumTopicHandler(IMtProtoRpcPort rpc, ILogger log)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Chats.CreateForumTopic");
        }

        public async Task<Result<ForumTopic, ChatError>> HandleAsync(CreateForumTopicCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<ForumTopic, ChatError>.Fail(ChatError.Unknown("null command"));

            _log.Info("creating topic channel=" + cmd.Channel + " title='" + cmd.Title + "' icon='" + cmd.IconEmoji + "'");

            var rpcResult = await _rpc.ChannelsCreateForumTopicAsync(cmd.Channel, cmd.Title, cmd.IconEmoji, ct)
                                      .ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                _log.Warn("channels.createForumTopic failed: " + rpcResult.Error);
                return Result<ForumTopic, ChatError>.Fail(rpcResult.Error);
            }

            RawForumTopic raw = rpcResult.Value;
            var topic = new ForumTopic(
                raw.TopicId,
                raw.ChannelPeer,
                raw.Title,
                raw.IconEmoji,
                raw.UnreadCount,
                raw.TopMessageId,
                raw.CreatedAt);

            _log.Info("topic created channel=" + cmd.Channel + " topicId=" + raw.TopicId);
            return Result<ForumTopic, ChatError>.Ok(topic);
        }
    }
}
