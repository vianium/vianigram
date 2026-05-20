// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Polymorphic discriminated union of TL Update constructors that the Sync
    /// context understands. New TL constructors should land as new subclasses;
    /// unknown constructors collapse to <see cref="UpdateUnsupported"/> so we
    /// fail open rather than crash the update loop.
    ///
    /// The hierarchy is intentionally narrow — only the updates that drive the
    /// chat experience (new/edit/delete messages, read marks, typing, presence,
    /// profile changes). Group calls, business bots, stories, and stickers will
    /// land as additional subclasses later.
    ///
    /// Subclasses are sealed; abstract base allows pattern-matching dispatch via
    /// type checks without a Visitor.
    /// </summary>
    public abstract class Update
    {
        protected Update(uint constructorId)
        {
            ConstructorId = constructorId;
        }

        /// <summary>
        /// The TL constructor id (e.g. 0x1f2b0afd for updateNewMessage).
        /// Preserved for diagnostics and the unsupported-constructor fallback.
        /// </summary>
        public uint ConstructorId { get; private set; }
    }

    public sealed class UpdateNewMessage : Update
    {
        public const uint TlConstructorId = 0x1f2b0afdu;

        public UpdateNewMessage(int pts, int ptsCount, MessageDto message)
            : base(TlConstructorId)
        {
            Pts = pts;
            PtsCount = ptsCount;
            Message = message;
        }

        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public MessageDto Message { get; private set; }
    }

    public sealed class UpdateNewChannelMessage : Update
    {
        public const uint TlConstructorId = 0x62ba04d9u;

        public UpdateNewChannelMessage(int pts, int ptsCount, MessageDto message, long channelId)
            : base(TlConstructorId)
        {
            Pts = pts;
            PtsCount = ptsCount;
            Message = message;
            ChannelId = channelId;
        }

        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public MessageDto Message { get; private set; }
        public long ChannelId { get; private set; }
    }

    /// <summary>
    /// updateEditMessage#e40370a3 message:Message pts:int pts_count:int = Update;
    /// — emitted when a previously sent / received message is edited
    /// (text changed, media replaced, reactions enabled, etc.). Same
    /// wire shape as updateNewMessage; downstream consumers swap the
    /// rendered bubble in place.
    /// </summary>
    public sealed class UpdateEditMessage : Update
    {
        public const uint TlConstructorId = 0xe40370a3u;

        public UpdateEditMessage(int pts, int ptsCount, MessageDto message)
            : base(TlConstructorId)
        {
            Pts = pts;
            PtsCount = ptsCount;
            Message = message;
        }

        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public MessageDto Message { get; private set; }
    }

    /// <summary>
    /// updateEditChannelMessage#1b3f4df7 message:Message pts:int pts_count:int = Update;
    /// — channel/megagroup variant of updateEditMessage. Carries an
    /// extra channel id pulled from the inner Message's peer_id so
    /// downstream consumers can route the edit to the right channel
    /// stream.
    /// </summary>
    public sealed class UpdateEditChannelMessage : Update
    {
        public const uint TlConstructorId = 0x1b3f4df7u;

        public UpdateEditChannelMessage(int pts, int ptsCount, MessageDto message, long channelId)
            : base(TlConstructorId)
        {
            Pts = pts;
            PtsCount = ptsCount;
            Message = message;
            ChannelId = channelId;
        }

        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public MessageDto Message { get; private set; }
        public long ChannelId { get; private set; }
    }

    public sealed class UpdateMessageId : Update
    {
        public const uint TlConstructorId = 0x4e90bfd6u;

        public UpdateMessageId(int localId, long randomId) : base(TlConstructorId)
        {
            LocalId = localId;
            RandomId = randomId;
        }

        /// <summary>The server-issued message id replacing the optimistic random_id.</summary>
        public int LocalId { get; private set; }
        /// <summary>The random_id the client tagged the optimistic send with.</summary>
        public long RandomId { get; private set; }
    }

    public sealed class UpdateDeleteMessages : Update
    {
        public const uint TlConstructorId = 0xa20db0e5u;

        public UpdateDeleteMessages(int pts, int ptsCount, IList<int> messageIds)
            : base(TlConstructorId)
        {
            Pts = pts;
            PtsCount = ptsCount;
            MessageIds = messageIds ?? new List<int>(0);
        }

        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public IList<int> MessageIds { get; private set; }
    }

    public sealed class UpdateDeleteChannelMessages : Update
    {
        public const uint TlConstructorId = 0xc32d5b12u;

        public UpdateDeleteChannelMessages(long channelId, int pts, int ptsCount, IList<int> messageIds)
            : base(TlConstructorId)
        {
            ChannelId = channelId;
            Pts = pts;
            PtsCount = ptsCount;
            MessageIds = messageIds ?? new List<int>(0);
        }

        public long ChannelId { get; private set; }
        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public IList<int> MessageIds { get; private set; }
    }

    public sealed class UpdateUserStatus : Update
    {
        public const uint TlConstructorId = 0xe5bdf8deu;

        public UpdateUserStatus(long userId, UserStatusKind status, DateTime? wasOnline)
            : base(TlConstructorId)
        {
            UserId = userId;
            Status = status;
            WasOnline = wasOnline;
        }

        public long UserId { get; private set; }
        public UserStatusKind Status { get; private set; }
        public DateTime? WasOnline { get; private set; }
    }

    public sealed class UpdateUserTyping : Update
    {
        public const uint TlConstructorId = 0xc01e857fu;

        public UpdateUserTyping(long userId, TypingActionKind action) : base(TlConstructorId)
        {
            UserId = userId;
            Action = action;
        }

        public long UserId { get; private set; }
        public TypingActionKind Action { get; private set; }
    }

    public sealed class UpdateChatUserTyping : Update
    {
        public const uint TlConstructorId = 0x83487af0u;

        public UpdateChatUserTyping(long chatId, long userId, TypingActionKind action)
            : base(TlConstructorId)
        {
            ChatId = chatId;
            UserId = userId;
            Action = action;
        }

        public long ChatId { get; private set; }
        public long UserId { get; private set; }
        public TypingActionKind Action { get; private set; }
    }

    public sealed class UpdateChannelUserTyping : Update
    {
        public const uint TlConstructorId = 0x8c88c923u;

        public UpdateChannelUserTyping(long channelId, long userId, TypingActionKind action)
            : base(TlConstructorId)
        {
            ChannelId = channelId;
            UserId = userId;
            Action = action;
        }

        public long ChannelId { get; private set; }
        public long UserId { get; private set; }
        public TypingActionKind Action { get; private set; }
    }

    public sealed class UpdateChatParticipants : Update
    {
        // updateChatParticipants#7761198, updateChatParticipantAdd, etc. are folded into one event;
        // the constructor id below is the umbrella one — TlDecoder may fold variants.
        public const uint TlConstructorId = 0x07761198u;

        public UpdateChatParticipants(long chatId, int version) : base(TlConstructorId)
        {
            ChatId = chatId;
            Version = version;
        }

        public long ChatId { get; private set; }
        public int Version { get; private set; }
    }

    public sealed class UpdateReadHistoryInbox : Update
    {
        public const uint TlConstructorId = 0x9c974fdfu;

        public UpdateReadHistoryInbox(string peerKey, int maxId, int pts, int ptsCount)
            : base(TlConstructorId)
        {
            PeerKey = peerKey ?? string.Empty;
            MaxId = maxId;
            Pts = pts;
            PtsCount = ptsCount;
        }

        public string PeerKey { get; private set; }
        public int MaxId { get; private set; }
        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
    }

    public sealed class UpdateReadHistoryOutbox : Update
    {
        public const uint TlConstructorId = 0x2f2f21bfu;

        public UpdateReadHistoryOutbox(string peerKey, int maxId, int pts, int ptsCount)
            : base(TlConstructorId)
        {
            PeerKey = peerKey ?? string.Empty;
            MaxId = maxId;
            Pts = pts;
            PtsCount = ptsCount;
        }

        public string PeerKey { get; private set; }
        public int MaxId { get; private set; }
        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
    }

    public sealed class UpdateReadChannelInbox : Update
    {
        public const uint TlConstructorId = 0x922e6e10u;

        public UpdateReadChannelInbox(long channelId, int maxId, int stillUnreadCount, int pts)
            : base(TlConstructorId)
        {
            ChannelId = channelId;
            MaxId = maxId;
            StillUnreadCount = stillUnreadCount;
            Pts = pts;
        }

        public long ChannelId { get; private set; }
        public int MaxId { get; private set; }
        public int StillUnreadCount { get; private set; }
        public int Pts { get; private set; }
    }

    public sealed class UpdateReadChannelOutbox : Update
    {
        public const uint TlConstructorId = 0xb75f99a9u;

        public UpdateReadChannelOutbox(long channelId, int maxId) : base(TlConstructorId)
        {
            ChannelId = channelId;
            MaxId = maxId;
        }

        public long ChannelId { get; private set; }
        public int MaxId { get; private set; }
    }

    public sealed class UpdateNotifySettings : Update
    {
        public const uint TlConstructorId = 0xbec268efu;

        public UpdateNotifySettings(string peerKey, bool? showPreviews, bool? silent, int muteUntil)
            : base(TlConstructorId)
        {
            PeerKey = peerKey ?? string.Empty;
            ShowPreviews = showPreviews;
            Silent = silent;
            MuteUntil = muteUntil;
        }

        public string PeerKey { get; private set; }
        public bool? ShowPreviews { get; private set; }
        public bool? Silent { get; private set; }
        public int MuteUntil { get; private set; }
    }

    public sealed class UpdateUserName : Update
    {
        public const uint TlConstructorId = 0x1bfbd823u;

        public UpdateUserName(long userId, string firstName, string lastName, string username)
            : base(TlConstructorId)
        {
            UserId = userId;
            FirstName = firstName ?? string.Empty;
            LastName = lastName ?? string.Empty;
            Username = username ?? string.Empty;
        }

        public long UserId { get; private set; }
        public string FirstName { get; private set; }
        public string LastName { get; private set; }
        public string Username { get; private set; }
    }

    public sealed class UpdateUserPhone : Update
    {
        public const uint TlConstructorId = 0x05492a13u;

        public UpdateUserPhone(long userId, string phone) : base(TlConstructorId)
        {
            UserId = userId;
            Phone = phone ?? string.Empty;
        }

        public long UserId { get; private set; }
        public string Phone { get; private set; }
    }

    public sealed class UpdateUserPhoto : Update
    {
        // updateUserPhoto#f227868c (legacy) / a layer-specific variant; constructor id used as discriminator only.
        public const uint TlConstructorId = 0xf227868cu;

        public UpdateUserPhoto(long userId, ProfilePhotoSummary photo) : base(TlConstructorId)
        {
            UserId = userId;
            Photo = photo ?? ProfilePhotoSummary.Empty;
        }

        public long UserId { get; private set; }
        public ProfilePhotoSummary Photo { get; private set; }
    }

    public sealed class UpdateConfig : Update
    {
        public const uint TlConstructorId = 0xa229dd06u;
        public UpdateConfig() : base(TlConstructorId) { }
    }

    /// <summary>
    /// updateMessageReactions#5e1b3cb8 peer:Peer msg_id:int top_msg_id:flags.0?int reactions:MessageReactions
    /// — the server reports a change in the reaction set on a message
    /// (someone reacted, removed, or another logged-in session of ours
    /// updated). We surface only the peer + message id + a coarse
    /// summary count; rendering the actual emoji set requires the full
    /// MessageReactions sub-tree which Sync does not decode in v1.
    /// </summary>
    public sealed class UpdateMessageReactions : Update
    {
        public const uint TlConstructorId = 0x5e1b3cb8u;

        public UpdateMessageReactions(string peerKey, int messageId)
            : base(TlConstructorId)
        {
            PeerKey = peerKey ?? string.Empty;
            MessageId = messageId;
        }

        public string PeerKey { get; private set; }
        public int MessageId { get; private set; }
    }

    /// <summary>
    /// The server signals "I have explicitly bumped pts on you" — typically because
    /// account.updateStatus or a similar side-channel touched our cursor. Client
    /// must call updates.getDifference to discover what changed.
    /// </summary>
    public sealed class UpdatePtsChanged : Update
    {
        public const uint TlConstructorId = 0x3354678fu;
        public UpdatePtsChanged() : base(TlConstructorId) { }
    }

    /// <summary>
    /// Umbrella signal for the two TL constructors that say "channel
    /// <c>X</c>'s pts moved without
    /// telling you the messages — refetch via updates.getChannelDifference":
    /// <list type="bullet">
    ///   <item><c>updateChannel#635b4c09 channel_id:long</c> — fires when
    ///     the server tickles the channel for any reason (member count
    ///     change, settings tweak, OR a missed message that landed
    ///     out-of-band). It is the SOLE update many channel pushes
    ///     deliver, so without handling it our channels go silent.</item>
    ///   <item><c>updateChannelTooLong#108d941f flags:# channel_id:long
    ///     pts:flags.0?int</c> — server explicitly tells us our channel
    ///     pts is too far behind to incrementally diff; same recovery
    ///     (getChannelDifference) but expect a CHANNEL_DIFF_TOO_LONG
    ///     response that triggers reseed.</item>
    /// </list>
    /// SyncState routes both into <c>needsChannelDiff</c> so the
    /// application layer fires the recovery RPC on the next pump.
    /// </summary>
    public sealed class UpdateChannelTouched : Update
    {
        public UpdateChannelTouched(long channelId, uint constructorId)
            : base(constructorId)
        {
            ChannelId = channelId;
        }

        public long ChannelId { get; private set; }
    }

    /// <summary>
    /// Fallback for any TL Update constructor we don't decode. The raw body is
    /// preserved so a future build can decode it without re-fetching from the
    /// wire (or an out-of-band tool can inspect what was received).
    /// </summary>
    public sealed class UpdateUnsupported : Update
    {
        public UpdateUnsupported(uint constructorId, byte[] rawBody) : base(constructorId)
        {
            RawBody = rawBody ?? new byte[0];
        }

        public byte[] RawBody { get; private set; }
    }
}
