// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// RpcDtos.cs — Vianigram.Chats.Ports.Outbound
// Plain-data DTOs returned by the outbound RPC methods.

using System;
using System.Collections.Generic;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Ports.Outbound
{
    /// <summary>
    /// Lightweight projection of a freshly-created chat / channel returned by
    /// <c>messages.createChat</c> or <c>channels.createChannel</c>. The handler
    /// turns this into a <c>Dialog</c> aggregate before publishing.
    /// </summary>
    public sealed class RawDialog
    {
        public RawDialog(PeerId peer, string title, DateTimeOffset createdAt)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
            Title = title ?? string.Empty;
            CreatedAt = createdAt;
        }

        public PeerId Peer { get; private set; }
        public string Title { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
    }

    /// <summary>
    /// Lightweight projection of <c>messages.chatFull</c> / <c>channels.channelFull</c>.
    /// </summary>
    public sealed class RawGroupInfo
    {
        public RawGroupInfo(
            PeerId peer,
            string title,
            string description,
            int memberCount,
            IList<RawGroupMember> members,
            bool isAdmin,
            bool isCreator,
            DateTimeOffset createdAt)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
            Title = title ?? string.Empty;
            Description = description ?? string.Empty;
            MemberCount = memberCount;
            Members = members ?? new RawGroupMember[0];
            IsAdmin = isAdmin;
            IsCreator = isCreator;
            CreatedAt = createdAt;
        }

        public PeerId Peer { get; private set; }
        public string Title { get; private set; }
        public string Description { get; private set; }
        public int MemberCount { get; private set; }
        public IList<RawGroupMember> Members { get; private set; }
        public bool IsAdmin { get; private set; }
        public bool IsCreator { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
    }

    /// <summary>One participant inside <see cref="RawGroupInfo"/>.</summary>
    public sealed class RawGroupMember
    {
        public RawGroupMember(long userId, string displayName, bool isAdmin, DateTimeOffset joinedAt)
        {
            UserId = userId;
            DisplayName = displayName ?? string.Empty;
            IsAdmin = isAdmin;
            JoinedAt = joinedAt;
        }

        public long UserId { get; private set; }
        public string DisplayName { get; private set; }
        public bool IsAdmin { get; private set; }
        public DateTimeOffset JoinedAt { get; private set; }
    }

    /// <summary>
    /// Projection of one <c>forumTopic</c> TL constructor.
    /// </summary>
    public sealed class RawForumTopic
    {
        public RawForumTopic(
            long topicId,
            PeerId channelPeer,
            string title,
            string iconEmoji,
            int unreadCount,
            long? topMessageId,
            DateTimeOffset createdAt)
        {
            if (channelPeer == null) throw new ArgumentNullException("channelPeer");
            TopicId = topicId;
            ChannelPeer = channelPeer;
            Title = title ?? string.Empty;
            IconEmoji = iconEmoji ?? string.Empty;
            UnreadCount = unreadCount;
            TopMessageId = topMessageId;
            CreatedAt = createdAt;
        }

        public long TopicId { get; private set; }
        public PeerId ChannelPeer { get; private set; }
        public string Title { get; private set; }
        public string IconEmoji { get; private set; }
        public int UnreadCount { get; private set; }
        public long? TopMessageId { get; private set; }
        public DateTimeOffset CreatedAt { get; private set; }
    }
}
