// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Domain.Events
{
    /// <summary>
    /// Common base for Messages domain events — carries timestamp and peer for
    /// cheap cross-context fan-out.
    /// </summary>
    public abstract class MessageEventBase : IDomainEvent
    {
        protected MessageEventBase(string peerKey, DateTime timestampUtc)
        {
            PeerKey = peerKey;
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// Emitted synchronously by SendTextMessageHandler before any network I/O.
    /// This is the M1-mandatory event: UI binds to it for the optimistic bubble.
    /// </summary>
    public sealed class MessageQueuedForSend : MessageEventBase
    {
        public MessageQueuedForSend(string peerKey, long clientTempId, MessageContent content, DateTime timestampUtc)
            : base(peerKey, timestampUtc)
        {
            ClientTempId = clientTempId;
            Content = content;
        }

        public long ClientTempId { get; private set; }
        public MessageContent Content { get; private set; }
    }

    /// <summary>
    /// Emitted on server ACK of an optimistic send. Carries enough info for UI
    /// to swap a pending bubble (keyed by ClientTempId) to its confirmed form.
    /// </summary>
    public sealed class MessageSent : MessageEventBase
    {
        public MessageSent(string peerKey, long clientTempId, long serverId, DateTime serverDate, DateTime timestampUtc)
            : base(peerKey, timestampUtc)
        {
            ClientTempId = clientTempId;
            ServerId = serverId;
            ServerDate = serverDate;
        }

        public long ClientTempId { get; private set; }
        public long ServerId { get; private set; }
        public DateTime ServerDate { get; private set; }
    }

    public sealed class MessageSendFailed : MessageEventBase
    {
        public MessageSendFailed(string peerKey, long clientTempId, string reason, DateTime timestampUtc)
            : base(peerKey, timestampUtc)
        {
            ClientTempId = clientTempId;
            Reason = reason ?? string.Empty;
        }

        public long ClientTempId { get; private set; }
        public string Reason { get; private set; }
    }

    public sealed class MessageReceived : MessageEventBase
    {
        public MessageReceived(string peerKey, long messageId, DateTime at, bool isOutgoing, DateTime timestampUtc)
            : this(peerKey, messageId, at, isOutgoing, timestampUtc, fromUserId: 0L, body: null)
        {
        }

        /// <summary>
        /// Richer ctor — used by the Sync→Messages bridge so the UI can render
        /// the new bubble in place instead of re-fetching the entire
        /// conversation page. <paramref name="body"/> may be
        /// empty (service messages, media-only) — UI falls back to a
        /// "(media)" placeholder. <paramref name="fromUserId"/> is 0 for
        /// 1-on-1 dms (caller already knows the peer).
        /// </summary>
        public MessageReceived(
            string peerKey,
            long messageId,
            DateTime at,
            bool isOutgoing,
            DateTime timestampUtc,
            long fromUserId,
            string body)
            : base(peerKey, timestampUtc)
        {
            MessageId = messageId;
            At = at;
            IsOutgoing = isOutgoing;
            FromUserId = fromUserId;
            Body = body ?? string.Empty;
        }

        public long MessageId { get; private set; }
        public DateTime At { get; private set; }
        public bool IsOutgoing { get; private set; }

        /// <summary>
        /// Sender's user id when the message arrives in a group / channel;
        /// 0 for 1-on-1 DMs (the peer is unambiguous).
        /// </summary>
        public long FromUserId { get; private set; }

        /// <summary>
        /// Plain-text body of the message. Empty for media-only / service
        /// messages — caller renders a placeholder. Optional: the legacy
        /// <c>MessageReceived</c> constructor sets this to empty so the
        /// thin push pipeline (which only knows peer+id) keeps working.
        /// </summary>
        public string Body { get; private set; }
    }

    /// <summary>
    /// The peer read our outgoing messages up to <see cref="UpToMessageId"/>,
    /// advancing the "✓✓" read mark on our side. Distinct from
    /// <see cref="MessageReadByMe"/> which is emitted when WE read messages
    /// from the peer.
    /// </summary>
    public sealed class MessagesReadByPeer : MessageEventBase
    {
        public MessagesReadByPeer(string peerKey, long upToMessageId, DateTime timestampUtc)
            : base(peerKey, timestampUtc)
        {
            UpToMessageId = upToMessageId;
        }

        public long UpToMessageId { get; private set; }
    }

    /// <summary>
    /// Peer's online presence changed. Surfaced to ChatList (last-seen
    /// footer) and ChatPage (subtitle status text).
    /// </summary>
    public sealed class PeerStatusChanged : IDomainEvent
    {
        public PeerStatusChanged(long userId, bool isOnline, DateTime? lastOnlineUtc, DateTime timestampUtc)
        {
            UserId = userId;
            IsOnline = isOnline;
            LastOnlineUtc = lastOnlineUtc;
            TimestampUtc = timestampUtc;
        }

        public long UserId { get; private set; }
        public bool IsOnline { get; private set; }

        /// <summary>Server-reported last-online time (UTC), or null when unavailable.</summary>
        public DateTime? LastOnlineUtc { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// Peer is composing a message / sending media in the chat
    /// <see cref="PeerKey"/>. UI shows "is typing…" /
    /// "is recording a voice message…" until the action expires
    /// (~6 s server-enforced) or a new <c>updateUserTyping</c> with
    /// <see cref="TypingAction"/> = Cancel arrives.
    /// </summary>
    public sealed class PeerTypingChanged : IDomainEvent
    {
        public PeerTypingChanged(string peerKey, long userId, string action, DateTime timestampUtc)
        {
            PeerKey = peerKey ?? string.Empty;
            UserId = userId;
            Action = action ?? "Typing";
            TimestampUtc = timestampUtc;
        }

        public string PeerKey { get; private set; }
        public long UserId { get; private set; }

        /// <summary>
        /// Free-form action label projected from
        /// <c>SendMessageAction</c> — e.g. "Typing", "RecordingVoice",
        /// "RecordingVideo", "UploadingPhoto", "Cancel".
        /// </summary>
        public string Action { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    public sealed class MessageEdited : MessageEventBase
    {
        public MessageEdited(string peerKey, long messageId, DateTime at, DateTime timestampUtc)
            : this(peerKey, messageId, at, timestampUtc, body: null)
        {
        }

        /// <summary>
        /// Richer ctor — used by the Sync→Messages bridge so the UI can
        /// rewrite the bubble text in place without a full reload.
        /// <paramref name="body"/> may be
        /// empty when the edit was on a media caption (the body field
        /// wasn't surfaced) or a service-message edit; the VM falls back
        /// to a partial reload in that case.
        /// </summary>
        public MessageEdited(string peerKey, long messageId, DateTime at, DateTime timestampUtc, string body)
            : base(peerKey, timestampUtc)
        {
            MessageId = messageId;
            At = at;
            Body = body ?? string.Empty;
        }

        public long MessageId { get; private set; }
        public DateTime At { get; private set; }

        /// <summary>
        /// Edited message body. Empty when the bridge couldn't surface
        /// it (caption edit, service message). Subscribers that need the
        /// full new content fall back to a reload when this is empty.
        /// </summary>
        public string Body { get; private set; }
    }

    public sealed class MessageDeleted : MessageEventBase
    {
        public MessageDeleted(string peerKey, long messageId, DateTime timestampUtc)
            : base(peerKey, timestampUtc)
        {
            MessageId = messageId;
        }

        public long MessageId { get; private set; }
    }

    public sealed class MessageReadByMe : MessageEventBase
    {
        public MessageReadByMe(string peerKey, long upToMessageId, DateTime timestampUtc)
            : base(peerKey, timestampUtc)
        {
            UpToMessageId = upToMessageId;
        }

        public long UpToMessageId { get; private set; }
    }

    /// <summary>
    /// Domain-internal payload used by <c>MessageStream.Apply</c>. This is not
    /// published on the bus directly; the public event is <see cref="MessageEdited"/>.
    /// </summary>
    public sealed class MessageEditEvent
    {
        public MessageEditEvent(long messageId, MessageContent newContent, DateTime editedAt)
        {
            MessageId = messageId;
            NewContent = newContent;
            EditedAt = editedAt;
        }

        public long MessageId { get; private set; }
        public MessageContent NewContent { get; private set; }
        public DateTime EditedAt { get; private set; }
    }

    /// <summary>
    /// Domain-internal payload used by <c>MessageStream.Apply</c>. The public
    /// event is <see cref="MessageDeleted"/>.
    /// </summary>
    public sealed class MessageDeleteEvent
    {
        public MessageDeleteEvent(long messageId)
        {
            MessageId = messageId;
        }

        public long MessageId { get; private set; }
    }
}
