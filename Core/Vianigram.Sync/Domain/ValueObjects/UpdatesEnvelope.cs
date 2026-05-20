// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Discriminated union over the TL "Updates" supertype:
    ///
    ///   updates#74ae4240               → <see cref="UpdatesEnvelopeFull"/> (also covers updatesCombined#725b04c3)
    ///   updateShort#78d4dec1           → <see cref="UpdatesEnvelopeShort"/>
    ///   updateShortMessage#313bc7f8    → <see cref="UpdatesEnvelopeShortMessage"/>
    ///   updateShortChatMessage#4d6deea5→ <see cref="UpdatesEnvelopeShortMessage"/> (kind = ChatMessage)
    ///   updateShortSentMessage#9015e101→ <see cref="UpdatesEnvelopeShortSent"/>
    ///   updatesTooLong#e317af7e        → <see cref="UpdatesTooLong"/>
    ///
    /// Sync's TlDecoder produces one of these from a raw TL byte buffer; the
    /// SyncState aggregate then folds it into pts/qts/seq/date transitions and
    /// derived events.
    /// </summary>
    public abstract class UpdatesEnvelope
    {
        protected UpdatesEnvelope(uint constructorId)
        {
            ConstructorId = constructorId;
        }

        public uint ConstructorId { get; private set; }
    }

    /// <summary>
    /// updates#74ae4240 / updatesCombined#725b04c3 — a list of typed updates plus
    /// the user/chat hydration set and (date, seq, [seq_start]).
    /// </summary>
    public sealed class UpdatesEnvelopeFull : UpdatesEnvelope
    {
        public const uint TlConstructorIdUpdates = 0x74ae4240u;
        public const uint TlConstructorIdUpdatesCombined = 0x725b04c3u;

        public UpdatesEnvelopeFull(
            uint constructorId,
            IList<Update> updates,
            IList<UserStub> users,
            IList<ChatStub> chats,
            int date,
            int seq,
            int seqStart)
            : base(constructorId)
        {
            Updates = updates ?? new List<Update>(0);
            Users = users ?? new List<UserStub>(0);
            Chats = chats ?? new List<ChatStub>(0);
            Date = date;
            Seq = seq;
            SeqStart = seqStart;
        }

        public IList<Update> Updates { get; private set; }
        public IList<UserStub> Users { get; private set; }
        public IList<ChatStub> Chats { get; private set; }
        public int Date { get; private set; }
        public int Seq { get; private set; }

        /// <summary>For updatesCombined; otherwise equal to Seq.</summary>
        public int SeqStart { get; private set; }
    }

    /// <summary>
    /// updateShort#78d4dec1 — single inline update without users/chats.
    /// </summary>
    public sealed class UpdatesEnvelopeShort : UpdatesEnvelope
    {
        public const uint TlConstructorId = 0x78d4dec1u;

        public UpdatesEnvelopeShort(Update update, int date) : base(TlConstructorId)
        {
            Update = update;
            Date = date;
        }

        public Update Update { get; private set; }
        public int Date { get; private set; }
    }

    public enum ShortMessageKind
    {
        Private = 0,
        ChatMessage = 1
    }

    /// <summary>
    /// updateShortMessage / updateShortChatMessage — server-issued shortcut
    /// when only a single new message was delivered. Payload is essentially an
    /// inlined message + pts cursor.
    /// </summary>
    public sealed class UpdatesEnvelopeShortMessage : UpdatesEnvelope
    {
        public const uint TlConstructorIdPrivate = 0x313bc7f8u;
        public const uint TlConstructorIdChat = 0x4d6deea5u;

        public UpdatesEnvelopeShortMessage(
            uint constructorId,
            ShortMessageKind kind,
            int messageId,
            long fromUserId,
            long peerOrChatId,
            string message,
            int pts,
            int ptsCount,
            int date,
            bool isOutgoing,
            int replyToMessageId)
            : base(constructorId)
        {
            Kind = kind;
            MessageId = messageId;
            FromUserId = fromUserId;
            PeerOrChatId = peerOrChatId;
            Message = message ?? string.Empty;
            Pts = pts;
            PtsCount = ptsCount;
            Date = date;
            IsOutgoing = isOutgoing;
            ReplyToMessageId = replyToMessageId;
        }

        public ShortMessageKind Kind { get; private set; }
        public int MessageId { get; private set; }
        public long FromUserId { get; private set; }

        /// <summary>For Private: the other user's id. For ChatMessage: the chat_id.</summary>
        public long PeerOrChatId { get; private set; }
        public string Message { get; private set; }
        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public int Date { get; private set; }
        public bool IsOutgoing { get; private set; }
        public int ReplyToMessageId { get; private set; }
    }

    /// <summary>
    /// updateShortSentMessage#9015e101 — the server's response to messages.sendMessage,
    /// returned as an Updates type. Carries the new server-side id for the
    /// optimistic local message and the pts increment.
    /// </summary>
    public sealed class UpdatesEnvelopeShortSent : UpdatesEnvelope
    {
        public const uint TlConstructorId = 0x9015e101u;

        public UpdatesEnvelopeShortSent(
            int messageId,
            int pts,
            int ptsCount,
            int date,
            bool isOutgoing)
            : base(TlConstructorId)
        {
            MessageId = messageId;
            Pts = pts;
            PtsCount = ptsCount;
            Date = date;
            IsOutgoing = isOutgoing;
        }

        public int MessageId { get; private set; }
        public int Pts { get; private set; }
        public int PtsCount { get; private set; }
        public int Date { get; private set; }
        public bool IsOutgoing { get; private set; }
    }

    /// <summary>
    /// updatesTooLong#e317af7e — server tells us our cursor is too stale; the
    /// caller must invoke updates.getDifference (common box) to recover. There
    /// is no further payload.
    /// </summary>
    public sealed class UpdatesTooLong : UpdatesEnvelope
    {
        public const uint TlConstructorId = 0xe317af7eu;
        public static readonly UpdatesTooLong Instance = new UpdatesTooLong();
        private UpdatesTooLong() : base(TlConstructorId) { }
    }
}
