// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Messages.Domain.Entities;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Infrastructure
{
    /// <summary>
    /// TL deserializers for the response shapes the Messages context expects.
    /// Minimal: enough fields to build a domain Message and a getHistory page.
    /// The native TL stack remains the production decoder; this is the path
    /// used by the in-memory adapter.
    ///
    /// Constructor IDs (TL layer 214):
    ///
    ///   updateShortSentMessage    0x9015e101
    ///   updates                   0x74ae4240
    ///   message                   0x76352c8d   (full boxed message, with flags)
    ///   messageEmpty              0x90a6ca84
    ///   messageService            0x2b085862
    ///   messages.messages         0x8c718e87
    ///   messages.messagesSlice    0x3a54685e
    ///   messages.channelMessages  0xc776ba4e
    /// </summary>
    internal static class TlDecoder
    {
        public const uint CtorUpdateShortSentMessage = 0x9015e101u;
        // Layer 214: message#9815cec8 (was 0x76352c8d) — many additional
        // optional fields (offline, factcheck, effect, quick_reply, etc.).
        // The current reader only inspects identifying fields; unknown
        // optional bits are skipped via flag-aware traversal.
        public const uint CtorMessage = 0x9815cec8u;
        public const uint CtorMessageEmpty = 0x90a6ca84u;
        // messageService variants. The layer-214 ctor has flags2; legacy
        // ones don't. Both shapes are accepted; we branch on _hasFlags2 in
        // ReadOneMessage. The call-service legacy variant 0x1f1c25e9 is a
        // separate ctor — it appears in DMs that contain VoIP call
        // notifications.
        public const uint CtorMessageService = 0x7a800e0au;          // layer 214 (flags2)
        public const uint CtorMessageServiceLegacy = 0x2b085862u;    // legacy (no flags2)
        public const uint CtorMessageServiceCall = 0x1f1c25e9u;      // legacy call svc (no flags2)
        public const uint CtorMessagesMessages = 0x8c718e87u;
        public const uint CtorMessagesMessagesSlice = 0x3a54685eu;
        // Layer 214 introduces a richer messages.messagesSlice variant with
        // optional next_rate / offset_id_offset / search_flood fields under
        // the same flags word — same shape as the older slice, new ctor.
        public const uint CtorMessagesMessagesSlice214 = 0x762b263du;
        public const uint CtorMessagesChannelMessages = 0xc776ba4eu;
        public const uint CtorVector = 0x1cb5c415u;

        /// <summary>
        /// Try to decode a <c>messages.sendMessage</c> response. The simplest
        /// shape is <c>updateShortSentMessage</c>, which carries an int id and
        /// a date. The full <c>updates</c> envelope is more involved; for MVP
        /// we accept both an updateShortSentMessage and a stripped-down
        /// updates payload that contains a single message constructor.
        /// </summary>
        public static bool TryDecodeSendMessageResponse(byte[] payload, out long serverId, out DateTime serverDate)
        {
            serverId = 0;
            serverDate = DateTime.UtcNow;
            if (payload == null || payload.Length < 4) return false;

            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    uint ctor = r.ReadUInt32();
                    if (ctor == CtorUpdateShortSentMessage)
                    {
                        // flags : int, id : int, pts : int, pts_count : int, date : int, ...
                        r.ReadUInt32();      // flags
                        serverId = r.ReadInt32();
                        r.ReadInt32();       // pts
                        r.ReadInt32();       // pts_count
                        int date = r.ReadInt32();
                        serverDate = FromUnix(date);
                        return true;
                    }

                    // Fallback: try to read a bare message ctor with a minimal layout.
                    if (ctor == CtorMessage || ctor == CtorMessageService)
                    {
                        r.ReadUInt32();      // flags
                        serverId = r.ReadInt32();
                        // We do not have enough context to decode the full body
                        // in the bare case; return success with current time.
                        serverDate = DateTime.UtcNow;
                        return true;
                    }

                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Decode <c>messages.messages | messages.messagesSlice |
        /// messages.channelMessages</c> into domain Message entities.
        /// </summary>
        public static bool TryDecodeMessages(string peerKey, byte[] payload, out IList<Message> messages, out bool hasMore)
        {
            messages = new List<Message>();
            hasMore = false;
            if (payload == null || payload.Length < 4) return false;

            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    uint ctor = r.ReadUInt32();

                    int totalCount = -1;
                    if (ctor == CtorMessagesMessagesSlice)
                    {
                        // messages.messagesSlice#3a54685e flags:#
                        //   inexact:flags.1?true count:int
                        //   next_rate:flags.0?int offset_id_offset:flags.2?int
                        //   messages:Vector<Message> chats:Vector<Chat> users:Vector<User>
                        uint flags = r.ReadUInt32();
                        totalCount = r.ReadInt32();
                        if ((flags & (1u << 0)) != 0) r.ReadInt32(); // next_rate
                        if ((flags & (1u << 2)) != 0) r.ReadInt32(); // offset_id_offset
                        hasMore = true;
                    }
                    else if (ctor == CtorMessagesMessagesSlice214)
                    {
                        // Layer-214 evolution: same prefix as 0x3a54685e
                        // plus an optional search_flood:flags.3?SearchFlood
                        // sub-object. We don't model search_flood; if its
                        // flag is set we surface a decode failure rather
                        // than read garbage. In practice it only appears
                        // in messages.search responses, never in
                        // messages.getHistory.
                        uint flags = r.ReadUInt32();
                        totalCount = r.ReadInt32();
                        if ((flags & (1u << 0)) != 0) r.ReadInt32(); // next_rate
                        if ((flags & (1u << 2)) != 0) r.ReadInt32(); // offset_id_offset
                        if ((flags & (1u << 3)) != 0) return false;  // search_flood (unmodelled)
                        hasMore = true;
                    }
                    else if (ctor == CtorMessagesChannelMessages)
                    {
                        // messages.channelMessages#c776ba4e flags:#
                        //   inexact:flags.1?true pts:int count:int
                        //   offset_id_offset:flags.2?int messages:Vector<Message>
                        //   topics:Vector<ForumTopic> chats:Vector<Chat> users:Vector<User>
                        uint flags = r.ReadUInt32();
                        r.ReadInt32();                               // pts
                        totalCount = r.ReadInt32();
                        if ((flags & (1u << 2)) != 0) r.ReadInt32(); // offset_id_offset
                        hasMore = true;
                    }
                    else if (ctor != CtorMessagesMessages)
                    {
                        return false;
                    }

                    // messages : Vector<Message>
                    uint vec = r.ReadUInt32();
                    if (vec != CtorVector) return false;
                    int n = r.ReadInt32();
                    // Scan-skip recovery loop: when ReadOneMessage hits a
                    // trailing field (media / entities / reactions / etc.)
                    // it can't safely walk past, we throw, capture any
                    // partial result, and then sweep the byte stream
                    // forward at 4-byte alignment for the next message ctor.
                    // Without this we lost ~49 of every 50 messages in a
                    // typical channel page (each message's trailing fields
                    // would abort the whole vector). The trade-off is a
                    // tiny window for a false-positive ctor match; in
                    // practice the scan only walks ~50-500 bytes between
                    // real messages so the collision odds (~1 in 4B per
                    // offset) are negligible.
                    int skipBudget = n + 8;
                    int parseFailures = 0;
                    for (int i = 0; i < n; i++)
                    {
                        // Bail-out before reading the ctor when we've fallen
                        // off the end of the buffer. Without this guard the
                        // BinaryReader throws ArgumentOutOfRangeException
                        // and the debugger logs a first-chance exception.
                        if (ms.Position >= ms.Length) break;

                        MessageReadResult res;
                        try
                        {
                            res = ReadOneMessage(peerKey, r, ms);
                        }
                        catch (Exception ex)
                        {
                            // Genuine parse error (cursor lands in garbage,
                            // unexpected EOF inside a string, etc.). Log
                            // once so it's visible above the noise of
                            // benign bail-outs, then try to recover.
                            parseFailures++;
                            if (parseFailures == 1)
                            {
                                Vianigram.Kernel.Logging.EarlyLog.Write(
                                    "Messages.TlDecoder",
                                    "ReadOneMessage threw " + ex.GetType().Name +
                                    ": " + ex.Message + " at pos=" + ms.Position +
                                    " (decoded=" + messages.Count + " of " + n + ")");
                            }
                            if (skipBudget-- <= 0) break;
                            if (!TryAdvanceToNextMessageCtor(r, ms))
                                break;
                            continue;
                        }

                        if (res.Message != null) messages.Add(res.Message);

                        if (res.NeedsBail)
                        {
                            // The message decoded fine but its trailing
                            // fields (media / entities / reactions / etc.)
                            // are too rich to skim. Scan forward for the
                            // next message ctor and continue without
                            // throwing — keeps the debug output clean and
                            // avoids per-message exception allocations.
                            if (skipBudget-- <= 0) break;
                            if (!TryAdvanceToNextMessageCtor(r, ms))
                                break;
                        }
                    }

                    // The trailing chats/users vectors are intentionally not
                    // consumed here — the in-memory adapter discards anything
                    // past the messages vector. Production decoder lives in
                    // Vianigram.Core.Tl.

                    if (totalCount < 0) hasMore = false;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Result of a single <see cref="ReadOneMessage"/> call. Carries the
        /// decoded message (if any) plus a flag telling the outer loop
        /// whether to scan-skip past trailing bytes we couldn't model. We
        /// return-by-value instead of throwing because TL responses contain
        /// dozens of messages with trailing fields per page — using
        /// exceptions for control flow lit up the debugger output and cost
        /// real CPU on every parse.
        /// </summary>
        private struct MessageReadResult
        {
            public Message Message;
            public bool NeedsBail;
            public static MessageReadResult Ok(Message m) { return new MessageReadResult { Message = m, NeedsBail = false }; }
            public static MessageReadResult Bail(Message m) { return new MessageReadResult { Message = m, NeedsBail = true }; }
            public static MessageReadResult Empty() { return new MessageReadResult { Message = null, NeedsBail = false }; }
        }

        // Layer-214 message#9815cec8 partial decoder. The wire layout (only
        // fields we model are listed; everything else is skipped or used as a
        // bail-trigger) is:
        //
        //   flags:#  flags2:#  id:int
        //   from_id:flags.8?Peer                   (12B if present)
        //   from_boosts_applied:flags.29?int       (4B if present)
        //   peer_id:Peer                           (12B always)
        //   saved_peer_id:flags.28?Peer            (12B if present)
        //   fwd_from:flags.2?MessageFwdHeader      ← skipped via SkipMessageFwdHeader
        //   via_bot_id:flags.11?long
        //   via_business_bot_id:flags2.0?long
        //   reply_to:flags.3?MessageReplyHeader    ← skipped + reply_to_msg_id captured
        //   date:int
        //   message:string
        //   media:flags.9?MessageMedia             ← ctor read, body left to scan-skip
        //   ...(entities/views/edit_date/etc — left to scan-skip)
        //
        // When *any* of media / entities / reactions / etc. is present, we
        // return MessageReadResult.Bail(message) — the message itself is
        // captured, but the caller is told to skip forward to the next
        // message ctor before reading again.
        private static MessageReadResult ReadOneMessage(string peerKey, BinaryReader r, MemoryStream ms)
        {
            uint ctor = r.ReadUInt32();
            switch (ctor)
            {
                case CtorMessageEmpty:
                {
                    r.ReadUInt32(); // flags
                    r.ReadInt32();  // id
                    return MessageReadResult.Empty();
                }
                case CtorMessage:
                {
                    uint flags = r.ReadUInt32();
                    uint flags2 = r.ReadUInt32();
                    int id = r.ReadInt32();

                    // A message id MUST be positive in the wire format. If we
                    // see id <= 0 here it means scan-skip recovery landed on a
                    // false-positive ctor match (the next 4 bytes were not a
                    // real message but a payload window that happened to look
                    // like 0x9815cec8). Drop the slot silently — the caller's
                    // scan-skip will move forward to the next candidate. We
                    // can't keep parsing this "message" because Message.FromServer
                    // throws ArgumentOutOfRangeException on non-positive ids,
                    // which the outer error handler would log as a real
                    // parse failure.
                    if (id <= 0) return MessageReadResult.Bail(null);

                    long? fromId = null;
                    if ((flags & (1u << 8)) != 0)
                    {
                        // from_id : Peer (boxed)
                        fromId = ReadBoxedPeerId(r);
                    }

                    if ((flags & (1u << 29)) != 0) r.ReadInt32(); // from_boosts_applied

                    // peer_id : Peer (always present)
                    ReadBoxedPeerId(r);

                    if ((flags & (1u << 28)) != 0) ReadBoxedPeerId(r); // saved_peer_id

                    // fwd_from : MessageFwdHeader#4e4df4bb. Most channel posts
                    // are forwards, so handling this is the difference between
                    // an empty page and a useful one.
                    if ((flags & (1u << 2)) != 0)
                    {
                        SkipMessageFwdHeader(r);
                    }

                    if ((flags & (1u << 11)) != 0) r.ReadInt64();   // via_bot_id
                    if ((flags2 & (1u << 0)) != 0) r.ReadInt64();   // via_business_bot_id

                    long? replyTo = null;
                    if ((flags & (1u << 3)) != 0)
                    {
                        replyTo = SkipMessageReplyHeaderAndExtractMsgId(r);
                    }

                    int date = r.ReadInt32();
                    string body = ReadString(r);
                    bool hasReplyMarkup = (flags & (1u << 6)) != 0;
                    bool hasEntities = (flags & (1u << 7)) != 0;

                    // Media kind identification: when flag.9 is set the next
                    // 4 bytes are the MessageMedia ctor. We *don't* try to
                    // decode the rest of the media object (each variant has
                    // its own rich shape — Photo with PhotoSizes,
                    // MessageMediaDocument with DocumentAttribute vector,
                    // etc.) — but knowing the ctor is enough to pick the
                    // right bubble in the UI. We replace the plain-text
                    // content with the matching MessageContent stub.
                    bool hasMedia = (flags & (1u << 9)) != 0;
                    MessageContent content;
                    if (hasMedia)
                    {
                        uint mediaCtor;
                        try { mediaCtor = r.ReadUInt32(); }
                        catch { mediaCtor = 0; }
                        content = MapMediaCtorToContent(mediaCtor, body, r, ms);
                    }
                    else
                    {
                        content = new MessageContentText(body);
                    }

                    bool consumedEntities = false;
                    if (!hasMedia && !hasReplyMarkup && hasEntities)
                    {
                        IList<MessageEntity> entities = ReadMessageEntities(r);
                        content = new MessageContentText(body, entities);
                        consumedEntities = true;
                    }

                    // Any *additional* trailing field beyond the text/media
                    // pair signals data we don't model — bail so the
                    // scan-skip recovery in TryDecodeMessages can find the
                    // next message header.
                    bool hasTrailing = hasMedia
                     || hasReplyMarkup                // reply_markup
                     || (hasEntities && !consumedEntities)
                     || (flags & (1u << 10)) != 0    // views/forwards
                     || (flags & (1u << 23)) != 0    // replies
                     || (flags & (1u << 15)) != 0    // edit_date
                     || (flags & (1u << 16)) != 0    // post_author
                     || (flags & (1u << 17)) != 0    // grouped_id
                     || (flags & (1u << 20)) != 0    // reactions
                     || (flags & (1u << 22)) != 0    // restriction_reason
                     || (flags & (1u << 25)) != 0    // ttl_period
                     || (flags & (1u << 30)) != 0    // quick_reply_shortcut_id
                     || (flags2 & (1u << 2)) != 0    // effect
                     || (flags2 & (1u << 3)) != 0    // factcheck
                     || (flags2 & (1u << 5)) != 0    // report_delivery_until_date
                     || (flags2 & (1u << 6)) != 0    // paid_message_stars
                     || (flags2 & (1u << 7)) != 0;   // suggested_post

                    bool isOut = (flags & (1u << 1)) != 0;
                    var message = Message.FromServer(
                        peerKey,
                        id,
                        fromId,
                        FromUnix(date),
                        content,
                        replyTo,
                        isOut,
                        DeliveryState.Delivered);

                    return hasTrailing
                        ? MessageReadResult.Bail(message)
                        : MessageReadResult.Ok(message);
                }
                case CtorMessageService:
                case CtorMessageServiceLegacy:
                case CtorMessageServiceCall:
                {
                    // messageService — three accepted ctors. The layer-214
                    // ctor (0x7a800e0a) carries a flags2 word; the legacy
                    // variants (0x2b085862, 0x1f1c25e9) do not. After the
                    // flags-prefix the layout is identical: id + optional
                    // from_id + peer_id + optional reply_to + date + action.
                    // The action union has dozens of variants we don't model,
                    // so we capture timestamp + placeholder body and ask
                    // the caller to scan-skip past the action bytes.
                    bool hasFlags2 = (ctor == CtorMessageService);
                    uint flags = r.ReadUInt32();
                    if (hasFlags2) r.ReadUInt32();              // flags2
                    int id = r.ReadInt32();
                    // Same false-positive-ctor guard as the regular message
                    // branch — see comment there for rationale.
                    if (id <= 0) return MessageReadResult.Bail(null);
                    if ((flags & (1u << 8)) != 0) ReadBoxedPeerId(r);
                    ReadBoxedPeerId(r);
                    if ((flags & (1u << 28)) != 0) ReadBoxedPeerId(r); // saved_peer_id
                    if ((flags & (1u << 3)) != 0) SkipMessageReplyHeaderAndExtractMsgId(r);
                    int date = r.ReadInt32();
                    var svc = Message.FromServer(
                        peerKey,
                        id,
                        null,
                        FromUnix(date),
                        new MessageContentService(ServiceMessageKind.Unknown, string.Empty),
                        null,
                        false);
                    return MessageReadResult.Bail(svc);
                }
                default:
                    return MessageReadResult.Empty();
            }
        }

        // messageFwdHeader#4e4df4bb (layer 214) flags:#
        //   from_id:flags.0?Peer  from_name:flags.5?string  date:int
        //   channel_post:flags.2?int  post_author:flags.3?string
        //   saved_from_peer:flags.4?Peer  saved_from_msg_id:flags.4?int
        //   saved_from_id:flags.8?Peer  saved_from_name:flags.9?string
        //   saved_date:flags.10?int  psa_type:flags.6?string
        private static void SkipMessageFwdHeader(BinaryReader r)
        {
            r.ReadUInt32();                                           // ctor (typically 0x4e4df4bb)
            uint flags = r.ReadUInt32();
            if ((flags & (1u << 0)) != 0) ReadBoxedPeerId(r);          // from_id
            if ((flags & (1u << 5)) != 0) ReadString(r);               // from_name
            r.ReadInt32();                                             // date (always)
            if ((flags & (1u << 2)) != 0) r.ReadInt32();               // channel_post
            if ((flags & (1u << 3)) != 0) ReadString(r);               // post_author
            if ((flags & (1u << 4)) != 0)
            {
                ReadBoxedPeerId(r);                                    // saved_from_peer
                r.ReadInt32();                                         // saved_from_msg_id
            }
            if ((flags & (1u << 8)) != 0) ReadBoxedPeerId(r);          // saved_from_id
            if ((flags & (1u << 9)) != 0) ReadString(r);               // saved_from_name
            if ((flags & (1u << 10)) != 0) r.ReadInt32();              // saved_date
            if ((flags & (1u << 6)) != 0) ReadString(r);               // psa_type
        }

        // messageReplyHeader#6917560b (layer 214). We skip the structurally
        // simple fields and bail on rich sub-objects (reply_media,
        // quote_entities, reply_from is the FwdHeader so safe to recurse).
        // Returns reply_to_msg_id when present, otherwise null.
        private static long? SkipMessageReplyHeaderAndExtractMsgId(BinaryReader r)
        {
            r.ReadUInt32();                                            // ctor
            uint flags = r.ReadUInt32();
            long? msgId = null;
            if ((flags & (1u << 4)) != 0) msgId = r.ReadInt32();       // reply_to_msg_id
            if ((flags & (1u << 0)) != 0) ReadBoxedPeerId(r);          // reply_to_peer_id
            if ((flags & (1u << 5)) != 0) SkipMessageFwdHeader(r);     // reply_from
            if ((flags & (1u << 8)) != 0)
            {
                throw new InvalidDataException("reply_media in reply_to (rich variant)");
            }
            if ((flags & (1u << 1)) != 0) r.ReadInt32();               // reply_to_top_id
            if ((flags & (1u << 6)) != 0) ReadString(r);               // quote_text
            if ((flags & (1u << 7)) != 0)
            {
                throw new InvalidDataException("quote_entities in reply_to (rich variant)");
            }
            if ((flags & (1u << 10)) != 0) r.ReadInt32();              // quote_offset
            if ((flags & (1u << 11)) != 0) r.ReadInt32();              // todo_item_id
            return msgId;
        }

        private static long? ReadBoxedPeerId(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            switch (ctor)
            {
                case 0x59511722u: // peerUser#59511722 user_id:long
                case 0x36c6019au: // peerChat#36c6019a chat_id:long
                case 0xa2a5371eu: // peerChannel#a2a5371e channel_id:long
                    return r.ReadInt64();
                default:
                    // Unknown peer ctor — every Peer variant we know carries
                    // a single long, so consume the 8 bytes anyway to keep
                    // the surrounding cursor aligned. Returning null tells
                    // the caller we don't recognise the peer kind, but the
                    // wire offset remains correct.
                    r.ReadInt64();
                    return null;
            }
        }

        private static string ReadString(BinaryReader r)
        {
            byte first = r.ReadByte();
            int len;
            int consumed;
            if (first == 254)
            {
                int b0 = r.ReadByte();
                int b1 = r.ReadByte();
                int b2 = r.ReadByte();
                len = b0 | (b1 << 8) | (b2 << 16);
                consumed = 4 + len;
            }
            else
            {
                len = first;
                consumed = 1 + len;
            }

            var bytes = r.ReadBytes(len);
            int padding = (4 - (consumed % 4)) % 4;
            for (int i = 0; i < padding; i++) r.ReadByte();
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        private static byte[] ReadBytes(BinaryReader r)
        {
            byte first = r.ReadByte();
            int len;
            int consumed;
            if (first == 254)
            {
                int b0 = r.ReadByte();
                int b1 = r.ReadByte();
                int b2 = r.ReadByte();
                len = b0 | (b1 << 8) | (b2 << 16);
                consumed = 4 + len;
            }
            else
            {
                len = first;
                consumed = 1 + len;
            }

            byte[] bytes = r.ReadBytes(len);
            int padding = (4 - (consumed % 4)) % 4;
            for (int i = 0; i < padding; i++) r.ReadByte();
            return bytes;
        }

        private static IList<MessageEntity> ReadMessageEntities(BinaryReader r)
        {
            var result = new List<MessageEntity>();
            uint vec = r.ReadUInt32();
            if (vec != CtorVector) return result;
            int count = r.ReadInt32();
            if (count < 0 || count > 512) return result;

            for (int i = 0; i < count; i++)
            {
                MessageEntity e = ReadMessageEntity(r);
                if (e != null) result.Add(e);
            }
            return result;
        }

        private static void SkipEntityVector(BinaryReader r)
        {
            ReadMessageEntities(r);
        }

        private static MessageEntity ReadMessageEntity(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            int flags;
            int offset;
            int length;
            switch (ctor)
            {
                case CtorEntityBold:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Bold, offset, length);
                case CtorEntityItalic:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Italic, offset, length);
                case CtorEntityUnderline:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Underline, offset, length);
                case CtorEntityStrike:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Strike, offset, length);
                case CtorEntityCode:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Code, offset, length);
                case CtorEntityPre:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Pre, offset, length, null, ReadString(r), null);
                case CtorEntityUrl:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Url, offset, length);
                case CtorEntityTextUrl:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.TextUrl, offset, length, ReadString(r));
                case CtorEntityMention:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Mention, offset, length);
                case CtorEntityHashtag:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Hashtag, offset, length);
                case CtorEntityBotCommand:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.BotCommand, offset, length);
                case CtorEntityEmail:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Email, offset, length);
                case CtorEntityPhone:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.PhoneNumber, offset, length);
                case CtorEntityCashtag:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Hashtag, offset, length);
                case CtorEntityMentionName:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    r.ReadInt64(); // user_id
                    return new MessageEntity(EntityKind.Mention, offset, length);
                case CtorEntitySpoiler:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.Spoiler, offset, length);
                case CtorEntityCustomEmoji:
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    long documentId = r.ReadInt64();
                    return new MessageEntity(EntityKind.CustomEmoji, offset, length, null, null,
                        documentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
                case CtorEntityBlockquote:
                    flags = r.ReadInt32();
                    offset = r.ReadInt32(); length = r.ReadInt32();
                    return new MessageEntity(EntityKind.BlockQuote, offset, length);
                default:
                    // Most entity variants are offset:int length:int. Consume
                    // that common tail so one new entity does not poison the
                    // whole message vector.
                    r.ReadInt32();
                    r.ReadInt32();
                    return null;
            }
        }

        private static void SkipPeerVector(BinaryReader r)
        {
            uint vec = r.ReadUInt32();
            if (vec != CtorVector) return;
            int count = r.ReadInt32();
            if (count < 0 || count > 1024) return;
            for (int i = 0; i < count; i++) ReadBoxedPeerId(r);
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static void SkipBytes(BinaryReader r, int n)
        {
            for (int i = 0; i < n; i++) r.ReadByte();
        }

        private static DateTime FromUnix(int unixSeconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);
        }

        // Layer-214 MessageMedia ctors. We only keep the prefix (4 bytes) on
        // the wire to identify the variant; the full body is left unparsed
        // (the scan-skip recovery in TryDecodeMessages walks past it). For
        // documents we run a bounded heuristic scan over the body looking for
        // DocumentAttribute ctors so we can pick the right bubble (voice vs
        // audio vs sticker vs video vs GIF vs file). See ParseDocumentMedia.
        private const uint CtorMessageMediaEmpty       = 0x3ded6320u;
        private const uint CtorMessageMediaPhoto       = 0x695150d7u;
        private const uint CtorMessageMediaPhotoLayer  = 0x6620ebc9u; // alt seen in layer 214
        private const uint CtorMessageMediaGeo         = 0x56e0d474u;
        private const uint CtorMessageMediaVenue       = 0x2ec0533fu;
        private const uint CtorMessageMediaContact     = 0x70322949u;
        private const uint CtorMessageMediaUnsupported = 0x9f84f49eu;
        private const uint CtorMessageMediaDocumentA   = 0x4cf4d864u; // older
        private const uint CtorMessageMediaDocumentB   = 0xdd570bd5u; // mid-layer
        private const uint CtorMessageMediaDocumentC   = 0x4cf4d72du; // layer 214
        private const uint CtorMessageMediaDocumentLegacy = 0x52d8ccd9u;
        private const uint CtorMessageMediaWebPage     = 0xddf10c3bu; // layer 214
        private const uint CtorMessageMediaWebPageOld  = 0xa32dd600u;
        private const uint CtorMessageMediaPoll        = 0x4bd6e798u;
        private const uint CtorMessageMediaDice        = 0x3f7ee58bu;
        private const uint CtorMessageMediaGame        = 0xfdb19008u;
        private const uint CtorMessageMediaInvoice     = 0xf6a548d3u;
        private const uint CtorMessageMediaGiveaway    = 0xdaad85b0u;
        private const uint CtorMessageMediaStory       = 0x68cb6283u;
        private const uint CtorMessageMediaGeoLive     = 0xb940c666u;

        private const uint CtorPhotoEmpty              = 0x2331b22du;
        private const uint CtorPhotoFull               = 0xfb197a65u;
        private const uint CtorPhotoSizeEmpty          = 0x0e17e23cu;
        private const uint CtorPhotoSize               = 0x75c78e60u;
        private const uint CtorPhotoStrippedSize       = 0xe0b0bc2eu;
        private const uint CtorPhotoSizeProgressive    = 0xfa3efb95u;
        private const uint CtorPhotoCachedSize         = 0x021e1ad6u;
        private const uint CtorPhotoPathSize           = 0xd8214d41u;

        // DocumentAttribute ctor ids (layer 214). The presence + flags of
        // these attributes inside a Document's `attributes:Vector<>` field
        // tells us whether the document is a voice note, audio track,
        // sticker, animation, video, or generic file.
        private const uint CtorDocAttrFilename   = 0x15590068u;
        private const uint CtorDocAttrImageSize  = 0x6c37c15cu;
        private const uint CtorDocAttrAnimated   = 0x11b58939u;
        private const uint CtorDocAttrSticker    = 0x6319d612u;
        private const uint CtorDocumentEmpty     = 0x36f8c871u;
        private const uint CtorDocument          = 0x8fd4c4d8u;
        private const uint CtorDocAttrVideo      = 0x43c57c48u;
        private const uint CtorDocAttrVideoLegacy = 0x0ef02ce6u;
        private const uint CtorDocAttrAudio      = 0x9852f9c6u;
        private const uint CtorDocAttrHasStickers = 0x9801d2f7u;
        private const uint CtorDocAttrCustomEmoji = 0xfd149899u;
        private const uint CtorVideoSize         = 0xde33b094u;
        private const uint CtorVideoSizeEmojiMarkup = 0xf85c413cu;
        private const uint CtorInputStickerSetEmpty = 0xffb62b95u;
        private const uint CtorInputStickerSetId = 0x9de7a269u;
        private const uint CtorInputStickerSetShortName = 0x861cc8a0u;
        private const uint CtorMaskCoords        = 0xaed6dbb2u;

        private const uint CtorTextWithEntities = 0x744694e0u;
        private const uint CtorPoll = 0x86e18161u;
        private const uint CtorPollAnswer = 0x6ca9c2e9u;
        private const uint CtorPollAnswerLayer214 = 0xff16e85eu;
        private const uint CtorPollResults = 0x7adf2420u;
        private const uint CtorPollAnswerVoters = 0x3b6ddad2u;
        private const uint CtorGeoPointEmpty = 0x1117dd5fu;
        private const uint CtorGeoPoint = 0xb2a2f663u;

        private const uint CtorEntityBold = 0xbd610bc9u;
        private const uint CtorEntityItalic = 0x826f8b60u;
        private const uint CtorEntityCode = 0x28a20571u;
        private const uint CtorEntityPre = 0x73924be0u;
        private const uint CtorEntityUrl = 0x6ed02538u;
        private const uint CtorEntityTextUrl = 0x76a6d327u;
        private const uint CtorEntityMention = 0xfa04579du;
        private const uint CtorEntityHashtag = 0x6f635b0du;
        private const uint CtorEntityBotCommand = 0x6cef8ac7u;
        private const uint CtorEntityEmail = 0x64e475c2u;
        private const uint CtorEntityPhone = 0x9b69e34bu;
        private const uint CtorEntityCashtag = 0x4c4e743fu;
        private const uint CtorEntityMentionName = 0xdc7b1140u;
        private const uint CtorEntityUnderline = 0x9c4e7e8bu;
        private const uint CtorEntityStrike = 0xbf0693d4u;
        private const uint CtorEntitySpoiler = 0x32ca960fu;
        private const uint CtorEntityCustomEmoji = 0xc8cf05f8u;
        private const uint CtorEntityBlockquote = 0xf1ccaaacu;

        /// <summary>
        /// Converts a media ctor id to the matching domain
        /// <see cref="MessageContent"/> stub. For documents we additionally
        /// scan the body for <c>DocumentAttribute*</c> ctors so we can
        /// classify the document as voice / audio / sticker / animation /
        /// video / videoNote / file.
        /// </summary>
        private static MessageContent MapMediaCtorToContent(
            uint mediaCtor, string caption, BinaryReader r, MemoryStream ms)
        {
            switch (mediaCtor)
            {
                case CtorMessageMediaEmpty:
                    return new MessageContentText(caption);
                case CtorMessageMediaPhoto:
                case CtorMessageMediaPhotoLayer:
                    return ParsePhotoMedia(r, ms, caption);
                case CtorMessageMediaDocumentA:
                case CtorMessageMediaDocumentB:
                case CtorMessageMediaDocumentC:
                case CtorMessageMediaDocumentLegacy:
                    return ParseDocumentMedia(r, ms, caption);
                case CtorMessageMediaContact:
                    return ParseContactMedia(r, ms);
                case CtorMessageMediaGeo:
                case CtorMessageMediaGeoLive:
                    return ParseGeoMedia(r, ms);
                case CtorMessageMediaVenue:
                    return ParseVenueMedia(r, ms);
                case CtorMessageMediaPoll:
                    return ParsePollMedia(r, ms);
                case CtorMessageMediaWebPage:
                case CtorMessageMediaWebPageOld:
                    return BuildWebPageFromMessageText(caption);
                case CtorMessageMediaDice:
                case CtorMessageMediaGame:
                case CtorMessageMediaInvoice:
                case CtorMessageMediaGiveaway:
                case CtorMessageMediaStory:
                case CtorMessageMediaUnsupported:
                    return new MessageContentUnsupported("0x" + mediaCtor.ToString("x8"));
                default:
                    return new MessageContentUnsupported(mediaCtor == 0 ? "missing" : "0x" + mediaCtor.ToString("x8"));
            }
        }

        private static MessageContent ParsePhotoMedia(BinaryReader r, MemoryStream ms, string caption)
        {
            long start = ms.Position;
            try
            {
                int mediaFlags = r.ReadInt32();
                if ((mediaFlags & 1) == 0)
                    return new MessageContentPhoto(string.Empty, string.Empty, 0, 0, caption);

                uint photoCtor = r.ReadUInt32();
                if (photoCtor == CtorPhotoEmpty)
                {
                    long emptyId = r.ReadInt64();
                    TelegramMediaFile emptyFile = new TelegramMediaFile(
                        emptyId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        0L, null, 0, 0L, "image/jpeg", string.Empty, string.Empty, string.Empty);
                    return new MessageContentPhoto(string.Empty, string.Empty, 0, 0, caption,
                        null, emptyFile, null, CtorMessageMediaPhoto, null, null, null);
                }

                if (photoCtor != CtorPhotoFull)
                    throw new InvalidDataException("photo ctor");

                int photoFlags = r.ReadInt32();
                long photoId = r.ReadInt64();
                long accessHash = r.ReadInt64();
                byte[] fileReference = ReadBytes(r);
                r.ReadInt32(); // date

                var thumbnails = new List<MediaThumbnail>();
                int bestWidth = 0;
                int bestHeight = 0;
                long bestSize = 0;

                uint vectorCtor = r.ReadUInt32();
                if (vectorCtor == CtorVector)
                {
                    int count = r.ReadInt32();
                    if (count < 0 || count > 64) count = 0;
                    for (int i = 0; i < count; i++)
                    {
                        ReadPhotoSize(r, thumbnails, ref bestWidth, ref bestHeight, ref bestSize);
                    }
                }

                // photo.video_sizes:flags.1?Vector<VideoSize>. Rare for
                // chat previews; skip best-effort before dc_id.
                if ((photoFlags & (1 << 1)) != 0)
                    SkipBoxedObjectVector(r, 32);

                int dcId = 0;
                try { dcId = r.ReadInt32(); } catch { dcId = 0; }

                var file = new TelegramMediaFile(
                    photoId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    accessHash,
                    fileReference,
                    dcId,
                    bestSize,
                    "image/jpeg",
                    string.Empty,
                    string.Empty,
                    string.Empty);

                return new MessageContentPhoto(string.Empty, string.Empty, bestWidth, bestHeight, caption,
                    null, file, thumbnails, CtorMessageMediaPhoto, null, null, null);
            }
            catch
            {
                return new MessageContentPhoto(string.Empty, string.Empty, 0, 0, caption);
            }
            finally
            {
                ms.Position = start;
            }
        }

        private static void ReadPhotoSize(BinaryReader r, IList<MediaThumbnail> thumbnails,
            ref int bestWidth, ref int bestHeight, ref long bestSize)
        {
            uint ctor = r.ReadUInt32();
            string type;
            int width;
            int height;
            long size;
            byte[] bytes;

            switch (ctor)
            {
                case CtorPhotoSizeEmpty:
                    ReadString(r);
                    return;

                case CtorPhotoSize:
                    type = ReadString(r);
                    width = r.ReadInt32();
                    height = r.ReadInt32();
                    size = r.ReadInt32();
                    thumbnails.Add(new MediaThumbnail(type, width, height, size, string.Empty, string.Empty, null));
                    UpdateBestPhotoSize(width, height, size, ref bestWidth, ref bestHeight, ref bestSize);
                    return;

                case CtorPhotoSizeProgressive:
                    type = ReadString(r);
                    width = r.ReadInt32();
                    height = r.ReadInt32();
                    size = ReadLargestIntVectorValue(r);
                    thumbnails.Add(new MediaThumbnail(type, width, height, size, string.Empty, string.Empty, null));
                    UpdateBestPhotoSize(width, height, size, ref bestWidth, ref bestHeight, ref bestSize);
                    return;

                case CtorPhotoStrippedSize:
                    type = ReadString(r);
                    bytes = ReadBytes(r);
                    width = bytes != null && bytes.Length > 2 ? bytes[1] : 0;
                    height = bytes != null && bytes.Length > 2 ? bytes[2] : 0;
                    size = bytes != null ? bytes.Length : 0;
                    thumbnails.Add(new MediaThumbnail(type, width, height, size, string.Empty, string.Empty, bytes));
                    UpdateBestPhotoSize(width, height, size, ref bestWidth, ref bestHeight, ref bestSize);
                    return;

                case CtorPhotoCachedSize:
                    type = ReadString(r);
                    width = r.ReadInt32();
                    height = r.ReadInt32();
                    bytes = ReadBytes(r);
                    size = bytes != null ? bytes.Length : 0;
                    thumbnails.Add(new MediaThumbnail(type, width, height, size, string.Empty, string.Empty, bytes));
                    UpdateBestPhotoSize(width, height, size, ref bestWidth, ref bestHeight, ref bestSize);
                    return;

                case CtorPhotoPathSize:
                    ReadString(r);
                    ReadBytes(r);
                    return;

                default:
                    throw new InvalidDataException("photo size ctor");
            }
        }

        private static long ReadLargestIntVectorValue(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector) return 0;
            int count = r.ReadInt32();
            if (count < 0 || count > 64) return 0;

            long largest = 0;
            for (int i = 0; i < count; i++)
            {
                int value = r.ReadInt32();
                if (value > largest) largest = value;
            }
            return largest;
        }

        private static void UpdateBestPhotoSize(int width, int height, long size,
            ref int bestWidth, ref int bestHeight, ref long bestSize)
        {
            long score = width > 0 && height > 0 ? (long)width * height : size;
            long bestScore = bestWidth > 0 && bestHeight > 0 ? (long)bestWidth * bestHeight : bestSize;
            if (score < bestScore) return;

            bestWidth = width;
            bestHeight = height;
            bestSize = size;
        }

        private static void SkipBoxedObjectVector(BinaryReader r, int maxItems)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector) return;

            int count = r.ReadInt32();
            if (count < 0 || count > maxItems) return;
            for (int i = 0; i < count; i++)
                SkipBoxedObject(r, 512);
        }

        private static void SkipBoxedObject(BinaryReader r, int maxBytes)
        {
            r.ReadUInt32();
            long remaining = r.BaseStream.Length - r.BaseStream.Position;
            int skip = (int)Math.Min(Math.Max(0, remaining), maxBytes);
            if (skip > 0) r.BaseStream.Position += skip;
        }

        private static MessageContent ParseContactMedia(BinaryReader r, MemoryStream ms)
        {
            long start = ms.Position;
            try
            {
                string phone = ReadString(r);
                string first = ReadString(r);
                string last = ReadString(r);
                ReadString(r); // vcard
                long userId = r.ReadInt64();
                return new MessageContentContact(first, last, phone, userId == 0L ? (long?)null : userId);
            }
            catch
            {
                return new MessageContentContact(string.Empty, string.Empty, string.Empty, null);
            }
            finally
            {
                ms.Position = start;
            }
        }

        private static MessageContent ParseGeoMedia(BinaryReader r, MemoryStream ms)
        {
            long start = ms.Position;
            try
            {
                double lat, lon;
                if (TryReadGeoPoint(r, out lat, out lon))
                    return new MessageContentLocation(lat, lon, string.Empty, string.Empty);
            }
            catch { }
            finally
            {
                ms.Position = start;
            }
            return new MessageContentLocation(0.0, 0.0, string.Empty, string.Empty);
        }

        private static MessageContent ParseVenueMedia(BinaryReader r, MemoryStream ms)
        {
            long start = ms.Position;
            try
            {
                double lat, lon;
                TryReadGeoPoint(r, out lat, out lon);
                string title = ReadString(r);
                string address = ReadString(r);
                ReadString(r); // provider
                ReadString(r); // venue_id
                ReadString(r); // venue_type
                return new MessageContentLocation(lat, lon, title, address);
            }
            catch
            {
                return new MessageContentLocation(0.0, 0.0, "venue", string.Empty);
            }
            finally
            {
                ms.Position = start;
            }
        }

        private static MessageContent ParsePollMedia(BinaryReader r, MemoryStream ms)
        {
            long start = ms.Position;
            try
            {
                uint pollCtor = r.ReadUInt32();
                if (pollCtor != CtorPoll) throw new InvalidDataException("poll ctor");

                long pollId = r.ReadInt64();
                int flags = r.ReadInt32();
                bool closed = (flags & (1 << 0)) != 0;
                bool multiple = (flags & (1 << 2)) != 0;
                bool quiz = (flags & (1 << 3)) != 0;
                string question = ReadTextWithEntitiesString(r);

                var options = new List<PollOption>();
                uint vec = r.ReadUInt32();
                if (vec == CtorVector)
                {
                    int count = r.ReadInt32();
                    if (count < 0 || count > 64) count = 0;
                    for (int i = 0; i < count; i++)
                    {
                        PollOption option = ReadPollAnswer(r);
                        if (option != null) options.Add(option);
                    }
                }

                if ((flags & (1 << 4)) != 0) r.ReadInt32(); // close_period
                if ((flags & (1 << 5)) != 0) r.ReadInt32(); // close_date

                int totalVoters = 0;
                uint resultsCtor = r.ReadUInt32();
                if (resultsCtor == CtorPollResults)
                {
                    ApplyPollResults(r, options, out totalVoters);
                }

                return new MessageContentPoll(question, options, totalVoters, closed, multiple, quiz, pollId,
                    CtorMessageMediaPoll, null, null, null);
            }
            catch
            {
                return new MessageContentPoll(string.Empty, new string[0], 0, false);
            }
            finally
            {
                ms.Position = start;
            }
        }

        private static MessageContentWebPage BuildWebPageFromMessageText(string body)
        {
            string url = ExtractFirstUrl(body);
            string siteName = BuildSiteName(url);
            string displayUrl = BuildDisplayUrl(url);
            return new MessageContentWebPage(
                body,
                url,
                siteName,
                string.Empty,
                string.Empty,
                string.Empty,
                displayUrl,
                null);
        }

        private static string ExtractFirstUrl(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return string.Empty;

            string[] parts = text.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                string token = TrimUrlToken(parts[i]);
                if (string.IsNullOrEmpty(token)) continue;

                Uri uri;
                if (Uri.TryCreate(token, UriKind.Absolute, out uri) && IsLaunchableLinkScheme(uri.Scheme))
                    return token;

                if (token.IndexOf("://", StringComparison.Ordinal) < 0 &&
                    token.IndexOf('@') < 0 &&
                    token.IndexOf('.') > 0 &&
                    Uri.TryCreate("https://" + token, UriKind.Absolute, out uri))
                    return "https://" + token;
            }

            return string.Empty;
        }

        private static string TrimUrlToken(string token)
        {
            if (string.IsNullOrEmpty(token)) return string.Empty;
            return token.Trim(' ', '\t', '\r', '\n', '"', '\'', '<', '>', '(', ')', '[', ']', '{', '}', ',', '.', ';', '!');
        }

        private static bool IsLaunchableLinkScheme(string scheme)
        {
            return string.Equals(scheme, "http", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheme, "https", StringComparison.OrdinalIgnoreCase)
                || string.Equals(scheme, "tg", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSiteName(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
                return string.Empty;

            string host = uri.Host ?? string.Empty;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                host = host.Substring(4);
            return host;
        }

        private static string BuildDisplayUrl(string url)
        {
            Uri uri;
            if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out uri))
                return url ?? string.Empty;

            if (string.Equals(uri.Scheme, "tg", StringComparison.OrdinalIgnoreCase))
                return url;

            string path = uri.PathAndQuery ?? string.Empty;
            if (path == "/") path = string.Empty;
            string host = BuildSiteName(url);
            return (host + path).TrimEnd('/');
        }

        private static bool TryReadGeoPoint(BinaryReader r, out double lat, out double lon)
        {
            lat = 0.0;
            lon = 0.0;
            uint ctor = r.ReadUInt32();
            if (ctor == CtorGeoPointEmpty) return false;
            if (ctor != CtorGeoPoint) return false;

            int flags = r.ReadInt32();
            lon = r.ReadDouble();
            lat = r.ReadDouble();
            r.ReadInt64(); // access_hash
            if ((flags & 1) != 0) r.ReadInt32(); // accuracy_radius
            return true;
        }

        private static string ReadTextWithEntitiesString(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorTextWithEntities)
            {
                string text = ReadString(r);
                SkipEntityVector(r);
                return text;
            }

            // Legacy poll question was a bare string in older layers.
            throw new InvalidDataException("textWithEntities expected");
        }

        private static PollOption ReadPollAnswer(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            string text;
            byte[] option;
            if (ctor == CtorPollAnswer)
            {
                text = ReadString(r);
                option = ReadBytes(r);
            }
            else if (ctor == CtorPollAnswerLayer214)
            {
                text = ReadTextWithEntitiesString(r);
                option = ReadBytes(r);
            }
            else
            {
                return null;
            }

            return new PollOption(text, option, 0, false, false);
        }

        private static void ApplyPollResults(BinaryReader r, List<PollOption> options, out int totalVoters)
        {
            totalVoters = 0;
            int flags = r.ReadInt32();
            if ((flags & (1 << 1)) != 0)
            {
                uint vec = r.ReadUInt32();
                int count = vec == CtorVector ? r.ReadInt32() : 0;
                if (count < 0 || count > 64) count = 0;
                for (int i = 0; i < count; i++)
                {
                    uint ctor = r.ReadUInt32();
                    if (ctor != CtorPollAnswerVoters) break;
                    int vf = r.ReadInt32();
                    byte[] token = ReadBytes(r);
                    int voters = r.ReadInt32();
                    ApplyPollOptionVotes(options, token, voters, (vf & 1) != 0, (vf & 2) != 0);
                }
            }

            if ((flags & (1 << 2)) != 0) totalVoters = r.ReadInt32();
            if ((flags & (1 << 3)) != 0) SkipPeerVector(r);
            if ((flags & (1 << 4)) != 0)
            {
                ReadString(r); // solution
                SkipEntityVector(r);
            }
        }

        private static void ApplyPollOptionVotes(List<PollOption> options, byte[] token, int voters, bool chosen, bool correct)
        {
            if (options == null) return;
            for (int i = 0; i < options.Count; i++)
            {
                PollOption current = options[i];
                if (current != null && BytesEqual(current.Token, token))
                {
                    options[i] = new PollOption(current.Text, current.Token, voters, chosen, correct);
                    return;
                }
            }
        }

        private static MessageContent ParseDocumentMedia(BinaryReader r, MemoryStream ms, string caption)
        {
            long startPos = ms.Position;
            try
            {
                int mediaFlags = r.ReadInt32();
                if ((mediaFlags & 1) == 0)
                    return new MessageContentDocument(string.Empty, 0L, string.Empty, string.Empty, caption);

                DocumentProjection doc = ReadDocumentProjection(r);
                return BuildDocumentContent(doc, caption);
            }
            catch
            {
                ms.Position = startPos;
                return ParseDocumentMediaByScan(r, ms, caption);
            }
            finally
            {
                ms.Position = startPos;
            }
        }

        private sealed class DocumentProjection
        {
            public long Id;
            public long AccessHash;
            public byte[] FileReference;
            public int DcId;
            public long Size;
            public string MimeType;
            public string FileName;
            public IList<MediaThumbnail> Thumbnails;
            public bool IsVoice;
            public bool IsAudio;
            public bool IsSticker;
            public bool IsAnimation;
            public bool IsVideo;
            public bool IsVideoNote;
            public bool IsCustomEmoji;
            public int DurationSeconds;
            public int Width;
            public int Height;
            public string AudioTitle;
            public string AudioPerformer;
            public byte[] VoiceWaveform;
            public string StickerEmoji;
        }

        private static DocumentProjection ReadDocumentProjection(BinaryReader r)
        {
            uint documentCtor = r.ReadUInt32();
            if (documentCtor == CtorDocumentEmpty)
            {
                long emptyId = r.ReadInt64();
                return new DocumentProjection
                {
                    Id = emptyId,
                    AccessHash = 0L,
                    FileReference = null,
                    DcId = 0,
                    Size = 0L,
                    MimeType = string.Empty,
                    FileName = string.Empty,
                    Thumbnails = new MediaThumbnail[0]
                };
            }

            if (documentCtor != CtorDocument)
                throw new InvalidDataException("document ctor");

            int docFlags = r.ReadInt32();
            long id = r.ReadInt64();
            long accessHash = r.ReadInt64();
            byte[] fileReference = ReadBytes(r);
            r.ReadInt32(); // date
            string mime = ReadString(r);
            long size = r.ReadInt64();

            var thumbnails = new List<MediaThumbnail>();
            int bestWidth = 0;
            int bestHeight = 0;
            long bestSize = 0;

            if ((docFlags & (1 << 0)) != 0)
                ReadPhotoSizeVector(r, thumbnails, ref bestWidth, ref bestHeight, ref bestSize);

            if ((docFlags & (1 << 1)) != 0)
                SkipVideoSizeVector(r);

            int dcId = r.ReadInt32();
            DocumentProjection doc = ReadDocumentAttributes(r);
            doc.Id = id;
            doc.AccessHash = accessHash;
            doc.FileReference = fileReference;
            doc.DcId = dcId;
            doc.Size = size;
            doc.MimeType = mime ?? string.Empty;
            doc.Thumbnails = thumbnails;
            if (string.IsNullOrEmpty(doc.FileName))
                doc.FileName = string.Empty;
            return doc;
        }

        private static MessageContent BuildDocumentContent(DocumentProjection doc, string caption)
        {
            if (doc == null)
                return new MessageContentDocument(string.Empty, 0L, string.Empty, string.Empty, caption);

            string fileId = doc.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string fileName = FirstNonEmpty(doc.FileName, GuessFileName(doc.MimeType));
            var file = new TelegramMediaFile(
                fileId,
                doc.AccessHash,
                doc.FileReference,
                doc.DcId,
                doc.Size,
                doc.MimeType,
                fileName,
                string.Empty,
                string.Empty);

            var span = TimeSpan.FromSeconds(doc.DurationSeconds);
            if (doc.IsVoice)
                return new MessageContentVoice(span, string.Empty, doc.VoiceWaveform,
                    file, CtorMessageMediaDocumentC, null, null, null);
            if (doc.IsAudio)
                return new MessageContentAudio(span, doc.AudioTitle, doc.AudioPerformer,
                    doc.Size, string.Empty, string.Empty, caption, null, file,
                    doc.Thumbnails, CtorMessageMediaDocumentC, null, null, null);
            if (doc.IsSticker || doc.IsCustomEmoji)
                return new MessageContentSticker(doc.StickerEmoji, string.Empty,
                    file, doc.Thumbnails, CtorMessageMediaDocumentC, null, null, null);
            if (doc.IsAnimation)
                return new MessageContentVideo(span, doc.Width, doc.Height, doc.Size,
                    string.Empty, string.Empty, caption, false, true, null, file,
                    doc.Thumbnails, CtorMessageMediaDocumentC, null, null, null);
            if (doc.IsVideoNote)
                return new MessageContentVideo(span, doc.Width, doc.Height, doc.Size,
                    string.Empty, string.Empty, caption, true, false, null, file,
                    doc.Thumbnails, CtorMessageMediaDocumentC, null, null, null);
            if (doc.IsVideo)
                return new MessageContentVideo(span, doc.Width, doc.Height, doc.Size,
                    string.Empty, string.Empty, caption, false, false, null, file,
                    doc.Thumbnails, CtorMessageMediaDocumentC, null, null, null);

            return new MessageContentDocument(fileName, doc.Size, doc.MimeType,
                string.Empty, string.Empty, caption, null, file, doc.Thumbnails,
                CtorMessageMediaDocumentC, null, null, null);
        }

        private static DocumentProjection ReadDocumentAttributes(BinaryReader r)
        {
            var doc = new DocumentProjection
            {
                MimeType = string.Empty,
                FileName = string.Empty,
                AudioTitle = string.Empty,
                AudioPerformer = string.Empty,
                StickerEmoji = string.Empty,
                Thumbnails = new MediaThumbnail[0]
            };

            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector) return doc;

            int count = r.ReadInt32();
            if (count < 0 || count > 64) return doc;

            for (int i = 0; i < count; i++)
            {
                uint attrCtor = r.ReadUInt32();
                switch (attrCtor)
                {
                    case CtorDocAttrFilename:
                        doc.FileName = ReadString(r);
                        break;
                    case CtorDocAttrImageSize:
                        doc.Width = r.ReadInt32();
                        doc.Height = r.ReadInt32();
                        break;
                    case CtorDocAttrAnimated:
                        doc.IsAnimation = true;
                        break;
                    case CtorDocAttrAudio:
                    {
                        int flags = r.ReadInt32();
                        doc.DurationSeconds = r.ReadInt32();
                        if ((flags & (1 << 10)) != 0) doc.IsVoice = true;
                        else doc.IsAudio = true;
                        if ((flags & (1 << 0)) != 0) doc.AudioTitle = ReadString(r);
                        if ((flags & (1 << 1)) != 0) doc.AudioPerformer = ReadString(r);
                        if ((flags & (1 << 2)) != 0) doc.VoiceWaveform = ReadBytes(r);
                        break;
                    }
                    case CtorDocAttrVideo:
                    case CtorDocAttrVideoLegacy:
                    {
                        int flags = r.ReadInt32();
                        if ((flags & (1 << 0)) != 0) doc.IsVideoNote = true;
                        else doc.IsVideo = true;
                        doc.DurationSeconds = (int)Math.Round(r.ReadDouble());
                        doc.Width = r.ReadInt32();
                        doc.Height = r.ReadInt32();
                        if ((flags & (1 << 2)) != 0) r.ReadInt32(); // preload_prefix_size
                        if ((flags & (1 << 4)) != 0) r.ReadDouble(); // video_start_ts
                        if ((flags & (1 << 5)) != 0) ReadString(r); // video_codec
                        break;
                    }
                    case CtorDocAttrSticker:
                    {
                        int flags = r.ReadInt32();
                        doc.IsSticker = true;
                        doc.StickerEmoji = ReadString(r);
                        SkipInputStickerSet(r);
                        if ((flags & (1 << 0)) != 0) SkipMaskCoords(r);
                        break;
                    }
                    case CtorDocAttrCustomEmoji:
                    {
                        r.ReadInt32(); // flags
                        doc.IsCustomEmoji = true;
                        doc.StickerEmoji = ReadString(r);
                        SkipInputStickerSet(r);
                        break;
                    }
                    case CtorDocAttrHasStickers:
                        break;
                    default:
                        throw new InvalidDataException("document attribute ctor");
                }
            }

            return doc;
        }

        private static void ReadPhotoSizeVector(BinaryReader r, IList<MediaThumbnail> thumbnails,
            ref int bestWidth, ref int bestHeight, ref long bestSize)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector) return;

            int count = r.ReadInt32();
            if (count < 0 || count > 64) return;
            for (int i = 0; i < count; i++)
                ReadPhotoSize(r, thumbnails, ref bestWidth, ref bestHeight, ref bestSize);
        }

        private static void SkipVideoSizeVector(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector) return;

            int count = r.ReadInt32();
            if (count < 0 || count > 64) return;

            for (int i = 0; i < count; i++)
            {
                uint ctor = r.ReadUInt32();
                if (ctor == CtorVideoSize)
                {
                    int flags = r.ReadInt32();
                    ReadString(r);
                    r.ReadInt32();
                    r.ReadInt32();
                    r.ReadInt32();
                    if ((flags & 1) != 0) r.ReadDouble();
                }
                else if (ctor == CtorVideoSizeEmojiMarkup)
                {
                    r.ReadInt64();
                    SkipIntVector(r);
                }
                else
                {
                    throw new InvalidDataException("video size ctor");
                }
            }
        }

        private static void SkipIntVector(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector) return;
            int count = r.ReadInt32();
            if (count < 0 || count > 128) return;
            for (int i = 0; i < count; i++) r.ReadInt32();
        }

        private static void SkipInputStickerSet(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            switch (ctor)
            {
                case CtorInputStickerSetEmpty:
                    return;
                case CtorInputStickerSetId:
                    r.ReadInt64();
                    r.ReadInt64();
                    return;
                case CtorInputStickerSetShortName:
                    ReadString(r);
                    return;
                default:
                    throw new InvalidDataException("inputStickerSet ctor");
            }
        }

        private static void SkipMaskCoords(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor != CtorMaskCoords)
                throw new InvalidDataException("maskCoords ctor");
            r.ReadInt32();
            r.ReadDouble();
            r.ReadDouble();
            r.ReadDouble();
        }

        private static string GuessFileName(string mime)
        {
            if (string.IsNullOrEmpty(mime)) return "file";
            if (mime.IndexOf("word", StringComparison.OrdinalIgnoreCase) >= 0) return "document.docx";
            if (mime.IndexOf("excel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mime.IndexOf("spreadsheet", StringComparison.OrdinalIgnoreCase) >= 0) return "document.xlsx";
            if (mime.IndexOf("powerpoint", StringComparison.OrdinalIgnoreCase) >= 0 ||
                mime.IndexOf("presentation", StringComparison.OrdinalIgnoreCase) >= 0) return "document.pptx";
            if (mime.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0) return "document.pdf";
            return "file";
        }

        private static string FirstNonEmpty(string a, string b)
        {
            return !string.IsNullOrEmpty(a) ? a : (b ?? string.Empty);
        }

        private static MessageContent ParseDocumentMediaByScan(BinaryReader r, MemoryStream ms, string caption)
        {
            long startPos = ms.Position;
            long maxScan = startPos + 768;
            if (maxScan > ms.Length) maxScan = ms.Length;

            bool isVoice = false, isAudio = false, isSticker = false, isAnimation = false;
            bool isVideo = false, isVideoNote = false;
            string fileName = string.Empty;
            int durationSeconds = 0;
            int width = 0, height = 0;
            string audioTitle = string.Empty, audioPerformer = string.Empty;
            byte[] voiceWaveform = null;
            string stickerEmoji = string.Empty;

            for (long pos = startPos; pos + 8 <= maxScan; pos += 4)
            {
                ms.Position = pos;
                uint ctor;
                try { ctor = r.ReadUInt32(); }
                catch { break; }

                switch (ctor)
                {
                    case CtorDocAttrAudio:
                    {
                        try
                        {
                            int aFlags = r.ReadInt32();
                            int dur = r.ReadInt32();
                            durationSeconds = dur;
                            // Bit 10 = voice (round mic recording vs uploaded
                            // audio file).
                            if ((aFlags & (1 << 10)) != 0) isVoice = true;
                            else isAudio = true;
                            if ((aFlags & (1 << 0)) != 0) audioTitle = ReadStringSafe(r);
                            if ((aFlags & (1 << 1)) != 0) audioPerformer = ReadStringSafe(r);
                            if ((aFlags & (1 << 2)) != 0) voiceWaveform = ReadBytesSafe(r);
                        }
                        catch { }
                        break;
                    }
                    case CtorDocAttrVideo:
                    case CtorDocAttrVideoLegacy:
                    {
                        try
                        {
                            int vFlags = r.ReadInt32();
                            // Bit 0 = round_message (a "video note": a
                            // circular short video usually shot with the
                            // selfie camera).
                            if ((vFlags & (1 << 0)) != 0) isVideoNote = true;
                            else isVideo = true;
                            // Duration is double in layer 214; we round to
                            // seconds. Reading 8 bytes either way to keep the
                            // local cursor sane (irrelevant to outer scan).
                            try { durationSeconds = (int)r.ReadDouble(); }
                            catch { r.ReadInt64(); }
                            width = r.ReadInt32();
                            height = r.ReadInt32();
                        }
                        catch { }
                        break;
                    }
                    case CtorDocAttrAnimated:
                        isAnimation = true;
                        break;
                    case CtorDocAttrSticker:
                    {
                        isSticker = true;
                        try
                        {
                            r.ReadInt32(); // flags
                            stickerEmoji = ReadStringSafe(r);
                        }
                        catch { }
                        break;
                    }
                    case CtorDocAttrFilename:
                        try { fileName = ReadStringSafe(r); }
                        catch { }
                        break;
                    case CtorDocAttrImageSize:
                        try
                        {
                            width = r.ReadInt32();
                            height = r.ReadInt32();
                        }
                        catch { }
                        break;
                }
            }

            // Restore cursor for the outer scan-skip.
            ms.Position = startPos;

            // Classification priority order:
            // Voice wins over audio (a voice note is also an audio attribute);
            // sticker/animation override video labels (a sticker may also have
            // imageSize / animated attributes); videoNote over plain video.
            var span = TimeSpan.FromSeconds(durationSeconds);
            if (isVoice)
                return new MessageContentVoice(span, string.Empty, voiceWaveform);
            if (isAudio)
                return new MessageContentAudio(span, audioTitle, audioPerformer, 0L, string.Empty, caption);
            if (isSticker)
                return new MessageContentSticker(stickerEmoji, string.Empty);
            if (isAnimation)
                return new MessageContentVideo(span, width, height, 0L,
                    string.Empty, string.Empty, caption, false, true);
            if (isVideoNote)
                return new MessageContentVideo(span, width, height, 0L,
                    string.Empty, string.Empty, caption, true, false);
            if (isVideo)
                return new MessageContentVideo(span, width, height, 0L,
                    string.Empty, string.Empty, caption, false, false);

            // Generic file. We capture the filename if the scanner found it.
            return new MessageContentDocument(fileName, 0L, string.Empty, string.Empty, caption);
        }

        /// <summary>Wraps <see cref="ReadString"/> in a try/catch so a
        /// length-of-string read past EOF returns empty instead of throwing.
        /// Useful inside the bounded heuristic scanner where we may land on
        /// false-positive ctors and the next bytes don't form a valid string.
        /// </summary>
        private static string ReadStringSafe(BinaryReader r)
        {
            try { return ReadString(r); }
            catch { return string.Empty; }
        }

        private static byte[] ReadBytesSafe(BinaryReader r)
        {
            try { return ReadBytes(r); }
            catch { return null; }
        }

        /// <summary>
        /// Best-effort recovery: walk the buffer forward from the current
        /// position at 4-byte alignment looking for a known message ctor
        /// id. If found, leave the stream positioned at that ctor so the
        /// outer ReadOneMessage call can resume cleanly. Returns false if
        /// the rest of the buffer holds no recognisable message header.
        ///
        /// We deliberately stop at the first hit. False positives are
        /// possible (a 4-byte payload window happening to equal one of
        /// our ctor ids) but rare enough that a single mis-attached
        /// message gets dropped via TrailingFieldsException on the next
        /// pass — far better than the previous behaviour of losing the
        /// entire vector to the first unparseable record.
        ///
        /// WP 8.1 / WinRT does not expose <c>MemoryStream.GetBuffer</c>,
        /// so we walk via the BinaryReader and rewind on a hit.
        /// </summary>
        private static bool TryAdvanceToNextMessageCtor(BinaryReader r, MemoryStream ms)
        {
            if (r == null || ms == null) return false;

            // Round forward to the next 4-byte boundary so all candidate
            // reads are aligned (TL is always 4-byte aligned).
            long pos = ms.Position;
            if ((pos & 3L) != 0)
            {
                long aligned = (pos + 3L) & ~3L;
                if (aligned > ms.Length) return false;
                ms.Position = aligned;
            }

            while (ms.Position + 4 <= ms.Length)
            {
                long candidatePos = ms.Position;
                uint candidate;
                try
                {
                    candidate = r.ReadUInt32();
                }
                catch
                {
                    return false;
                }

                if (candidate == CtorMessage
                    || candidate == CtorMessageService
                    || candidate == CtorMessageServiceLegacy
                    || candidate == CtorMessageServiceCall
                    || candidate == CtorMessageEmpty)
                {
                    // Rewind so the caller's next ReadUInt32 sees this ctor.
                    ms.Position = candidatePos;
                    return true;
                }
            }
            return false;
        }
    }
}
