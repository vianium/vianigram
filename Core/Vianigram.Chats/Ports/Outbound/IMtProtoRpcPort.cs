// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IMtProtoRpcPort.cs — Vianigram.Chats.Ports.Outbound
// Outbound MTProto port for the Chats bounded context. Exposes a raw call plus
// strongly-typed methods covering create/leave/forum-topic flows.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Ports.Outbound
{
    /// <summary>
    /// Outbound port for issuing one MTProto RPC. Defined per-context to keep
    /// bounded contexts decoupled (Chats does not reference Account.Ports).
    /// The same concrete adapter (<c>MtProtoRpcAdapter</c> in Composition / Sync)
    /// implements every context's IMtProtoRpcPort interface — the ACL pattern
    /// described in <c>docs/managed-architecture/principles.md</c>.
    ///
    /// V1 contract (raw call):
    ///   - <paramref name="payload"/> is a fully serialized TL request body.
    ///   - The returned byte[] is the raw TL response payload (constructor + body).
    ///   - Any RPC-level error is signalled by throwing; the calling handler MUST
    ///     catch and translate to a <c>ChatError</c>. Network errors (no response)
    ///     are likewise translated by the handler.
    ///
    /// Typed-method contract:
    ///   - One method per inbound API. Each maps roughly 1:1 to a TL
    ///     function (messages.createChat, channels.createChannel, etc.).
    ///   - Results are delivered as <c>Result&lt;DTO, ChatError&gt;</c>; no
    ///     exceptions cross the port. The adapter is responsible for translating
    ///     RPC-level failures into the appropriate <see cref="ChatError"/>.
    /// </summary>
    public interface IMtProtoRpcPort
    {
        Task<byte[]> CallAsync(byte[] payload, CancellationToken ct);

        // ---- Typed RPC methods -----------------------------------------------------------

        /// <summary>messages.createChat — create a basic group.</summary>
        Task<Result<RawDialog, ChatError>> MessagesCreateChatAsync(string title, IList<long> userIds, CancellationToken ct);

        /// <summary>channels.createChannel — create a broadcast or megagroup channel.</summary>
        Task<Result<RawDialog, ChatError>> ChannelsCreateChannelAsync(string title, string description, bool isPublic, string username, CancellationToken ct);

        /// <summary>channels.checkUsername — reserve-availability check for public channel usernames.</summary>
        Task<Result<bool, ChatError>> ChannelsCheckUsernameAsync(string username, CancellationToken ct);

        /// <summary>
        /// Leaves a peer. Adapter dispatches to <c>messages.deleteChatUser</c> for
        /// <c>PeerKind.Chat</c> and <c>channels.leaveChannel</c> for <c>PeerKind.Channel</c>.
        /// </summary>
        Task<Result<Unit, ChatError>> LeavePeerAsync(PeerId peer, CancellationToken ct);

        /// <summary>
        /// Fetches the "full" form of a chat or channel. Adapter dispatches to
        /// <c>messages.getFullChat</c> for <c>PeerKind.Chat</c> and
        /// <c>channels.getFullChannel</c> for <c>PeerKind.Channel</c>.
        /// </summary>
        Task<Result<RawGroupInfo, ChatError>> GetFullPeerAsync(PeerId peer, CancellationToken ct);

        /// <summary>channels.getForumTopics — list topics for a forum-enabled channel.</summary>
        Task<Result<IList<RawForumTopic>, ChatError>> ChannelsGetForumTopicsAsync(PeerId channel, CancellationToken ct);

        /// <summary>channels.createForumTopic — create a new topic in a forum-enabled channel.</summary>
        Task<Result<RawForumTopic, ChatError>> ChannelsCreateForumTopicAsync(PeerId channel, string title, string iconEmoji, CancellationToken ct);
    }
}
