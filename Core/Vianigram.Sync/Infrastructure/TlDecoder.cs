// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Text;
using Vianigram.Sync.Domain.ValueObjects;

namespace Vianigram.Sync.Infrastructure
{
    /// <summary>
    /// Decoder for the TL types Sync consumes:
    ///   - updates.State#a56c2a3e
    ///   - updates.differenceEmpty#5d75a138 / difference#00f49ca0 / differenceSlice#a8fb1981 / differenceTooLong#4afe8f6d
    ///   - updates#74ae4240 / updatesCombined#725b04c3 / updateShort#78d4dec1 / updateShortMessage#313bc7f8 /
    ///     updateShortChatMessage#4d6deea5 / updateShortSentMessage#9015e101 / updatesTooLong#e317af7e
    ///   - the subset of Update#* constructors enumerated in <see cref="Update"/>
    ///
    /// Unknown Update constructor ids collapse to <see cref="UpdateUnsupported"/>
    /// with the raw remaining bytes preserved; we cannot skip them safely without
    /// the schema, so the decoder consumes everything to end-of-buffer for an
    /// unsupported boxed Update inside a container — this is a documented
    /// trade-off. Containers prefix Update lists with a vector header so the
    /// count is known up-front; if an unsupported ctor appears mid-list we
    /// abort the list (logging the position) rather than misalign.
    /// </summary>
    public static class TlDecoder
    {
        // ----- Container constructor ids -----
        public const uint UpdatesId = 0x74ae4240u;
        public const uint UpdatesCombinedId = 0x725b04c3u;
        public const uint UpdateShortId = 0x78d4dec1u;
        public const uint UpdateShortMessageId = 0x313bc7f8u;
        public const uint UpdateShortChatMessageId = 0x4d6deea5u;
        public const uint UpdateShortSentMessageId = 0x9015e101u;
        public const uint UpdatesTooLongId = 0xe317af7eu;

        // ----- updates.State / Difference -----
        public const uint UpdatesStateId = 0xa56c2a3eu;
        public const uint UpdatesDifferenceEmptyId = 0x5d75a138u;
        public const uint UpdatesDifferenceId = 0x00f49ca0u;
        public const uint UpdatesDifferenceSliceId = 0xa8fb1981u;
        public const uint UpdatesDifferenceTooLongId = 0x4afe8f6du;

        // ----- Vector / Bool primitives -----
        public const uint VectorId = 0x1cb5c415u;
        public const uint BoolTrueId = 0x997275b5u;
        public const uint BoolFalseId = 0xbc799737u;

        // ----- Channel-touched ctors -----
        // These two carry no message but signal "channel X moved — fetch
        // via updates.getChannelDifference". Without handling them, the
        // channel push pipeline goes silent because most channel messages
        // arrive via the diff response, not via the push itself.
        public const uint UpdateChannelCtor = 0x635b4c09u;        // updateChannel
        public const uint UpdateChannelTooLongCtor = 0x108d941fu; // updateChannelTooLong

        // ----- Raw Message ctors (used by ScanAndDecodeMessagesInBody) -----
        // Schema layers we've observed in the wild:
        //   0x9815cec8 — message (layer 173+) with unconditional flags2
        //   0x76352de5 — message (layer 167-172) single flags
        //   0x38116ee0 — message (layer 158-166)
        //   0xa66c7efc — older message
        //   0x2b085862 — messageService (carries action; we synthesize
        //                an UpdateNewChannelMessage for them too so
        //                joined/left/pinned events show up as toasts)
        //   0x90a6ca84 — messageEmpty (tombstone — we skip)
        public const uint MessageCtorL173    = 0x9815cec8u;
        public const uint MessageCtorL167    = 0x76352de5u;
        public const uint MessageCtorCurrent = 0x38116ee0u;
        public const uint MessageCtorOlder   = 0xa66c7efcu;
        public const uint MessageServiceCtor = 0x2b085862u;
        public const uint MessageEmptyCtor   = 0x90a6ca84u;

        // -----------------------------------------------------------------
        // Public entry points
        // -----------------------------------------------------------------

        /// <summary>
        /// Decode an updates.State#a56c2a3e response. Returns null on malformed input.
        /// </summary>
        public static SyncCursor DecodeUpdatesState(byte[] body)
        {
            if (body == null || body.Length < 4 + 5 * 4) return null;
            int p = 0;
            uint ctor = ReadUInt32(body, ref p);
            if (ctor != UpdatesStateId) return null;
            int pts = ReadInt32(body, ref p);
            int qts = ReadInt32(body, ref p);
            int date = ReadInt32(body, ref p);
            int seq = ReadInt32(body, ref p);
            // unread_count (int32) — skip
            ReadInt32(body, ref p);
            return new SyncCursor(pts, qts, seq, date);
        }

        /// <summary>
        /// Decode an Updates supertype payload. Returns null if the body is empty or the
        /// constructor is unrecognized at the supertype level (caller should treat that
        /// as a no-op — likely a future TL container we don't know yet).
        /// </summary>
        public static UpdatesEnvelope DecodeUpdatesEnvelope(byte[] body)
        {
            if (body == null || body.Length < 4) return null;
            int p = 0;
            uint ctor = ReadUInt32(body, ref p);
            switch (ctor)
            {
                case UpdatesTooLongId:
                    return UpdatesTooLong.Instance;
                case UpdateShortId:
                    return DecodeUpdateShort(body, p);
                case UpdateShortMessageId:
                    return DecodeUpdateShortMessage(body, p, ShortMessageKind.Private, ctor);
                case UpdateShortChatMessageId:
                    return DecodeUpdateShortMessage(body, p, ShortMessageKind.ChatMessage, ctor);
                case UpdateShortSentMessageId:
                    return DecodeUpdateShortSentMessage(body, p);
                case UpdatesId:
                case UpdatesCombinedId:
                    return DecodeUpdatesContainer(body, p, ctor);
                default:
                    Vianigram.Kernel.Telemetry.UnknownCtorTelemetry.Observe(
                        "Sync.TlDecoder",
                        ctor,
                        "DecodeUpdatesEnvelope unknown supertype");
                    return null;
            }
        }

        /// <summary>
        /// Extract the cursor from an updates.getDifference response only when it
        /// is safe for a specialized poller to acknowledge it: no new message or
        /// encrypted-message vectors, and the other_updates count exactly matches
        /// the updates already handled by that poller.
        /// </summary>
        public static bool TryDecodeSafeDifferenceCursor(
            byte[] body,
            int handledOtherUpdatesCount,
            SyncCursor current,
            out SyncCursor cursor,
            out string reason)
        {
            cursor = current ?? SyncCursor.Initial();
            reason = string.Empty;

            if (body == null || body.Length < 4)
            {
                reason = "empty body";
                return false;
            }
            if (handledOtherUpdatesCount < 0)
            {
                reason = "negative handled update count";
                return false;
            }

            int p = 0;
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor == UpdatesDifferenceEmptyId)
            {
                if (body.Length < 12)
                {
                    reason = "malformed differenceEmpty";
                    return false;
                }

                int date = ReadInt32Safe(body, ref p);
                int seq = ReadInt32Safe(body, ref p);
                int nextDate = date > cursor.Date ? date : cursor.Date;
                int nextSeq = seq > cursor.Seq ? seq : cursor.Seq;
                cursor = cursor.WithSeqAndDate(nextSeq, nextDate);
                reason = "differenceEmpty";
                return true;
            }

            if (ctor == UpdatesDifferenceTooLongId)
            {
                if (body.Length < 8)
                {
                    reason = "malformed differenceTooLong";
                    return false;
                }

                int pts = ReadInt32Safe(body, ref p);
                if (pts > cursor.Pts)
                {
                    cursor = cursor.WithPts(pts);
                }
                reason = "differenceTooLong";
                return true;
            }

            if (ctor != UpdatesDifferenceId && ctor != UpdatesDifferenceSliceId)
            {
                reason = "not a difference response";
                return false;
            }

            int newMessages;
            if (!TryReadPhoneCallServiceMessageVector(body, ref p, out newMessages))
            {
                reason = "missing new_messages vector";
                return false;
            }

            int newEncryptedMessages;
            if (!TryReadVectorCount(body, ref p, out newEncryptedMessages))
            {
                reason = "missing new_encrypted_messages vector";
                return false;
            }
            if (newEncryptedMessages != 0)
            {
                reason = "contains new_encrypted_messages=" + newEncryptedMessages;
                return false;
            }

            int otherUpdates;
            if (!TryReadVectorCount(body, ref p, out otherUpdates))
            {
                reason = "missing other_updates vector";
                return false;
            }
            if (otherUpdates != handledOtherUpdatesCount)
            {
                reason = "other_updates=" + otherUpdates + " handled=" + handledOtherUpdatesCount;
                return false;
            }

            SyncCursor state;
            if (!TryDecodeTrailingUpdatesState(body, out state))
            {
                reason = "missing trailing updates.State";
                return false;
            }

            cursor = state;
            reason = "difference state";
            return true;
        }

        // -----------------------------------------------------------------
        // Container decoders
        // -----------------------------------------------------------------

        private static UpdatesEnvelope DecodeUpdateShort(byte[] body, int p)
        {
            // updateShort#78d4dec1 update:Update date:int
            Update u = DecodeUpdate(body, ref p);
            int date = ReadInt32Safe(body, ref p);
            return new UpdatesEnvelopeShort(u, date);
        }

        private static UpdatesEnvelope DecodeUpdateShortMessage(byte[] body, int p, ShortMessageKind kind, uint ctor)
        {
            // updateShortMessage#313bc7f8 flags:# out:flags.1?true mentioned:flags.4?true media_unread:flags.5?true
            //                              silent:flags.13?true id:int user_id:long pts:int pts_count:int date:int
            //                              fwd_from:flags.2?MessageFwdHeader via_bot_id:flags.11?long
            //                              reply_to:flags.3?MessageReplyHeader entities:flags.7?Vector<MessageEntity>
            //                              ttl_period:flags.25?int
            //
            // updateShortChatMessage#4d6deea5 flags:# out:flags.1?true ... id:int from_id:long chat_id:long message:string ...
            //
            // We only extract the fields we publish via UpdatesEnvelopeShortMessage:
            //   id, peer/chat id, sender id, message body, pts, pts_count, date,
            //   isOutgoing (flags.1), reply_to_msg_id if present.

            uint flags = ReadUInt32Safe(body, ref p);
            int id = ReadInt32Safe(body, ref p);

            long fromUserId;
            long peerOrChatId;
            string message;

            if (kind == ShortMessageKind.ChatMessage)
            {
                long fromId = ReadInt64Safe(body, ref p);
                long chatId = ReadInt64Safe(body, ref p);
                message = ReadStringSafe(body, ref p);
                fromUserId = fromId;
                peerOrChatId = chatId;
            }
            else
            {
                long userId = ReadInt64Safe(body, ref p);
                message = ReadStringSafe(body, ref p);
                bool isOutgoingFromFlags = (flags & (1u << 1)) != 0;
                // For private shortMessage, the peer is `user_id` regardless of direction.
                // FromUserId we approximate from the direction:
                //   incoming  → from = user_id, peer = user_id (the other party)
                //   outgoing  → from = self  → we can't know self id here; downstream
                //               consumers identify "outgoing" by the flag, not by from_id.
                fromUserId = isOutgoingFromFlags ? 0L : userId;
                peerOrChatId = userId;
            }

            int pts = ReadInt32Safe(body, ref p);
            int ptsCount = ReadInt32Safe(body, ref p);
            int date = ReadInt32Safe(body, ref p);

            // Conditional fields (fwd_from, via_bot_id, reply_to, entities, ttl_period).
            // We skip them; only reply_to is observable in the projection and we don't
            // strictly need it (Messages context can re-fetch the full message via
            // updates.getDifference if needed). We DO honor flag.3 to pull the
            // reply-to id, since it's the cheapest signal.
            int replyToMsgId = 0;

            if ((flags & (1u << 2)) != 0) // fwd_from
            {
                // Cannot decode messageFwdHeader without full schema. Abort optional parsing.
                return new UpdatesEnvelopeShortMessage(ctor, kind, id, fromUserId, peerOrChatId,
                    message, pts, ptsCount, date, (flags & (1u << 1)) != 0, 0);
            }
            if ((flags & (1u << 11)) != 0) // via_bot_id
            {
                ReadInt64Safe(body, ref p);
            }
            if ((flags & (1u << 3)) != 0) // reply_to
            {
                // messageReplyHeader#a6d57763 has flags:# reply_to_msg_id:flags.4?int ...
                // We crack it only enough to extract reply_to_msg_id when present; otherwise abort.
                int savedReply = p;
                if (savedReply + 8 <= body.Length)
                {
                    ReadUInt32(body, ref p); // reply ctor (messageReplyHeader#...)
                    uint replyFlags = ReadUInt32(body, ref p);
                    if ((replyFlags & (1u << 4)) != 0 && p + 4 <= body.Length)
                    {
                        replyToMsgId = ReadInt32(body, ref p);
                    }
                    // Don't try to decode the rest of messageReplyHeader; stop further
                    // optional parsing here and just emit what we have.
                    return new UpdatesEnvelopeShortMessage(ctor, kind, id, fromUserId, peerOrChatId,
                        message, pts, ptsCount, date, (flags & (1u << 1)) != 0, replyToMsgId);
                }
                else
                {
                    p = savedReply;
                }
            }
            // Skip remaining optional fields silently.
            return new UpdatesEnvelopeShortMessage(ctor, kind, id, fromUserId, peerOrChatId,
                message, pts, ptsCount, date, (flags & (1u << 1)) != 0, replyToMsgId);
        }

        private static UpdatesEnvelope DecodeUpdateShortSentMessage(byte[] body, int p)
        {
            // updateShortSentMessage#9015e101 flags:# out:flags.1?true id:int pts:int pts_count:int
            //                                  date:int media:flags.9?MessageMedia entities:flags.7?Vector<MessageEntity>
            //                                  ttl_period:flags.25?int
            uint flags = ReadUInt32Safe(body, ref p);
            int id = ReadInt32Safe(body, ref p);
            int pts = ReadInt32Safe(body, ref p);
            int ptsCount = ReadInt32Safe(body, ref p);
            int date = ReadInt32Safe(body, ref p);
            return new UpdatesEnvelopeShortSent(id, pts, ptsCount, date, (flags & (1u << 1)) != 0);
        }

        private static UpdatesEnvelope DecodeUpdatesContainer(byte[] body, int p, uint ctor)
        {
            // updates#74ae4240 updates:Vector<Update> users:Vector<User> chats:Vector<Chat> date:int seq:int
            // updatesCombined#725b04c3 updates:Vector<Update> users:Vector<User> chats:Vector<Chat> date:int seq_start:int seq:int
            var updates = DecodeUpdateVector(body, ref p);
            var users = DecodeUserVector(body, ref p);
            var chats = DecodeChatVector(body, ref p);
            int date = ReadInt32Safe(body, ref p);
            int seqStart;
            int seq;
            if (ctor == UpdatesCombinedId)
            {
                seqStart = ReadInt32Safe(body, ref p);
                seq = ReadInt32Safe(body, ref p);
            }
            else
            {
                seq = ReadInt32Safe(body, ref p);
                seqStart = seq;
            }
            return new UpdatesEnvelopeFull(ctor, updates, users, chats, date, seq, seqStart);
        }

        // -----------------------------------------------------------------
        // Vector decoders for the user/chat hydration sets
        // -----------------------------------------------------------------

        private static IList<Update> DecodeUpdateVector(byte[] body, ref int p)
        {
            var list = new List<Update>();
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor != VectorId) return list;
            int count = ReadInt32Safe(body, ref p);
            if (count < 0 || count > 10000) return list;
            for (int i = 0; i < count; i++)
            {
                Update u = DecodeUpdate(body, ref p);
                if (u == null) break; // alignment lost; stop the list
                list.Add(u);
            }
            return list;
        }

        private static bool TryReadVectorCount(byte[] body, ref int p, out int count)
        {
            count = 0;
            if (body == null || p + 8 > body.Length) return false;
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor != VectorId) return false;
            count = ReadInt32Safe(body, ref p);
            if (count < 0 || count > 100000) return false;
            return true;
        }

        private static bool TryReadPhoneCallServiceMessageVector(byte[] body, ref int p, out int count)
        {
            count = 0;
            if (!TryReadVectorCount(body, ref p, out count)) return false;
            for (int i = 0; i < count; i++)
            {
                if (!TrySkipPhoneCallServiceMessage(body, ref p)) return false;
            }
            return true;
        }

        /// <summary>
        /// Decode the <c>other_updates:Vector&lt;Update&gt;</c> portion of an
        /// <c>updates.difference</c> / <c>differenceSlice</c> response
        /// when the cursor lands AFTER <c>new_encrypted_messages</c>.
        /// Used by <see cref="SyncApplication.ProcessPolledDifferenceAsync"/>
        /// to deliver channel messages that arrive only via the
        /// getDifference path (the live push stream skips these for
        /// inactive sessions). Returns the decoded list, or null on
        /// malformed input. The reader's position lands AFTER the
        /// other_updates vector on success.
        /// </summary>
        public static IList<Update> TryDecodeOtherUpdatesVector(byte[] body, ref int p)
        {
            int count;
            if (!TryReadVectorCount(body, ref p, out count)) return null;
            var list = new List<Update>(count);
            for (int i = 0; i < count; i++)
            {
                Update u = DecodeUpdate(body, ref p);
                if (u == null)
                {
                    // Decode failure mid-vector — abort and return what
                    // we have. SyncState applies what it can.
                    break;
                }
                list.Add(u);
            }
            return list;
        }

        /// <summary>
        /// Decode the LEADING ctor of a <c>updates.difference*</c> response
        /// so the application knows
        /// which subtype it received before walking the body. Returns
        /// 0 on failure.
        /// </summary>
        public static uint PeekDifferenceCtor(byte[] body)
        {
            if (body == null || body.Length < 4) return 0;
            int p = 0;
            return ReadUInt32Safe(body, ref p);
        }

        // Update ctors we know how to decode + apply. Used by
        // ScanAndDecodeUpdatesInBody as a whitelist when scanning the
        // diff body for embedded updates.
        private static readonly uint[] _knownUpdateCtors = new uint[]
        {
            UpdateNewMessage.TlConstructorId,           // 0x1f2b0afd
            UpdateNewChannelMessage.TlConstructorId,    // 0x62ba04d9
            UpdateEditMessage.TlConstructorId,          // 0xe40370a3
            UpdateEditChannelMessage.TlConstructorId,   // 0x1b3f4df7
            UpdateDeleteMessages.TlConstructorId,
            UpdateDeleteChannelMessages.TlConstructorId,
            UpdateReadHistoryInbox.TlConstructorId,
            UpdateReadHistoryOutbox.TlConstructorId,
            UpdateReadChannelInbox.TlConstructorId,
            UpdateReadChannelOutbox.TlConstructorId,
            UpdateUserStatus.TlConstructorId,
            UpdateUserTyping.TlConstructorId,
            UpdateChatUserTyping.TlConstructorId,
            UpdateChannelUserTyping.TlConstructorId,
            UpdateChannelCtor,                          // 0x635b4c09
            UpdateChannelTooLongCtor,                   // 0x108d941f
        };

        /// <summary>
        /// Byte-scan path: scan the diff body for known Update ctors at
        /// 4-byte alignments and
        /// attempt to decode each one. Returns the list of valid
        /// updates (non-null DecodeUpdate results). De-duplicated by
        /// (ctor, message_id) so a ctor pattern matched at multiple
        /// false-positive offsets doesn't double-apply the same
        /// channel message.
        ///
        /// This bypasses the structural navigation through
        /// new_messages (which requires a robust Message skip routine
        /// we don't have) and works directly with the embedded Update
        /// envelopes inside the response. False positives (random byte
        /// sequences happening to match an Update ctor) are extremely
        /// rare given the tight ctor whitelist + the strict
        /// per-update field reads inside DecodeUpdate.
        /// </summary>
        public static IList<Update> ScanAndDecodeUpdatesInBody(byte[] body)
        {
            var results = new List<Update>();
            if (body == null || body.Length < 8) return results;

            // Per-(ctor, identifier) dedup. The identifier is the
            // message id for message-bearing updates and 0 for the rest
            // (status / typing / read marks fire so rarely that a dup
            // is harmless).
            var seen = new HashSet<long>();

            for (int i = 4; i + 4 <= body.Length; i += 4)
            {
                uint ctor = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));
                if (!IsKnownUpdateCtor(ctor)) continue;

                int p = i;
                Update u;
                try { u = DecodeUpdate(body, ref p); }
                catch { continue; }
                if (u == null) continue;

                long dedupKey = ComposeUpdateDedupKey(u);
                if (!seen.Add(dedupKey)) continue;

                results.Add(u);
            }
            return results;
        }

        private static bool IsKnownUpdateCtor(uint ctor)
        {
            for (int i = 0; i < _knownUpdateCtors.Length; i++)
            {
                if (_knownUpdateCtors[i] == ctor) return true;
            }
            return false;
        }

        /// <summary>
        /// Scan a TL response body for raw <c>Message</c> constructors at
        /// 4-byte alignments and
        /// synthesize a wrapper <c>UpdateNewChannelMessage</c> /
        /// <c>UpdateNewMessage</c> for each one with <c>pts=0</c>
        /// <c>ptsCount=0</c> so SyncState.Apply emits the message
        /// without touching the cursor (pts=0 short-circuits the
        /// channel pts arithmetic — see SyncState.TryAdvanceChannelPts
        /// and TryAdvancePts which both treat pts=0 as "no cursor info,
        /// just emit").
        ///
        /// Why this exists: <c>updates.difference</c> and
        /// <c>updates.channelDifference</c> deliver MOST channel
        /// messages via <c>new_messages:Vector&lt;Message&gt;</c> —
        /// raw Messages, NOT wrapped in <c>updateNewChannelMessage</c>.
        /// The Update-ctor byte-scan therefore misses them entirely.
        /// This complementary scan looks for the inner Message ctors
        /// directly. False positives are guarded by strict per-Message
        /// validation (id range, peer ctor whitelist, date sanity).
        ///
        /// Dedup is by <c>(peerKey, msg_id)</c> so repeated ctor-pattern
        /// hits at false-positive offsets don't double-emit the same
        /// notification.
        /// </summary>
        public static IList<Update> ScanAndDecodeMessagesInBody(byte[] body)
        {
            var results = new List<Update>();
            if (body == null || body.Length < 32) return results;

            // Earliest plausible Telegram message date — anything before
            // ~2018 is almost certainly a byte alignment false positive.
            const int MinPlausibleDate = 1514764800; // 2018-01-01
            const int MaxId = 2000000000;

            var seen = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i + 12 <= body.Length; i += 4)
            {
                uint ctor = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));
                if (ctor != MessageCtorL173
                    && ctor != MessageCtorL167
                    && ctor != MessageCtorCurrent
                    && ctor != MessageCtorOlder
                    && ctor != MessageServiceCtor)
                {
                    continue;
                }

                int p = i;
                Update u;
                try { u = TryDecodeRawMessageAsUpdate(body, ref p, ctor, MinPlausibleDate, MaxId); }
                catch { continue; }
                if (u == null) continue;

                // Compose dedup key: peerKey + ":" + msgId
                string key = ComposeMessageDedupKey(u);
                if (string.IsNullOrEmpty(key)) continue;
                if (!seen.Add(key)) continue;

                results.Add(u);
            }
            return results;
        }

        private static string ComposeMessageDedupKey(Update u)
        {
            UpdateNewChannelMessage ncm = u as UpdateNewChannelMessage;
            if (ncm != null && ncm.Message != null)
            {
                return (ncm.Message.PeerKey ?? string.Empty) + ":" + ncm.Message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            UpdateNewMessage nm = u as UpdateNewMessage;
            if (nm != null && nm.Message != null)
            {
                return (nm.Message.PeerKey ?? string.Empty) + ":" + nm.Message.Id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return string.Empty;
        }

        // Decode a raw Message starting at position p (which points at
        // the ctor word). On success, returns a synthetic
        // UpdateNewChannelMessage (when peer is channel:N) or
        // UpdateNewMessage (when peer is user:N or chat:N) with pts=0.
        // Returns null on any decode hiccup so the byte-scan keeps
        // moving without misaligning.
        //
        // Mirrors DecodeMessageBearingUpdate's inner-Message decode but
        // STOPS before reading the trailing pts/ptsCount (those fields
        // belong to the Update wrapper, not to the bare Message).
        private static Update TryDecodeRawMessageAsUpdate(
            byte[] body, ref int p, uint msgCtor, int minDate, int maxId)
        {
            ReadUInt32Safe(body, ref p); // consume ctor

            uint flags = ReadUInt32Safe(body, ref p);
            // Sanity: high flag bits beyond layer 214 are unused. If the
            // top byte is non-zero, it's almost certainly garbage from a
            // false-positive alignment.
            if ((flags & 0xFF000000u) != 0 && (flags & 0xFC000000u) != 0)
            {
                return null;
            }

            // Layer 173+ message has an unconditional flags2 word
            // immediately after flags. messageService and older message
            // ctors don't. Reading it for the wrong ctor misaligns
            // every subsequent field.
            uint flags2 = 0;
            if (msgCtor == MessageCtorL173)
            {
                if (p + 4 > body.Length) return null;
                flags2 = ReadUInt32Safe(body, ref p);
                // Sanity: same high-bit check as flags.
                if ((flags2 & 0xFF000000u) != 0) return null;
            }

            int id = ReadInt32Safe(body, ref p);
            if (id <= 0 || id > maxId) return null;

            bool isOutgoing = (flags & (1u << 1)) != 0;
            bool isMediaUnread = (flags & (1u << 5)) != 0;
            bool isSilent = (flags & (1u << 13)) != 0;

            long fromUserId = 0L;
            if ((flags & (1u << 8)) != 0)
            {
                if (p + 12 > body.Length) return null;
                int fromStart = p;
                string fromKey = DecodePeerKey(body, ref p);
                if (string.IsNullOrEmpty(fromKey))
                {
                    p = fromStart;
                    return null;
                }
                string fromKind; long fromId;
                if (PeerKey.TryParse(fromKey, out fromKind, out fromId) && fromKind == "user")
                {
                    fromUserId = fromId;
                }
            }

            // from_boosts_applied:flags.29?int (layer 173+).
            if (msgCtor == MessageCtorL173 && (flags & (1u << 29)) != 0)
            {
                if (p + 4 > body.Length) return null;
                ReadInt32Safe(body, ref p);
            }

            // peer_id MUST be one of the three peer ctors — that's the
            // strongest single check against false positives.
            if (p + 12 > body.Length) return null;
            int peerStart = p;
            uint peerCtor = ReadUInt32Safe(body, ref p);
            if (peerCtor != PeerUserId && peerCtor != PeerChatId && peerCtor != PeerChannelId)
            {
                return null;
            }
            long peerRawId = ReadInt64Safe(body, ref p);
            string peerKey;
            switch (peerCtor)
            {
                case PeerUserId: peerKey = PeerKey.ForUser(peerRawId); break;
                case PeerChatId: peerKey = PeerKey.ForChat(peerRawId); break;
                case PeerChannelId: peerKey = PeerKey.ForChannel(peerRawId); break;
                default: return null;
            }

            // saved_peer_id:flags.28?Peer
            if ((flags & (1u << 28)) != 0)
            {
                if (p + 12 > body.Length) return null;
                SkipPeer(body, ref p);
            }

            // fwd_from / via_bot_id / via_business_bot_id / reply_to
            // are variable-size sub-objects; if any is present, abort —
            // our skipper isn't watertight and a misaligned date read
            // poisons the result. The common "user just sent a plain
            // channel post" case has none of these set, so this
            // conservative bailout still catches the high-volume
            // scenarios that drive notifications.
            if ((flags & (1u << 2)) != 0) return null; // fwd_from
            if ((flags & (1u << 11)) != 0) return null; // via_bot_id
            if (msgCtor == MessageCtorL173 && (flags2 & 1u) != 0) return null; // via_business_bot_id
            if ((flags & (1u << 3)) != 0) return null; // reply_to

            // date — strong validation against false positives.
            if (p + 4 > body.Length) return null;
            int date = ReadInt32Safe(body, ref p);
            if (date < minDate || date > 0x7fffffff) return null;

            string text = string.Empty;
            int replyToMsgId = 0;
            int editDate = 0;

            if (msgCtor == MessageServiceCtor)
            {
                // action:MessageAction — describe via DescribeMessageAction
                // and stop. Failing to describe just yields a generic
                // body string; we don't care about further fields.
                int actionStart = p;
                try
                {
                    if (p + 4 <= body.Length)
                    {
                        uint actionCtor = ReadUInt32Safe(body, ref p);
                        text = DescribeMessageAction(actionCtor, body, ref p, fromUserId);
                    }
                }
                catch { text = "service message"; p = actionStart; }
            }
            else
            {
                // message:string
                if (p + 4 > body.Length) return null;
                text = ReadStringSafe(body, ref p);
                // If empty + media flag set, peek media ctor.
                if (string.IsNullOrEmpty(text) && (flags & (1u << 9)) != 0)
                {
                    int mediaStart = p;
                    if (p + 4 <= body.Length)
                    {
                        uint mediaCtor = ReadUInt32Safe(body, ref p);
                        try { text = DescribeMessageMedia(mediaCtor, body, mediaStart, body.Length); }
                        catch { /* leave text empty */ }
                    }
                    // Whether we extracted media text or not, do NOT
                    // attempt to consume further bytes — restore p so
                    // the outer loop continues from a known good point.
                    p = mediaStart;
                }
            }

            var dto = new MessageDto(
                id: id,
                peerKey: peerKey,
                fromUserId: fromUserId,
                date: date,
                message: text,
                replyToMessageId: replyToMsgId,
                isOutgoing: isOutgoing,
                isMediaUnread: isMediaUnread,
                isSilent: isSilent,
                editDate: editDate);

            // pts=0 + ptsCount=0 so SyncState emits the event without
            // touching cursor arithmetic. See SyncState.TryAdvancePts /
            // TryAdvanceChannelPts: both treat pts<=0 as "no cursor
            // info — emit only".
            switch (peerCtor)
            {
                case PeerChannelId:
                    return new UpdateNewChannelMessage(0, 0, dto, peerRawId);
                case PeerUserId:
                case PeerChatId:
                    return new UpdateNewMessage(0, 0, dto);
                default:
                    return null;
            }
        }

        // Compose a 64-bit dedup key from an Update so the byte-scan
        // doesn't apply the same logical update twice when its ctor
        // pattern happens to align at multiple offsets. Top 32 bits =
        // ctor, low 32 bits = a logical id (message id or 0).
        private static long ComposeUpdateDedupKey(Update u)
        {
            uint ctor = 0;
            long id = 0;
            UpdateNewMessage nm = u as UpdateNewMessage;
            if (nm != null) { ctor = UpdateNewMessage.TlConstructorId; id = nm.Message != null ? nm.Message.Id : 0; }
            else
            {
                UpdateNewChannelMessage ncm = u as UpdateNewChannelMessage;
                if (ncm != null) { ctor = UpdateNewChannelMessage.TlConstructorId; id = ncm.Message != null ? ncm.Message.Id : 0; }
                else
                {
                    UpdateEditMessage em = u as UpdateEditMessage;
                    if (em != null) { ctor = UpdateEditMessage.TlConstructorId; id = em.Message != null ? em.Message.Id : 0; }
                    else
                    {
                        UpdateEditChannelMessage ecm = u as UpdateEditChannelMessage;
                        if (ecm != null) { ctor = UpdateEditChannelMessage.TlConstructorId; id = ecm.Message != null ? ecm.Message.Id : 0; }
                        else
                        {
                            // Status / typing / read updates: no msg id;
                            // dedup on ctor + first-long if available.
                            ctor = (uint)u.GetType().GetHashCode();
                            id = 0;
                        }
                    }
                }
            }
            return ((long)ctor << 32) | (uint)id;
        }

        /// <summary>
        /// Walk an <c>updates.difference</c> body to the start of the
        /// <c>other_updates:Vector&lt;Update&gt;</c> field. On success
        /// the out-parameter <paramref name="p"/> sits at the vector
        /// header. On failure returns false (e.g. the
        /// new_messages skip ran into a non-phoneCall message).
        /// </summary>
        public static bool TryAdvanceToOtherUpdates(byte[] body, out int p)
        {
            p = 0;
            if (body == null || body.Length < 4) return false;

            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor != UpdatesDifferenceId && ctor != UpdatesDifferenceSliceId)
            {
                return false;
            }

            // differenceSlice carries an extra `int` sequence number
            // before the new_messages vector — we don't need it but
            // must consume the bytes to align.
            // Actually no: looking at the schema:
            //   updates.difference#00f49ca0 new_messages:Vector<Message>
            //     new_encrypted_messages:Vector<EncryptedMessage>
            //     other_updates:Vector<Update> chats:Vector<Chat>
            //     users:Vector<User> state:updates.State
            //   updates.differenceSlice#a8fb1981 new_messages:Vector<Message>
            //     new_encrypted_messages:Vector<EncryptedMessage>
            //     other_updates:Vector<Update> chats:Vector<Chat>
            //     users:Vector<User> intermediate_state:updates.State
            // Both start with new_messages. No leading int.

            // new_messages: we only safely skip a vector whose entries
            // are messageService phoneCall (the existing helper). For
            // any other shape we abort. Most polled diffs for active
            // users have count=0 here (channel msgs come via
            // other_updates, not new_messages), so this works in
            // practice for the chatlist-channel case.
            int newMessages;
            if (!TryReadPhoneCallServiceMessageVector(body, ref p, out newMessages))
            {
                return false;
            }

            int newEncrypted;
            if (!TryReadVectorCount(body, ref p, out newEncrypted)) return false;
            if (newEncrypted != 0) return false; // we don't support encrypted

            // p is now positioned at other_updates vector.
            return true;
        }

        private static bool TrySkipPhoneCallServiceMessage(byte[] body, ref int p)
        {
            const uint MessageServiceCtor = 0x2b085862u;

            int saved = p;
            uint msgCtor = ReadUInt32Safe(body, ref p);
            if (msgCtor != MessageServiceCtor)
            {
                p = saved;
                return false;
            }

            uint flags = ReadUInt32Safe(body, ref p);
            ReadInt32Safe(body, ref p); // id

            if ((flags & (1u << 8)) != 0) SkipPeer(body, ref p); // from_id
            SkipPeer(body, ref p); // peer_id

            if ((flags & (1u << 3)) != 0)
            {
                p = saved;
                return false;
            }

            ReadInt32Safe(body, ref p); // date
            if (!TrySkipPhoneCallAction(body, ref p))
            {
                p = saved;
                return false;
            }

            if ((flags & (1u << 25)) != 0) ReadInt32Safe(body, ref p); // ttl_period
            return true;
        }

        private static bool TrySkipPhoneCallAction(byte[] body, ref int p)
        {
            const uint MessageActionPhoneCall = 0x80e11a7fu;

            int saved = p;
            uint actionCtor = ReadUInt32Safe(body, ref p);
            if (actionCtor != MessageActionPhoneCall)
            {
                p = saved;
                return false;
            }

            uint flags = ReadUInt32Safe(body, ref p);
            ReadInt64Safe(body, ref p); // call_id
            if ((flags & 1u) != 0) ReadUInt32Safe(body, ref p); // PhoneCallDiscardReason
            if ((flags & 2u) != 0) ReadInt32Safe(body, ref p); // duration
            return true;
        }

        private static bool TryDecodeTrailingUpdatesState(byte[] body, out SyncCursor cursor)
        {
            cursor = null;
            if (body == null || body.Length < 24) return false;

            int p = body.Length - 24;
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor != UpdatesStateId) return false;

            int pts = ReadInt32Safe(body, ref p);
            int qts = ReadInt32Safe(body, ref p);
            int date = ReadInt32Safe(body, ref p);
            int seq = ReadInt32Safe(body, ref p);
            ReadInt32Safe(body, ref p); // unread_count

            cursor = new SyncCursor(pts, qts, seq, date);
            return true;
        }

        /// <summary>
        /// Public wrapper used by SyncApplication when the byte-scan path
        /// applied updates and
        /// we need to force-advance the cursor to prevent the server
        /// from re-sending the same diff (and us re-applying — visible
        /// as duplicate notifications).
        /// </summary>
        public static bool TryDecodeTrailingDifferenceState(byte[] body, out SyncCursor cursor)
        {
            return TryDecodeTrailingUpdatesState(body, out cursor);
        }

        private static IList<UserStub> DecodeUserVector(byte[] body, ref int p)
        {
            var list = new List<UserStub>();
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor != VectorId) return list;
            int count = ReadInt32Safe(body, ref p);
            if (count < 0 || count > 10000) return list;
            // Full TL User#* decode is expensive and the schema large. We try to
            // decode the high-traffic shape (user#83314fae or user#abb5f120 layer-X)
            // best-effort, but on any unrecognized ctor we ABORT the vector so we
            // don't corrupt the parser. Downstream consumers re-fetch missing user
            // hydrations on demand; missing users are not fatal.
            for (int i = 0; i < count; i++)
            {
                int beforeUser = p;
                UserStub u = TryDecodeUser(body, ref p);
                if (u == null)
                {
                    // Unknown user shape — restore p and bail out of the vector.
                    p = beforeUser;
                    // Skip remaining bytes by jumping to end-of-buffer is unsafe
                    // because chats:Vector<Chat> follows. We can't safely continue
                    // past an unknown User. Best we can do is abort the whole
                    // envelope — emit what we have so far and let the loop request
                    // getDifference.
                    return list;
                }
                list.Add(u);
            }
            return list;
        }

        private static IList<ChatStub> DecodeChatVector(byte[] body, ref int p)
        {
            var list = new List<ChatStub>();
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor != VectorId) { p = saved; return list; }
            int count = ReadInt32Safe(body, ref p);
            if (count < 0 || count > 10000) return list;
            for (int i = 0; i < count; i++)
            {
                int beforeChat = p;
                ChatStub c = TryDecodeChat(body, ref p);
                if (c == null) { p = beforeChat; return list; }
                list.Add(c);
            }
            return list;
        }

        private static UserStub TryDecodeUser(byte[] body, ref int p)
        {
            // user#abb5f120 (layer 159+) and user#83314fae (older) carry many
            // optional fields gated by flags / flags2. Decoding them precisely
            // is out of scope here; we return null on any uncertainty so the
            // caller bails out cleanly.
            //
            // We deliberately probe only userEmpty#d3bc4b7a here — a very narrow
            // shape — so we never misalign. Full user hydration arrives via
            // contacts.getContacts and dialog responses owned by Contacts/Chats.
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor == 0xd3bc4b7au) // userEmpty
            {
                long id = ReadInt64Safe(body, ref p);
                return new UserStub(id, 0L, "", "", "", "", false, false, false);
            }
            // Unknown user constructor — restore and signal abort.
            p = saved;
            return null;
        }

        private static ChatStub TryDecodeChat(byte[] body, ref int p)
        {
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor == 0x29562865u) // chatEmpty#29562865
            {
                long id = ReadInt64Safe(body, ref p);
                return new ChatStub(ChatStubKind.Empty, id, 0L, "", 0);
            }
            p = saved;
            return null;
        }

        // -----------------------------------------------------------------
        // Per-Update decoder dispatch
        // -----------------------------------------------------------------

        public static Update DecodeUpdate(byte[] body, ref int p)
        {
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            switch (ctor)
            {
                case UpdateNewMessage.TlConstructorId:
                    return DecodeUpdateNewMessage(body, ref p, channelMode: false);
                case UpdateNewChannelMessage.TlConstructorId:
                    return DecodeUpdateNewMessage(body, ref p, channelMode: true);
                // Edits are wire-shape-identical to new messages — share the
                // parser, switch on the concrete Update subclass at the
                // construct site.
                case UpdateEditMessage.TlConstructorId:
                    return DecodeMessageBearingUpdate(body, ref p, channelMode: false, isEdit: true);
                case UpdateEditChannelMessage.TlConstructorId:
                    return DecodeMessageBearingUpdate(body, ref p, channelMode: true, isEdit: true);
                case UpdateMessageId.TlConstructorId:
                    {
                        int localId = ReadInt32Safe(body, ref p);
                        long randomId = ReadInt64Safe(body, ref p);
                        return new UpdateMessageId(localId, randomId);
                    }
                case UpdateDeleteMessages.TlConstructorId:
                    {
                        IList<int> ids = DecodeIntVector(body, ref p);
                        int pts = ReadInt32Safe(body, ref p);
                        int ptsCount = ReadInt32Safe(body, ref p);
                        return new UpdateDeleteMessages(pts, ptsCount, ids);
                    }
                case UpdateDeleteChannelMessages.TlConstructorId:
                    {
                        long channelId = ReadInt64Safe(body, ref p);
                        IList<int> ids = DecodeIntVector(body, ref p);
                        int pts = ReadInt32Safe(body, ref p);
                        int ptsCount = ReadInt32Safe(body, ref p);
                        return new UpdateDeleteChannelMessages(channelId, pts, ptsCount, ids);
                    }
                case UpdateUserStatus.TlConstructorId:
                    {
                        long userId = ReadInt64Safe(body, ref p);
                        UserStatusKind k;
                        DateTime? wasOnline;
                        if (!TryDecodeUserStatus(body, ref p, out k, out wasOnline))
                        {
                            p = saved;
                            return new UpdateUnsupported(ctor, SliceTail(body, saved));
                        }
                        return new UpdateUserStatus(userId, k, wasOnline);
                    }
                case UpdateUserTyping.TlConstructorId:
                    {
                        long userId = ReadInt64Safe(body, ref p);
                        TypingActionKind action = DecodeTypingAction(body, ref p);
                        return new UpdateUserTyping(userId, action);
                    }
                case UpdateChatUserTyping.TlConstructorId:
                    {
                        long chatId = ReadInt64Safe(body, ref p);
                        // peer:Peer
                        long userId = DecodePeerUserId(body, ref p);
                        TypingActionKind action = DecodeTypingAction(body, ref p);
                        return new UpdateChatUserTyping(chatId, userId, action);
                    }
                case UpdateChannelUserTyping.TlConstructorId:
                    {
                        // updateChannelUserTyping#8c88c923 flags:# channel_id:long top_msg_id:flags.0?int from_id:Peer action:SendMessageAction
                        uint flags = ReadUInt32Safe(body, ref p);
                        long channelId = ReadInt64Safe(body, ref p);
                        if ((flags & 1u) != 0) ReadInt32Safe(body, ref p); // top_msg_id
                        long userId = DecodePeerUserId(body, ref p);
                        TypingActionKind action = DecodeTypingAction(body, ref p);
                        return new UpdateChannelUserTyping(channelId, userId, action);
                    }
                case UpdateReadHistoryInbox.TlConstructorId:
                    {
                        // updateReadHistoryInbox#9c974fdf flags:# folder_id:flags.0?int peer:Peer max_id:int still_unread_count:int pts:int pts_count:int
                        uint flags = ReadUInt32Safe(body, ref p);
                        if ((flags & 1u) != 0) ReadInt32Safe(body, ref p); // folder_id
                        string peerKey = DecodePeerKey(body, ref p);
                        int maxId = ReadInt32Safe(body, ref p);
                        ReadInt32Safe(body, ref p); // still_unread_count
                        int pts = ReadInt32Safe(body, ref p);
                        int ptsCount = ReadInt32Safe(body, ref p);
                        return new UpdateReadHistoryInbox(peerKey, maxId, pts, ptsCount);
                    }
                case UpdateReadHistoryOutbox.TlConstructorId:
                    {
                        // updateReadHistoryOutbox#2f2f21bf peer:Peer max_id:int pts:int pts_count:int
                        string peerKey = DecodePeerKey(body, ref p);
                        int maxId = ReadInt32Safe(body, ref p);
                        int pts = ReadInt32Safe(body, ref p);
                        int ptsCount = ReadInt32Safe(body, ref p);
                        return new UpdateReadHistoryOutbox(peerKey, maxId, pts, ptsCount);
                    }
                case UpdateReadChannelInbox.TlConstructorId:
                    {
                        // updateReadChannelInbox#922e6e10 flags:# folder_id:flags.0?int channel_id:long max_id:int still_unread_count:int pts:int
                        uint flags = ReadUInt32Safe(body, ref p);
                        if ((flags & 1u) != 0) ReadInt32Safe(body, ref p);
                        long channelId = ReadInt64Safe(body, ref p);
                        int maxId = ReadInt32Safe(body, ref p);
                        int stillUnread = ReadInt32Safe(body, ref p);
                        int pts = ReadInt32Safe(body, ref p);
                        return new UpdateReadChannelInbox(channelId, maxId, stillUnread, pts);
                    }
                case UpdateReadChannelOutbox.TlConstructorId:
                    {
                        long channelId = ReadInt64Safe(body, ref p);
                        int maxId = ReadInt32Safe(body, ref p);
                        return new UpdateReadChannelOutbox(channelId, maxId);
                    }
                case UpdateNotifySettings.TlConstructorId:
                    {
                        // updateNotifySettings#bec268ef peer:NotifyPeer
                        //   notify_settings:PeerNotifySettings
                        // We decode show_previews / silent / mute_until
                        // off the inner peerNotifySettings#99622c0c so
                        // PushNotificationsService can suppress toasts
                        // for muted peers. Stories sounds + per-platform
                        // ringtones are skipped (the toast surface
                        // doesn't honour them on WP 8.1).
                        string peerKey = DecodeNotifyPeerKey(body, ref p);
                        bool? showPreviews;
                        bool? silent;
                        int muteUntil;
                        TryDecodePeerNotifySettings(body, ref p, out showPreviews, out silent, out muteUntil);
                        return new UpdateNotifySettings(peerKey, showPreviews, silent, muteUntil);
                    }
                case UpdateUserName.TlConstructorId:
                    {
                        // updateUserName#1bfbd823 user_id:long first_name:string last_name:string usernames:Vector<Username>
                        long userId = ReadInt64Safe(body, ref p);
                        string first = ReadStringSafe(body, ref p);
                        string last = ReadStringSafe(body, ref p);
                        // usernames vector: we just surface the first one's username string if any.
                        string primary = TryDecodeFirstUsername(body, ref p);
                        return new UpdateUserName(userId, first, last, primary);
                    }
                case UpdateUserPhone.TlConstructorId:
                    {
                        long userId = ReadInt64Safe(body, ref p);
                        string phone = ReadStringSafe(body, ref p);
                        return new UpdateUserPhone(userId, phone);
                    }
                case UpdateConfig.TlConstructorId:
                    return new UpdateConfig();
                case UpdatePtsChanged.TlConstructorId:
                    return new UpdatePtsChanged();
                case UpdateChannelCtor:
                    {
                        // updateChannel#635b4c09 channel_id:long
                        // The sole-payload of many channel pushes — without
                        // handling it our channels go completely silent
                        // because the actual message arrives via
                        // updates.getChannelDifference, NOT via this push.
                        long channelId = ReadInt64Safe(body, ref p);
                        return new UpdateChannelTouched(channelId, UpdateChannelCtor);
                    }
                case UpdateChannelTooLongCtor:
                    {
                        // updateChannelTooLong#108d941f flags:# channel_id:long pts:flags.0?int
                        uint flags = ReadUInt32Safe(body, ref p);
                        long channelId = ReadInt64Safe(body, ref p);
                        if ((flags & 1u) != 0) ReadInt32Safe(body, ref p); // pts
                        return new UpdateChannelTouched(channelId, UpdateChannelTooLongCtor);
                    }
                case UpdateMessageReactions.TlConstructorId:
                    {
                        // updateMessageReactions#5e1b3cb8 flags:# peer:Peer msg_id:int top_msg_id:flags.0?int reactions:MessageReactions
                        uint flags = ReadUInt32Safe(body, ref p);
                        string peerKey = DecodePeerKey(body, ref p);
                        int msgId = ReadInt32Safe(body, ref p);
                        if ((flags & 1u) != 0) ReadInt32Safe(body, ref p); // top_msg_id
                        // We don't decode the MessageReactions sub-tree —
                        // its size is variable and requires the full schema.
                        // The downstream consumer can re-fetch via
                        // messages.getMessagesReactions if it needs the
                        // emoji aggregation.
                        return new UpdateMessageReactions(peerKey, msgId);
                    }
                case 0x2661bf09u:
                    {
                        // updatePhoneCallSignalingData#2661bf09 phone_call_id:long data:bytes.
                        // Calls owns the real routing path, but Sync still consumes
                        // this known shape to avoid noisy unknown-ctor logs and keep
                        // vector alignment for any following updates.
                        ReadInt64Safe(body, ref p);
                        ReadBytesSafe(body, ref p);
                        return new UpdateUnsupported(ctor, SliceTail(body, saved));
                    }
                case 0xab0f6b1eu:
                    {
                        // updatePhoneCall#ab0f6b1e phone_call:PhoneCall.
                        // The inner PhoneCall is a polymorphic boxed type
                        // (phoneCallEmpty / Waiting / Requested / Accepted /
                        // phoneCall / Discarded — variable size). Sync
                        // does NOT route call state — that's the Calls
                        // bounded context, which subscribes to the raw
                        // push bytes via CallsUpdatesProcessor and decodes
                        // the inner shape itself. Here we just consume
                        // the rest of the body so the unknown-ctor
                        // telemetry stops firing on every call event.
                        // Doing this conservatively (jump p to the end)
                        // works because Telegram delivers updatePhoneCall
                        // as the sole update inside an updates / updateShort
                        // envelope; if a later layer ever bundles it with
                        // companion updates we'd swallow those too, but
                        // the Calls bridge already has its own raw-bytes
                        // path so functionality is preserved.
                        p = body.Length;
                        return new UpdateUnsupported(ctor, SliceTail(body, saved));
                    }
                default:
                    // Unknown TL Update — restore p and surface as Unsupported with
                    // remaining bytes so the upper layer can log & continue. Note
                    // this DOES misalign the parser within a containing vector;
                    // DecodeUpdateVector handles that by aborting the loop on null.
                    Vianigram.Kernel.Telemetry.UnknownCtorTelemetry.Observe(
                        "Sync.TlDecoder",
                        ctor,
                        "DecodeUpdate fallthrough at offset " + saved.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    p = saved;
                    return null;
            }
        }

        private static Update DecodeUpdateNewMessage(byte[] body, ref int p, bool channelMode)
        {
            return DecodeMessageBearingUpdate(body, ref p, channelMode, isEdit: false);
        }

        // Shared body for the four message-bearing Update ctors
        // (updateNewMessage, updateNewChannelMessage,
        // updateEditMessage, updateEditChannelMessage). All four have the
        // same wire shape `message:Message pts:int pts_count:int`; the
        // discriminator (channelMode + isEdit) decides which concrete
        // Update subclass we produce so downstream consumers can route
        // edits separately from inserts.
        private static Update DecodeMessageBearingUpdate(byte[] body, ref int p, bool channelMode, bool isEdit)
        {
            // updateNewMessage#1f2b0afd       message:Message pts:int pts_count:int
            // updateNewChannelMessage#62ba04d9 message:Message pts:int pts_count:int
            // updateEditMessage#e40370a3       message:Message pts:int pts_count:int
            // updateEditChannelMessage#1b3f4df7 message:Message pts:int pts_count:int
            //
            // We decode just enough of the message to populate MessageDto:
            //   message#... flags:# id:int from_id:Peer? peer_id:Peer message:string date:int ...
            // The full message decode requires the entire schema. We extract
            // id + peer + body + date best-effort and leave the rest for
            // Messages context to refetch via getHistory if needed.

            int beforeMsg = p;
            uint msgCtor = ReadUInt32Safe(body, ref p);

            int id = 0;
            string peerKey = string.Empty;
            long fromUserId = 0L;
            int date = 0;
            string text = string.Empty;
            int replyToMsgId = 0;
            bool isOutgoing = false;
            int editDate = 0;
            bool isMediaUnread = false;
            bool isSilent = false;

            // message#... has flags:#, then id:int, from_id:Peer (flag-gated in some layers), peer_id:Peer, message:string, date:int.
            // We try a best-effort parse: read flags, id, from_id (if flag.8 set or unconditional in older), peer_id, ... message, date.
            //
            // To stay robust without locking to a specific layer, we DO NOT try to parse the message itself
            // beyond id/peer when the constructor isn't recognized. We fall back to producing an empty MessageDto
            // and rely on Messages context to refetch.
            //
            // Recognised message ctors:
            //   0x9815cec8 — message (layer 173+, with flags2 + via_business_bot_id + offline)
            //   0x76352de5 — message (layer 167-172, single flags word)
            //   0x38116ee0 — message (layer 158-166)
            //   0xa66c7efc — older message
            //   0x2b085862 — messageService
            //   0x90a6ca84 — messageEmpty
            const uint MessageCtorL173 = 0x9815cec8u;  // current layer (with flags2)
            const uint MessageCtorL167 = 0x76352de5u;  // intermediate layer
            const uint MessageCtor1 = 0x38116ee0u;     // older message
            const uint MessageCtor2 = 0xa66c7efcu;     // even older
            const uint MessageEmptyCtor = 0x90a6ca84u; // messageEmpty
            const uint MessageServiceCtor = 0x2b085862u; // messageService

            if (msgCtor == MessageEmptyCtor)
            {
                uint flags = ReadUInt32Safe(body, ref p);
                id = ReadInt32Safe(body, ref p);
                if ((flags & 1u) != 0) // peer_id flag
                {
                    peerKey = DecodePeerKey(body, ref p);
                }
            }
            else if (msgCtor == MessageCtorL173
                || msgCtor == MessageCtorL167
                || msgCtor == MessageCtor1
                || msgCtor == MessageCtor2
                || msgCtor == MessageServiceCtor)
            {
                uint flags = ReadUInt32Safe(body, ref p);
                isOutgoing = (flags & (1u << 1)) != 0;
                isMediaUnread = (flags & (1u << 5)) != 0;
                isSilent = (flags & (1u << 13)) != 0;

                // flags2: layer 173+ message has a SECOND unconditional
                // flags word right after the first one. messageService /
                // older message ctors don't have it. Reading the wrong
                // shape here misaligns id/peer/text and the byte-scan
                // returns 0 for all 4+ channel messages in the diff.
                uint flags2 = 0;
                if (msgCtor == MessageCtorL173)
                {
                    flags2 = ReadUInt32Safe(body, ref p);
                }

                id = ReadInt32Safe(body, ref p);

                // from_id:flags.8?Peer — Peer can be peerUser, peerChat
                // OR peerChannel (anonymous channel posting). The
                // current DecodePeerUserId-only path silently misaligned
                // when from_id was a non-user peer. Use DecodePeerKey
                // and extract userId only when peer is a user.
                if ((flags & (1u << 8)) != 0)
                {
                    int fromStart = p;
                    string fromKey = DecodePeerKey(body, ref p);
                    if (string.IsNullOrEmpty(fromKey))
                    {
                        // Unknown peer ctor — abort optional parsing,
                        // restore p so we don't misalign further.
                        p = fromStart;
                        return null;
                    }
                    string fromKind; long fromId;
                    if (PeerKey.TryParse(fromKey, out fromKind, out fromId) && fromKind == "user")
                    {
                        fromUserId = fromId;
                    }
                }

                // from_boosts_applied:flags.29?int (layer 173+ only).
                // Older layers reuse flag.29 for different fields, but
                // the safest read for the new ctor is to consume the
                // int when set.
                if (msgCtor == MessageCtorL173 && (flags & (1u << 29)) != 0)
                {
                    ReadInt32Safe(body, ref p); // from_boosts_applied
                }

                // peer_id:Peer (always present for message)
                peerKey = DecodePeerKey(body, ref p);

                // saved_peer_id:flags.28?Peer  (skip)
                if ((flags & (1u << 28)) != 0)
                {
                    SkipPeer(body, ref p);
                }
                // fwd_from:flags.2?MessageFwdHeader  — opaque, abort optional parsing on presence
                bool truncatedOptionals = (flags & (1u << 2)) != 0;
                if (!truncatedOptionals)
                {
                    if ((flags & (1u << 11)) != 0) ReadInt64Safe(body, ref p); // via_bot_id

                    // via_business_bot_id:flags2.0?long — layer 173+
                    if (msgCtor == MessageCtorL173 && (flags2 & 1u) != 0)
                    {
                        ReadInt64Safe(body, ref p);
                    }

                    if ((flags & (1u << 3)) != 0)
                    {
                        // reply_to:MessageReplyHeader — try to extract reply_to_msg_id.
                        int beforeReply = p;
                        uint rCtor = ReadUInt32Safe(body, ref p);
                        if (rCtor == 0xa6d57763u) // messageReplyHeader#a6d57763 (older shape)
                        {
                            uint rFlags = ReadUInt32Safe(body, ref p);
                            if ((rFlags & (1u << 4)) != 0) replyToMsgId = ReadInt32Safe(body, ref p);
                            // Stop parsing further reply fields; abort optional traversal.
                            truncatedOptionals = true;
                        }
                        else
                        {
                            p = beforeReply;
                            truncatedOptionals = true;
                        }
                    }
                    if (!truncatedOptionals)
                    {
                        date = ReadInt32Safe(body, ref p);
                        if (msgCtor == MessageServiceCtor)
                        {
                            // action:MessageAction — comprehensive
                            // decode. DescribeMessageAction reads the
                            // ctor magic AND the relevant inner fields
                            // (counts, user ids, durations, amounts)
                            // and produces a fully-formed user-readable
                            // body. Variable-length sub-objects (Photo,
                            // ChatTheme, full GiftCode payload, etc.)
                            // abort the per-action decode and produce
                            // a generic description; otherwise the
                            // cursor advances past the action so the
                            // trailing pts / pts_count read correctly.
                            int actionStart = p;
                            try
                            {
                                uint actionCtor = ReadUInt32Safe(body, ref p);
                                text = DescribeMessageAction(actionCtor, body, ref p, fromUserId);
                            }
                            catch
                            {
                                text = "service message";
                                p = actionStart;
                            }
                        }
                        else
                        {
                            text = ReadStringSafe(body, ref p);
                            // If the message has no caption text but DOES
                            // carry media (flags.9 set), peek the media ctor
                            // and surface a meaningful body — "📷 Photo",
                            // "🎤 Voice", etc. — instead of an empty
                            // string that the UI later renders as a
                            // generic "(media)" placeholder. For
                            // messageMediaDocument we go a step further
                            // and walk the document's attribute vector
                            // to distinguish voice / audio / video-note /
                            // sticker / animation / file (mirrors
                            // TDLib's DocumentsManager priority).
                            if (string.IsNullOrEmpty(text) && (flags & (1u << 9)) != 0)
                            {
                                int mediaStart = p;
                                uint mediaCtor = ReadUInt32Safe(body, ref p);
                                text = DescribeMessageMedia(mediaCtor, body, mediaStart, body.Length);
                                p = mediaStart;
                            }
                        }
                    }
                }
            }
            else
            {
                // Unknown message ctor — restore and emit a minimal MessageDto
                p = beforeMsg;
                // Best-effort: skip the message by reading nothing further reliably.
                // Caller will likely advance pts and the Messages context will refetch
                // via getDifference because we cannot align the trailing pts/pts_count.
                // Return an UpdateUnsupported in the caller's place by passing 0/0.
                return null;
            }

            int pts = ReadInt32Safe(body, ref p);
            int ptsCount = ReadInt32Safe(body, ref p);

            var dto = new MessageDto(
                id: id,
                peerKey: peerKey,
                fromUserId: fromUserId,
                date: date,
                message: text,
                replyToMessageId: replyToMsgId,
                isOutgoing: isOutgoing,
                isMediaUnread: isMediaUnread,
                isSilent: isSilent,
                editDate: editDate);

            if (channelMode)
            {
                long channelId = TryExtractChannelIdFromPeerKey(peerKey);
                if (isEdit) return new UpdateEditChannelMessage(pts, ptsCount, dto, channelId);
                return new UpdateNewChannelMessage(pts, ptsCount, dto, channelId);
            }
            if (isEdit) return new UpdateEditMessage(pts, ptsCount, dto);
            return new UpdateNewMessage(pts, ptsCount, dto);
        }


        private static long TryExtractChannelIdFromPeerKey(string peerKey)
        {
            string kind; long id;
            if (PeerKey.TryParse(peerKey, out kind, out id) && kind == "channel") return id;
            return 0L;
        }

        // -----------------------------------------------------------------
        // Helpers for Peer / SendMessageAction / UserStatus / NotifyPeer
        // -----------------------------------------------------------------

        private const uint PeerUserId = 0x59511722u;     // peerUser#59511722 user_id:long
        private const uint PeerChatId = 0x36c6019au;     // peerChat#36c6019a chat_id:long
        private const uint PeerChannelId = 0xa2a5371eu;  // peerChannel#a2a5371e channel_id:long

        private static string DecodePeerKey(byte[] body, ref int p)
        {
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            long id = ReadInt64Safe(body, ref p);
            switch (ctor)
            {
                case PeerUserId:    return PeerKey.ForUser(id);
                case PeerChatId:    return PeerKey.ForChat(id);
                case PeerChannelId: return PeerKey.ForChannel(id);
                default:
                    p = saved;
                    return string.Empty;
            }
        }

        private static long DecodePeerUserId(byte[] body, ref int p)
        {
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            long id = ReadInt64Safe(body, ref p);
            if (ctor == PeerUserId) return id;
            p = saved;
            return 0L;
        }

        private static void SkipPeer(byte[] body, ref int p)
        {
            // ctor + int64
            ReadUInt32Safe(body, ref p);
            ReadInt64Safe(body, ref p);
        }

        private static string DecodeNotifyPeerKey(byte[] body, ref int p)
        {
            // Telegram NotifyPeer subtypes:
            //   notifyPeer#9fd40bd8 peer:Peer       → "user:N" / "chat:N" / "channel:N"
            //   notifyUsers#b4c83b4c                → "scope:users"
            //   notifyChats#c007cec3                → "scope:chats"
            //   notifyBroadcasts#d612e8ef           → "scope:broadcasts"
            //   notifyForumTopic#226e6308 peer:Peer top_msg_id:int → "topic:peerKey:N"
            //
            // The synthetic "scope:*" keys feed MutedPeersStore so a
            // user-wide "mute all groups" preference is honoured even
            // for peers that have no per-peer override.
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            const uint NotifyPeerCtor = 0x9fd40bd8u;
            const uint NotifyUsersCtor = 0xb4c83b4cu;
            const uint NotifyChatsCtor = 0xc007cec3u;
            const uint NotifyBroadcastsCtor = 0xd612e8efu;
            const uint NotifyForumTopicCtor = 0x226e6308u;
            switch (ctor)
            {
                case NotifyPeerCtor:
                    return DecodePeerKey(body, ref p);
                case NotifyUsersCtor:
                    return "scope:users";
                case NotifyChatsCtor:
                    return "scope:chats";
                case NotifyBroadcastsCtor:
                    return "scope:broadcasts";
                case NotifyForumTopicCtor:
                {
                    string innerKey = DecodePeerKey(body, ref p);
                    int topMsgId = ReadInt32Safe(body, ref p);
                    return "topic:" + innerKey + ":" +
                        topMsgId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                default:
                    p = saved;
                    return string.Empty;
            }
        }

        private static TypingActionKind DecodeTypingAction(byte[] body, ref int p)
        {
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            // sendMessageAction* constructors — we recognize the common ones by id.
            switch (ctor)
            {
                case 0x16bf744eu: return TypingActionKind.Typing;             // sendMessageTypingAction
                case 0xfd5ec8f5u: return TypingActionKind.Cancel;             // sendMessageCancelAction
                case 0xa187d66fu: return TypingActionKind.RecordVideo;        // sendMessageRecordVideoAction
                case 0xe9763aecu: return TypingActionKind.UploadVideo;        // sendMessageUploadVideoAction (unparameterized variant 0xe9763aec; some layers carry progress)
                case 0xd52f73f7u: return TypingActionKind.RecordAudio;        // sendMessageRecordAudioAction
                case 0xf351d7abu: return TypingActionKind.UploadAudio;        // sendMessageUploadAudioAction (variant)
                case 0xd1d34a26u: return TypingActionKind.UploadPhoto;        // sendMessageUploadPhotoAction (variant)
                case 0x8faee98eu: return TypingActionKind.UploadDocument;
                case 0x176f8ba1u: return TypingActionKind.GeoLocation;
                case 0x628cbc6fu: return TypingActionKind.ChooseContact;
                case 0xdd6a8f48u: return TypingActionKind.GamePlay;
                case 0x88f27fbcu: return TypingActionKind.RecordRound;
                case 0x243e1c66u: return TypingActionKind.UploadRound;
                case 0xd92c2285u: return TypingActionKind.SpeakingInGroupCall;
                // Modern (layer 214+) actions.
                case 0xb05ac6b1u: return TypingActionKind.ChooseSticker;       // sendMessageChooseStickerAction
                case 0x25972bcbu: return TypingActionKind.EmojiInteraction;   // sendMessageEmojiInteraction (carries emoji + msg_id payload — we drop it; the kind is enough for the row label)
                case 0xb665d5dcu: return TypingActionKind.EmojiInteractionSeen; // sendMessageEmojiInteractionSeen
                case 0xdbda9246u: return TypingActionKind.ImportingHistory;   // sendMessageHistoryImportAction
                default:
                    return TypingActionKind.Other;
            }
        }

        private static bool TryDecodeUserStatus(byte[] body, ref int p, out UserStatusKind kind, out DateTime? wasOnline)
        {
            wasOnline = null;
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            switch (ctor)
            {
                case 0x09d05049u: // userStatusEmpty
                    kind = UserStatusKind.Empty; return true;
                case 0xedb93949u: // userStatusOnline expires:int
                    {
                        ReadInt32Safe(body, ref p);
                        kind = UserStatusKind.Online; return true;
                    }
                case 0x008c703fu: // userStatusOffline was_online:int
                    {
                        int wasOnlineUnix = ReadInt32Safe(body, ref p);
                        if (wasOnlineUnix > 0)
                        {
                            wasOnline = UnixToDateTime(wasOnlineUnix);
                        }
                        kind = UserStatusKind.Offline; return true;
                    }
                case 0x7b197dc8u: // userStatusRecently (older)
                case 0x7bf09fc0u: // userStatusRecently (newer)
                    kind = UserStatusKind.Recently; return true;
                case 0x541a1d1au: // userStatusLastWeek (older)
                case 0x7ae1ee9cu: // userStatusLastWeek (newer; flagged variant)
                    kind = UserStatusKind.LastWeek; return true;
                case 0x65899777u: // userStatusLastMonth (older)
                case 0x65f9cb83u: // userStatusLastMonth (newer; flagged variant)
                    kind = UserStatusKind.LastMonth; return true;
                default:
                    // Unknown status — surface as Empty and skip; caller handles.
                    p = saved;
                    kind = UserStatusKind.Empty;
                    return false;
            }
        }

        private static string TryDecodeFirstUsername(byte[] body, ref int p)
        {
            int saved = p;
            uint vCtor = ReadUInt32Safe(body, ref p);
            if (vCtor != VectorId) { p = saved; return string.Empty; }
            int count = ReadInt32Safe(body, ref p);
            if (count <= 0) return string.Empty;
            // username#b4073647 flags:# editable:flags.0?true active:flags.1?true username:string
            ReadUInt32Safe(body, ref p); // ctor
            ReadUInt32Safe(body, ref p); // flags
            string name = ReadStringSafe(body, ref p);
            return name ?? string.Empty;
        }

        private static IList<int> DecodeIntVector(byte[] body, ref int p)
        {
            var result = new List<int>();
            int saved = p;
            uint ctor = ReadUInt32Safe(body, ref p);
            if (ctor != VectorId) { p = saved; return result; }
            int count = ReadInt32Safe(body, ref p);
            if (count < 0 || count > 100000) return result;
            for (int i = 0; i < count; i++)
            {
                result.Add(ReadInt32Safe(body, ref p));
            }
            return result;
        }

        private static byte[] SliceTail(byte[] body, int from)
        {
            if (body == null || from < 0 || from >= body.Length) return new byte[0];
            int len = body.Length - from;
            byte[] tail = new byte[len];
            Buffer.BlockCopy(body, from, tail, 0, len);
            return tail;
        }

        private static DateTime UnixToDateTime(int unixSeconds)
        {
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(unixSeconds);
        }

        // -----------------------------------------------------------------
        // Primitive readers
        // -----------------------------------------------------------------

        public static int ReadInt32(byte[] body, ref int p)
        {
            int v = body[p] | (body[p + 1] << 8) | (body[p + 2] << 16) | (body[p + 3] << 24);
            p += 4;
            return v;
        }

        public static int ReadInt32Safe(byte[] body, ref int p)
        {
            if (body == null || p + 4 > body.Length) { p = body == null ? 0 : body.Length; return 0; }
            return ReadInt32(body, ref p);
        }

        public static uint ReadUInt32(byte[] body, ref int p)
        {
            uint v = (uint)body[p]
                   | ((uint)body[p + 1] << 8)
                   | ((uint)body[p + 2] << 16)
                   | ((uint)body[p + 3] << 24);
            p += 4;
            return v;
        }

        public static uint ReadUInt32Safe(byte[] body, ref int p)
        {
            if (body == null || p + 4 > body.Length) { p = body == null ? 0 : body.Length; return 0; }
            return ReadUInt32(body, ref p);
        }

        public static long ReadInt64(byte[] body, ref int p)
        {
            ulong lo = (uint)(body[p] | (body[p + 1] << 8) | (body[p + 2] << 16) | (body[p + 3] << 24));
            ulong hi = (uint)(body[p + 4] | (body[p + 5] << 8) | (body[p + 6] << 16) | (body[p + 7] << 24));
            p += 8;
            return (long)(lo | (hi << 32));
        }

        public static long ReadInt64Safe(byte[] body, ref int p)
        {
            if (body == null || p + 8 > body.Length) { p = body == null ? 0 : body.Length; return 0L; }
            return ReadInt64(body, ref p);
        }

        public static string ReadStringSafe(byte[] body, ref int p)
        {
            byte[] raw = ReadBytesSafe(body, ref p);
            if (raw == null || raw.Length == 0) return string.Empty;
            try
            {
                return Encoding.UTF8.GetString(raw, 0, raw.Length);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static byte[] ReadBytesSafe(byte[] body, ref int p)
        {
            if (body == null || p >= body.Length) return new byte[0];
            int prefixLen;
            int len;
            if (body[p] == 254)
            {
                // long-form prefix: 0xFE | len_lo | len_mid | len_hi
                if (p + 4 > body.Length) { p = body.Length; return new byte[0]; }
                len = body[p + 1] | (body[p + 2] << 8) | (body[p + 3] << 16);
                p += 4;
                prefixLen = 4;
            }
            else
            {
                len = body[p];
                p += 1;
                prefixLen = 1;
            }

            if (len < 0 || p + len > body.Length)
            {
                p = body.Length;
                return new byte[0];
            }

            byte[] result = new byte[len];
            Buffer.BlockCopy(body, p, result, 0, len);
            p += len;

            // TL bytes are padded to a 4-byte boundary including the length prefix.
            int unalignedTotal = prefixLen + len;
            int padding = (4 - (unalignedTotal & 3)) & 3;
            if (padding > 0)
            {
                int newP = p + padding;
                p = newP > body.Length ? body.Length : newP;
            }
            return result;
        }

        // -----------------------------------------------------------------
        // User-readable descriptions for messageAction* and messageMedia*
        // sub-types.
        //
        // The strings are intentionally short and locale-agnostic
        // (English by default, no internalisation yet). The official
        // clients add a sender prefix ("Bob: joined the group") at the
        // UI layer — see PushNotificationsService.BuildMessageBody.
        // Service-message strings are written WITHOUT the subject so
        // the prefix reads naturally ("Bob joined the group" vs the
        // awkward "Bob: Bob joined the group" if we double-printed).
        // -----------------------------------------------------------------

        /// <summary>
        /// Produce a user-readable description for a <c>messageAction*</c>.
        /// Reads
        /// the inner fields (per-action TL shape) so the description
        /// reflects real data ("scored 42", "missed call (1:23)",
        /// "joined the group") and the cursor lands past the payload
        /// so the trailing <c>pts</c> / <c>pts_count</c> in the
        /// containing <c>updateNewChannelMessage</c> read correctly.
        ///
        /// On entry: <paramref name="p"/> sits AFTER the action's
        /// 4-byte ctor magic (caller has already consumed it).
        /// On exit: <paramref name="p"/> is advanced past every
        /// known field; for variable-length sub-objects (Photo,
        /// ChatTheme, etc.) the function aborts mid-payload and the
        /// caller's outer try/catch keeps the cursor at a safe spot.
        /// </summary>
        // The descriptions emitted here use the keyed-format wire
        // convention "~Key" / "~Key|arg1|arg2"
        // so the App layer (LocalizedText.Resolve) can translate them
        // against Strings.resw without Sync needing a UI dependency.
        // The key namespace is "Service.*" for messageAction and
        // "Media.*" for messageMedia descriptors. Resw entries live in
        // Clients/Vianigram.App/Strings/{en-US,es-ES}/Resources.resw.
        internal static string DescribeMessageAction(uint actionCtor, byte[] body, ref int p, long fromUserId)
        {
            switch (actionCtor)
            {
                case 0xb6aef7b0u: return string.Empty;                     // messageActionEmpty

                case 0xbd47cbadu: // messageActionChatCreate title:string users:Vector<long>
                {
                    string title = ReadStringSafe(body, ref p);
                    SkipLongVector(body, ref p);
                    return string.IsNullOrEmpty(title)
                        ? "~Service.GroupCreated"
                        : "~Service.GroupCreatedNamed|" + EscapeArg(title);
                }
                case 0x95d2ac92u: // messageActionChannelCreate title:string
                {
                    string title = ReadStringSafe(body, ref p);
                    return string.IsNullOrEmpty(title)
                        ? "~Service.ChannelCreated"
                        : "~Service.ChannelCreatedNamed|" + EscapeArg(title);
                }
                case 0xb5a1ce5au: // messageActionChatEditTitle title:string
                {
                    string title = ReadStringSafe(body, ref p);
                    return string.IsNullOrEmpty(title)
                        ? "~Service.GroupTitleChanged"
                        : "~Service.GroupTitleChangedTo|" + EscapeArg(title);
                }
                case 0x7fcb13a8u: return "~Service.GroupPhotoChanged";    // messageActionChatEditPhoto
                case 0x95e3fbefu: return "~Service.GroupPhotoRemoved";    // messageActionChatDeletePhoto

                case 0x15cefd00u: // messageActionChatAddUser users:Vector<long>
                {
                    long firstUser; int count;
                    ReadLongVector(body, ref p, out count, out firstUser);
                    if (count == 1 && firstUser != 0 && firstUser == fromUserId)
                        return "~Service.JoinedGroup";
                    if (count > 1) return "~Service.AddedNMembers|" + count;
                    return "~Service.AddedMember";
                }
                case 0xa43f30ccu: // messageActionChatDeleteUser user_id:long
                {
                    long userId = ReadInt64SafeOrZero(body, ref p);
                    if (userId != 0 && userId == fromUserId) return "~Service.LeftGroup";
                    return "~Service.RemovedMember";
                }
                case 0x031224c3u: // messageActionChatJoinedByLink inviter_id:long
                    ReadInt64SafeOrZero(body, ref p);
                    return "~Service.JoinedViaInviteLink";
                case 0xebbca3cbu: return "~Service.JoinedGroup";          // messageActionChatJoinedByRequest
                case 0xf3f25f76u: return "~Service.JoinedTelegram";       // messageActionContactSignUp

                case 0x94bd38edu: return "~Service.PinnedMessage";        // messageActionPinMessage
                case 0x9fbab604u: return "~Service.ClearedHistory";       // messageActionHistoryClear
                case 0x4792929bu: return "~Service.ScreenshotTaken";      // messageActionScreenshotTaken

                case 0x3c134d7bu: // messageActionSetMessagesTTL flags period [auto_setting_from]
                {
                    uint flags = ReadUInt32Safe(body, ref p);
                    int period = ReadInt32Safe(body, ref p);
                    if ((flags & 1u) != 0) ReadInt64SafeOrZero(body, ref p);
                    if (period == 0) return "~Service.AutoDeleteDisabled";
                    return "~Service.AutoDeleteEnabled|" + FormatTtlKeyed(period);
                }

                case 0x80e11a7fu: // messageActionPhoneCall flags video call_id reason duration
                {
                    uint flags = ReadUInt32Safe(body, ref p);
                    bool video = (flags & (1u << 2)) != 0;
                    ReadInt64SafeOrZero(body, ref p); // call_id
                    bool missed = false;
                    if ((flags & 1u) != 0)
                    {
                        uint reason = ReadUInt32Safe(body, ref p);
                        if (reason == 0x85e42301u) missed = true;
                    }
                    int duration = 0;
                    if ((flags & (1u << 1)) != 0) duration = ReadInt32Safe(body, ref p);
                    if (missed) return video ? "~Service.MissedVideoCall" : "~Service.MissedPhoneCall";
                    if (duration > 0)
                    {
                        return (video ? "~Service.VideoCallDuration|" : "~Service.PhoneCallDuration|")
                            + FormatDuration(duration);
                    }
                    return video ? "~Service.VideoCall" : "~Service.PhoneCall";
                }

                case 0x7a0d7f42u:                                          // messageActionGroupCall (older)
                case 0x7842c969u:                                          // messageActionGroupCall (newer)
                {
                    uint flags = ReadUInt32Safe(body, ref p);
                    SkipInputGroupCall(body, ref p);
                    int duration = 0;
                    if ((flags & 1u) != 0) duration = ReadInt32Safe(body, ref p);
                    if (duration > 0) return "~Service.GroupCallEnded|" + FormatDuration(duration);
                    return "~Service.GroupCallStarted";
                }
                case 0xb3a07661u: // messageActionGroupCallScheduled
                    SkipInputGroupCall(body, ref p);
                    ReadInt32Safe(body, ref p);
                    return "~Service.GroupCallScheduled";
                case 0x502f92f7u: // messageActionInviteToGroupCall
                {
                    SkipInputGroupCall(body, ref p);
                    long _; int n;
                    ReadLongVector(body, ref p, out n, out _);
                    return n > 1
                        ? "~Service.GroupCallInvitedMembers|" + n
                        : "~Service.GroupCallInvitedMember";
                }
                case 0xfba6f33du: return "~Service.ConferenceCall";

                case 0x92a72876u: // messageActionGameScore
                {
                    ReadInt64SafeOrZero(body, ref p);
                    int score = ReadInt32Safe(body, ref p);
                    return "~Service.GameScore|" + score;
                }

                case 0xc83d6aecu:                                          // GiftPremium (older)
                case 0xaba0f5c6u:                                          // (newer)
                case 0x48e91302u:
                {
                    ReadUInt32Safe(body, ref p);
                    ReadStringSafe(body, ref p);
                    ReadInt64SafeOrZero(body, ref p);
                    int days = ReadInt32Safe(body, ref p);
                    if (days >= 30) return "~Service.GiftPremiumMonths|" + (days / 30);
                    if (days > 0) return "~Service.GiftPremiumDays|" + days;
                    return "~Service.GiftPremium";
                }
                case 0xed85eab5u:
                case 0x31c48347u: return "~Service.GiftCode";

                case 0xfb6a0bb8u:
                case 0x9bb3ef44u:
                case 0xea2c31d3u: return "~Service.StarGift";
                case 0xa07a08a0u:
                case 0x45d5b021u: return "~Service.SentStars";
                case 0xb00c47a2u: return "~Service.WonStars";

                case 0xa80f51e4u:
                case 0x332ba9edu: return "~Service.GiveawayLaunched";
                case 0x2a9fadc5u:
                case 0x87e2f155u: return "~Service.GiveawayResults";

                case 0x40699cd0u:
                case 0xc624b16eu: return "~Service.PaymentSent";
                case 0x96163f56u:
                case 0x8f31b327u: return "~Service.PaymentReceived";
                case 0x41b3e202u: return "~Service.PaymentRefunded";

                case 0xfae69f56u: // messageActionCustomAction message:string
                {
                    string m = ReadStringSafe(body, ref p);
                    // Custom actions carry verbatim user-visible text;
                    // pass through as-is (no key lookup).
                    return string.IsNullOrEmpty(m) ? "~Service.Generic" : m;
                }

                case 0xe1037f92u:
                case 0x51bdb021u:
                    ReadInt64SafeOrZero(body, ref p);
                    return "~Service.MigratedToSupergroup";
                case 0xea3948e9u:
                case 0xb055eaeeu:
                    ReadStringSafe(body, ref p);
                    ReadInt64SafeOrZero(body, ref p);
                    return "~Service.MigratedFromGroup";

                case 0x0d999256u: // messageActionTopicCreate
                {
                    uint flags = ReadUInt32Safe(body, ref p);
                    string title = ReadStringSafe(body, ref p);
                    ReadInt32Safe(body, ref p);
                    if ((flags & 1u) != 0) ReadInt64SafeOrZero(body, ref p);
                    return string.IsNullOrEmpty(title)
                        ? "~Service.TopicCreated"
                        : "~Service.TopicCreatedNamed|" + EscapeArg(title);
                }
                case 0xc0944820u:
                    ReadUInt32Safe(body, ref p);
                    return "~Service.TopicEdited";

                case 0xaa786345u:
                case 0xb91bbd3au: return "~Service.ChatThemeChanged";

                case 0xb4c38cb5u:
                    ReadStringSafe(body, ref p);
                    return "~Service.WebViewDataSent";

                case 0xc1de0f7cu:
                case 0xc516d679u: return "~Service.BotAllowed";

                case 0xd9a4844fu:
                case 0x31518e9bu: return "~Service.PeerShared";
                case 0x5d7eb95eu: return "~Service.PeerShareReceived";

                case 0x57de635eu: return "~Service.SuggestedProfilePhoto";

                case 0x5060a3f4u:
                    ReadUInt32Safe(body, ref p);
                    return "~Service.WallpaperChanged";

                case 0xcc02aa6du:
                {
                    int boosts = ReadInt32Safe(body, ref p);
                    return boosts > 1
                        ? "~Service.BoostedNTimes|" + boosts
                        : "~Service.Boosted";
                }

                case 0x98e0d697u: return "~Service.GeoNearby";

                default:
                    return "~Service.Generic";
            }
        }

        // Sanitize an arg before embedding in the keyed-format wire
        // string. The format uses '|' as separator — replace any
        // pipe characters in user-supplied text with the broken-bar
        // glyph so the LocalizedText parser doesn't trip.
        private static string EscapeArg(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.IndexOf('|') >= 0 ? s.Replace('|', '¦') : s;
        }

        // Localized TTL formatter that emits a keyed string the App layer
        // translates against Service.Ttl.* entries with proper plural forms
        // per locale.
        private static string FormatTtlKeyed(int seconds)
        {
            if (seconds <= 0) return "~Service.Ttl.Now";
            if (seconds < 60) return seconds == 1 ? "~Service.Ttl.OneSecond" : "~Service.Ttl.NSeconds|" + seconds;
            if (seconds < 3600)
            {
                int m = seconds / 60;
                return m == 1 ? "~Service.Ttl.OneMinute" : "~Service.Ttl.NMinutes|" + m;
            }
            if (seconds < 86400)
            {
                int h = seconds / 3600;
                return h == 1 ? "~Service.Ttl.OneHour" : "~Service.Ttl.NHours|" + h;
            }
            if (seconds < 604800)
            {
                int d = seconds / 86400;
                return d == 1 ? "~Service.Ttl.OneDay" : "~Service.Ttl.NDays|" + d;
            }
            if (seconds < 2592000)
            {
                int w = seconds / 604800;
                return w == 1 ? "~Service.Ttl.OneWeek" : "~Service.Ttl.NWeeks|" + w;
            }
            if (seconds < 31536000)
            {
                int mo = seconds / 2592000;
                return mo == 1 ? "~Service.Ttl.OneMonth" : "~Service.Ttl.NMonths|" + mo;
            }
            int y = seconds / 31536000;
            return y == 1 ? "~Service.Ttl.OneYear" : "~Service.Ttl.NYears|" + y;
        }

        // Skip a Vector<long> at the current cursor. Tolerant of a
        // missing vector marker — restores p and returns 0 entries.
        private static void SkipLongVector(byte[] body, ref int p)
        {
            int saved = p;
            uint vc = ReadUInt32Safe(body, ref p);
            if (vc != VectorId) { p = saved; return; }
            int n = ReadInt32Safe(body, ref p);
            for (int i = 0; i < n && p + 8 <= body.Length; i++)
                ReadInt64Safe(body, ref p);
        }

        // Read a Vector<long>. Sets <paramref name="firstUser"/> to the
        // first long when count >= 1, else 0. Advances past the entire
        // vector. Tolerant of missing vector marker.
        private static void ReadLongVector(byte[] body, ref int p, out int count, out long firstUser)
        {
            count = 0;
            firstUser = 0L;
            int saved = p;
            uint vc = ReadUInt32Safe(body, ref p);
            if (vc != VectorId) { p = saved; return; }
            count = ReadInt32Safe(body, ref p);
            if (count > 0 && p + 8 <= body.Length) firstUser = ReadInt64Safe(body, ref p);
            for (int i = 1; i < count && p + 8 <= body.Length; i++)
                ReadInt64Safe(body, ref p);
        }

        // Skip an InputGroupCall (4 ctor + 8 id + 8 access_hash = 20 bytes).
        private static void SkipInputGroupCall(byte[] body, ref int p)
        {
            int target = p + 20;
            if (target > body.Length) target = body.Length;
            p = target;
        }

        private static long ReadInt64SafeOrZero(byte[] body, ref int p)
        {
            return p + 8 <= body.Length ? ReadInt64Safe(body, ref p) : 0L;
        }

        // Format a TTL duration in seconds as "1 hour", "2 days",
        // "1 week", "3 months", etc. Mirrors LocaleController.formatTTLString.
        private static string FormatTtl(int seconds)
        {
            if (seconds <= 0) return "now";
            if (seconds < 60) return Plural(seconds, "second", "seconds");
            if (seconds < 3600) return Plural(seconds / 60, "minute", "minutes");
            if (seconds < 86400) return Plural(seconds / 3600, "hour", "hours");
            if (seconds < 604800) return Plural(seconds / 86400, "day", "days");
            if (seconds < 2592000) return Plural(seconds / 604800, "week", "weeks");
            if (seconds < 31536000) return Plural(seconds / 2592000, "month", "months");
            return Plural(seconds / 31536000, "year", "years");
        }

        private static string Plural(int n, string singular, string plural)
        {
            return n.ToString(System.Globalization.CultureInfo.InvariantCulture) + " " + (n == 1 ? singular : plural);
        }

        // Format a call duration in seconds as "0:42", "1:23", "1:02:34".
        private static string FormatDuration(int seconds)
        {
            if (seconds < 0) seconds = 0;
            int s = seconds % 60;
            int totalMinutes = seconds / 60;
            int m = totalMinutes % 60;
            int h = totalMinutes / 60;
            if (h > 0)
            {
                return h.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ":" + m.ToString("D2", System.Globalization.CultureInfo.InvariantCulture)
                    + ":" + s.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            }
            return totalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":" + s.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string DescribeMessageMedia(uint mediaCtor, byte[] body, int searchFrom, int searchTo)
        {
            switch (mediaCtor)
            {
                case 0x3ded6320u: return string.Empty;                     // messageMediaEmpty
                case 0x695150d7u:                                          // messageMediaPhoto
                case 0xb5223b0fu: return "~Media.Photo";
                case 0x9cb070d7u:                                          // messageMediaDocument (older)
                case 0x4cf4d72du:                                          // messageMediaDocument (current)
                {
                    DocumentSubtype sub = InferDocumentSubtype(body, searchFrom, searchTo);
                    switch (sub)
                    {
                        case DocumentSubtype.Voice:        return "~Media.Voice";
                        case DocumentSubtype.Audio:        return "~Media.Music";
                        case DocumentSubtype.VideoNote:    return "~Media.VideoNote";
                        case DocumentSubtype.Video:        return "~Media.Video";
                        case DocumentSubtype.Sticker:      return "~Media.Sticker";
                        case DocumentSubtype.CustomEmoji:  return "~Media.AnimatedEmoji";
                        case DocumentSubtype.Animation:    return "~Media.Gif";
                        case DocumentSubtype.File:
                        default:                            return "~Media.File";
                    }
                }
                case 0x70322949u: return "~Media.Contact";                 // messageMediaContact
                case 0x56e0d474u: return "~Media.Location";                // messageMediaGeo
                case 0xb6f8639du:                                          // messageMediaGeoLive (older)
                case 0xb940c666u: return "~Media.LiveLocation";            // (newer)
                case 0x2ec0533fu: return "~Media.Venue";                   // messageMediaVenue
                case 0xfdb19008u: return "~Media.Game";                    // messageMediaGame
                case 0xf6a548d3u:
                case 0xb6abc341u: return "~Media.Invoice";
                case 0x4bd6e798u: return "~Media.Poll";
                case 0xa32dd600u:
                case 0x84551347u:
                case 0xddf10c3bu: return "~Media.Link";
                case 0x9f84f49eu: return "~Media.Unsupported";
                case 0x3f7ee58bu: return "~Media.Dice";
                case 0x68cb6283u: return "~Media.Story";
                case 0xdaad85b0u: return "~Media.Giveaway";
                case 0xc6991068u: return "~Media.GiveawayResults";
                case 0xa8852491u: return "~Media.PaidMedia";
                default:
                    return "~Media.Generic";
            }
        }

        /// <summary>
        /// TDLib-style document subtype enum. Drives the toast emoji /
        /// label for every <c>messageMediaDocument</c> a peer can send.
        /// </summary>
        internal enum DocumentSubtype
        {
            File = 0,
            Voice,
            Audio,
            VideoNote,
            Video,
            Sticker,
            CustomEmoji,
            Animation
        }

        /// <summary>
        /// Scan a slice of TL bytes looking for known DocumentAttribute
        /// constructors. When found,
        /// peek their first 4-byte flags word to detect sub-modifiers
        /// (audio.voice, video.round_message). Returns the most-specific
        /// subtype found per TDLib's priority order:
        ///
        ///   Audio+voice → Voice
        ///   Audio       → Audio
        ///   CustomEmoji → CustomEmoji
        ///   Sticker     → Sticker
        ///   Video+round → VideoNote
        ///   Video       → Video
        ///   Animated    → Animation
        ///   (none)      → File
        ///
        /// We scan instead of structurally parsing the Document because
        /// Document has variable-length thumbs / video_thumbs vectors
        /// before reaching attributes; reproducing the schema for those
        /// is heavy and brittle. Scanning for known 32-bit ctor magic
        /// numbers has a tiny false-positive rate (~2^-32 per offset)
        /// and the match-then-peek-flags pattern further reduces noise.
        /// </summary>
        /// <summary>
        /// Decode the relevant fields of <c>peerNotifySettings#99622c0c</c>:
        /// flags + show_previews + silent + mute_until. Sounds (per-OS
        /// ringtones, stories sounds) are skipped so the cursor lands
        /// somewhere "safe" — we accept the misalignment because the
        /// containing update isn't a vector and we don't have anything
        /// after it to align to.
        /// </summary>
        private static void TryDecodePeerNotifySettings(
            byte[] body, ref int p,
            out bool? showPreviews, out bool? silent, out int muteUntil)
        {
            showPreviews = null;
            silent = null;
            muteUntil = 0;

            int saved = p;
            try
            {
                uint ctor = ReadUInt32Safe(body, ref p);
                // peerNotifySettings#99622c0c (current) and any fwd-compat
                // variants (the codebase has seen multiple in topCtors logs).
                if (ctor != 0x99622c0cu && ctor != 0xaf509d20u && ctor != 0x9c19a443u)
                {
                    // Unknown shape — restore and bail. Caller already
                    // tolerates null/0.
                    p = saved;
                    return;
                }
                uint flags = ReadUInt32Safe(body, ref p);
                if ((flags & (1u << 0)) != 0)
                {
                    showPreviews = ReadBoolSafe(body, ref p);
                }
                if ((flags & (1u << 1)) != 0)
                {
                    silent = ReadBoolSafe(body, ref p);
                }
                if ((flags & (1u << 2)) != 0)
                {
                    muteUntil = ReadInt32Safe(body, ref p);
                }
                // The remaining fields (sounds + stories) are skipped:
                // we don't honour them on WP 8.1, and skipping them via
                // schema-aware reads would require a NotificationSound
                // decoder. Cursor misalignment past this point is OK
                // because peerNotifySettings is the LAST field of
                // updateNotifySettings — nothing trails inside the
                // containing update.
            }
            catch
            {
                p = saved;
            }
        }

        /// <summary>
        /// Read a TL Bool (boolTrue#997275b5 / boolFalse#bc799737).
        /// </summary>
        private static bool? ReadBoolSafe(byte[] body, ref int p)
        {
            int saved = p;
            try
            {
                uint ctor = ReadUInt32Safe(body, ref p);
                if (ctor == BoolTrueId) return true;
                if (ctor == BoolFalseId) return false;
                p = saved;
                return null;
            }
            catch
            {
                p = saved;
                return null;
            }
        }

        internal static DocumentSubtype InferDocumentSubtype(byte[] body, int from, int to)
        {
            const uint AttrAudio       = 0x9852f9c6u;
            const uint AttrVideo       = 0x43c57c48u;
            const uint AttrSticker     = 0x6319d612u;
            const uint AttrCustomEmoji = 0xfd149899u;
            const uint AttrAnimated    = 0x11b58939u;
            // Filename / image-size / has-stickers don't change the
            // subtype — we ignore them.

            if (body == null || from < 0) return DocumentSubtype.File;
            int end = to > body.Length ? body.Length : to;
            if (end - from < 4) return DocumentSubtype.File;

            bool hasAudio = false, audioVoice = false;
            bool hasVideo = false, videoRound = false;
            bool hasSticker = false, hasCustomEmoji = false, hasAnimated = false;

            for (int i = from; i + 4 <= end; i += 4)
            {
                uint u = (uint)(body[i]
                    | (body[i + 1] << 8)
                    | (body[i + 2] << 16)
                    | (body[i + 3] << 24));
                if (u == AttrAudio)
                {
                    hasAudio = true;
                    if (i + 8 <= end)
                    {
                        uint flagsWord = (uint)(body[i + 4]
                            | (body[i + 5] << 8)
                            | (body[i + 6] << 16)
                            | (body[i + 7] << 24));
                        if ((flagsWord & (1u << 10)) != 0) audioVoice = true;
                    }
                }
                else if (u == AttrVideo)
                {
                    hasVideo = true;
                    if (i + 8 <= end)
                    {
                        uint flagsWord = (uint)(body[i + 4]
                            | (body[i + 5] << 8)
                            | (body[i + 6] << 16)
                            | (body[i + 7] << 24));
                        if ((flagsWord & (1u << 0)) != 0) videoRound = true;
                    }
                }
                else if (u == AttrSticker) hasSticker = true;
                else if (u == AttrCustomEmoji) hasCustomEmoji = true;
                else if (u == AttrAnimated) hasAnimated = true;
            }

            if (hasAudio && audioVoice) return DocumentSubtype.Voice;
            if (hasAudio) return DocumentSubtype.Audio;
            if (hasCustomEmoji) return DocumentSubtype.CustomEmoji;
            if (hasSticker) return DocumentSubtype.Sticker;
            if (hasVideo && videoRound) return DocumentSubtype.VideoNote;
            if (hasVideo) return DocumentSubtype.Video;
            if (hasAnimated) return DocumentSubtype.Animation;
            return DocumentSubtype.File;
        }
    }
}
