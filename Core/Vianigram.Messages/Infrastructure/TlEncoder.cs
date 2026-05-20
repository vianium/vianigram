// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Infrastructure
{
    /// <summary>
    /// TL serializers for the messages.* and channels.* methods used by this
    /// bounded context.
    ///
    /// These are minimal encoders — the production data plane lives in the
    /// native <c>Vianigram.Core.Tl</c> project. They use straightforward
    /// little-endian IO so the managed handlers can fall through to a simple
    /// in-memory adapter for tests and wiring without taking a hard dep on
    /// the native WinMD.
    ///
    /// Constructor IDs (TL layer 214, sourced from
    /// <c>Vianigram.Core.Tl/src/infrastructure/generated/tl_layer_214.h</c>):
    ///
    ///   messages.sendMessage      0x280d096f
    ///   messages.editMessage      0x48f71778
    ///   messages.deleteMessages   0xe58e95d2
    ///   messages.readHistory      0x0e306d3a
    ///   messages.getHistory       0x4423e6c5
    ///   channels.readHistory      0xcc104937
    ///   channels.deleteMessages   0x84c1fd4e
    ///
    /// Boxed peer constructors (input):
    ///
    ///   inputPeerEmpty            0x7f3b18ea
    ///   inputPeerSelf             0x7da07ec9
    ///   inputPeerChat             0x35a95cb9   (chat_id : long)
    ///   inputPeerUser             0xdde8a54c   (user_id, access_hash : long)
    ///   inputPeerChannel          0x27bcbbfc   (channel_id, access_hash : long)
    ///
    /// Note: access_hash is not yet plumbed end-to-end through this MVP path;
    /// adapters should override these encoders when the full TL stack is in
    /// place.
    /// </summary>
    internal static class TlEncoder
    {
        // ---------- Method constructor ids ----------

        public const uint CtorMessagesSendMessage = 0x280d096fu;
        public const uint CtorMessagesEditMessage = 0x48f71778u;
        public const uint CtorMessagesDeleteMessages = 0xe58e95d2u;
        public const uint CtorMessagesReadHistory = 0x0e306d3au;
        public const uint CtorMessagesGetHistory = 0x4423e6c5u;
        public const uint CtorChannelsReadHistory = 0xcc104937u;
        public const uint CtorChannelsDeleteMessages = 0x84c1fd4eu;

        // ---------- Peer ctors ----------

        public const uint CtorInputPeerSelf = 0x7da07ec9u;
        public const uint CtorInputPeerChat = 0x35a95cb9u;
        // inputPeerUser#dde8a54c user_id:long access_hash:long.
        // The legacy 0xf21158c6 is `inputUser` (a different peer-context
        // ctor used by users.* RPCs); using it here makes the server reject
        // messages.getHistory with PEER_ID_INVALID even when the access_hash
        // is correct.
        public const uint CtorInputPeerUser = 0xdde8a54cu;
        public const uint CtorInputPeerChannel = 0x27bcbbfcu;

        // Boxed Vector<long>
        public const uint CtorVector = 0x1cb5c415u;

        // ---------- Public encoders ----------

        public static byte[] EncodeSendMessage(string peerKey, string text, long? replyTo, long randomId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorMessagesSendMessage);

                // flags: bit 0 = no_webpage(unset means linkpreview), bit 0 of replyTo,
                // For MVP we only set bit 0 if replyTo is provided. Flags layout per
                // upstream schema: we keep it simple with explicit fields.
                uint flags = 0;
                if (replyTo.HasValue) flags |= 1u; // reply_to_msg_id present
                w.Write(flags);

                WriteInputPeer(w, peerKey);

                if (replyTo.HasValue) w.Write((int)replyTo.Value);

                WriteString(w, text);
                w.Write(randomId); // random_id : long

                // reply_markup, entities, schedule_date, send_as: omitted (flag bits not set)

                w.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] EncodeEditMessage(string peerKey, long messageId, string newText)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorMessagesEditMessage);

                uint flags = 1u << 11; // bit 11 = message present
                w.Write(flags);

                WriteInputPeer(w, peerKey);
                w.Write((int)messageId);
                WriteString(w, newText);

                w.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] EncodeMessagesDeleteMessages(long messageId, bool revoke)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorMessagesDeleteMessages);

                uint flags = 0;
                if (revoke) flags |= 1u; // bit 0 = revoke
                w.Write(flags);

                // id : Vector<int>
                w.Write(CtorVector);
                w.Write(1); // count
                w.Write((int)messageId);

                w.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] EncodeChannelsDeleteMessages(string peerKey, long messageId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorChannelsDeleteMessages);

                WriteInputChannel(w, peerKey);

                w.Write(CtorVector);
                w.Write(1);
                w.Write((int)messageId);

                w.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] EncodeMessagesReadHistory(string peerKey, long upToMessageId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorMessagesReadHistory);
                WriteInputPeer(w, peerKey);
                w.Write((int)upToMessageId);

                w.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] EncodeChannelsReadHistory(string peerKey, long upToMessageId)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorChannelsReadHistory);
                WriteInputChannel(w, peerKey);
                w.Write((int)upToMessageId);

                w.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] EncodeGetHistory(string peerKey, long? offsetMsgId, int limit)
        {
            return EncodeGetHistory(peerKey, offsetMsgId, limit, 0L);
        }

        /// <summary>
        /// Variant that accepts the resolved access_hash for the peer (or 0
        /// for chat/User peers without one). Telegram rejects
        /// inputPeerUser / inputPeerChannel with mismatched access_hash via
        /// PEER_ID_INVALID — call sites should resolve the value from
        /// <see cref="Ports.Outbound.IPeerAccessHashPort"/> before invoking.
        /// </summary>
        public static byte[] EncodeGetHistory(string peerKey, long? offsetMsgId, int limit, long accessHash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorMessagesGetHistory);
                WriteInputPeer(w, peerKey, accessHash);

                w.Write(offsetMsgId.HasValue ? (int)offsetMsgId.Value : 0); // offset_id
                w.Write(0);            // offset_date
                w.Write(0);            // add_offset
                w.Write(limit);        // limit
                w.Write(0);            // max_id
                w.Write(0);            // min_id
                w.Write(0L);           // hash : long

                w.Flush();
                return ms.ToArray();
            }
        }

        // ---------- Helpers ----------

        private static void WriteInputPeer(BinaryWriter w, string peerKey)
        {
            WriteInputPeer(w, peerKey, 0L);
        }

        private static void WriteInputPeer(BinaryWriter w, string peerKey, long accessHash)
        {
            PeerKind kind;
            long id;
            if (!PeerKey.TryParse(peerKey, out kind, out id))
                throw new ArgumentException("invalid peerKey: " + peerKey);

            switch (kind)
            {
                case PeerKind.User:
                    w.Write(CtorInputPeerUser);
                    w.Write(id);
                    w.Write(accessHash); // resolved from peer cache
                    break;
                case PeerKind.Chat:
                    w.Write(CtorInputPeerChat);
                    w.Write(id);
                    break;
                case PeerKind.Channel:
                    w.Write(CtorInputPeerChannel);
                    w.Write(id);
                    w.Write(accessHash); // resolved from peer cache
                    break;
                default:
                    throw new ArgumentException("unsupported peer kind");
            }
        }

        private static void WriteInputChannel(BinaryWriter w, string peerKey)
        {
            PeerKind kind;
            long id;
            if (!PeerKey.TryParse(peerKey, out kind, out id) || kind != PeerKind.Channel)
                throw new ArgumentException("expected channel peerKey: " + peerKey);

            // inputChannel#f35aec28 channel_id:long access_hash:long
            w.Write(0xf35aec28u);
            w.Write(id);
            w.Write(0L);
        }

        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = s == null ? new byte[0] : Encoding.UTF8.GetBytes(s);
            int len = bytes.Length;
            int padding;

            if (len <= 253)
            {
                w.Write((byte)len);
                w.Write(bytes);
                padding = (4 - ((len + 1) % 4)) % 4;
            }
            else
            {
                w.Write((byte)254);
                w.Write((byte)(len & 0xff));
                w.Write((byte)((len >> 8) & 0xff));
                w.Write((byte)((len >> 16) & 0xff));
                w.Write(bytes);
                padding = (4 - (len % 4)) % 4;
            }

            for (int i = 0; i < padding; i++) w.Write((byte)0);
        }
    }
}
