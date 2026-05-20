// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ForumTopic.cs — Vianigram.Chats.Domain.ValueObjects
// Represents one topic inside a forum-enabled channel (Telegram "Topics" feature).

using System;

namespace Vianigram.Chats.Domain.ValueObjects
{
    /// <summary>
    /// One topic inside a forum-enabled channel. Mirrors the data carried by
    /// <c>forumTopic</c> in TL: id, title, optional icon emoji, the top message
    /// pointer, unread counter, and creation timestamp. Immutable.
    ///
    /// The <see cref="ChannelPeer"/> identifies the parent channel; <see cref="TopicId"/>
    /// is unique within that channel and is also the message-id of the topic root.
    /// </summary>
    public sealed class ForumTopic
    {
        private readonly long _topicId;
        private readonly PeerId _channelPeer;
        private readonly string _title;
        private readonly string _iconEmoji;
        private readonly int _unreadCount;
        private readonly long? _topMessageId;
        private readonly DateTimeOffset _createdAt;

        public ForumTopic(
            long topicId,
            PeerId channelPeer,
            string title,
            string iconEmoji,
            int unreadCount,
            long? topMessageId,
            DateTimeOffset createdAt)
        {
            if (channelPeer == null) throw new ArgumentNullException("channelPeer");
            if (channelPeer.Kind != PeerKind.Channel)
                throw new ArgumentException("forum topics live under a channel peer", "channelPeer");
            if (unreadCount < 0) throw new ArgumentOutOfRangeException("unreadCount");
            _topicId = topicId;
            _channelPeer = channelPeer;
            _title = title ?? string.Empty;
            _iconEmoji = iconEmoji ?? string.Empty;
            _unreadCount = unreadCount;
            _topMessageId = topMessageId;
            _createdAt = createdAt;
        }

        public long TopicId { get { return _topicId; } }
        public PeerId ChannelPeer { get { return _channelPeer; } }
        public string Title { get { return _title; } }
        public string IconEmoji { get { return _iconEmoji; } }
        public int UnreadCount { get { return _unreadCount; } }
        public long? TopMessageId { get { return _topMessageId; } }
        public DateTimeOffset CreatedAt { get { return _createdAt; } }
    }
}
