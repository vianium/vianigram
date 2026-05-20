// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the Search RPC shapes this
    /// context issues. Mirrors the per-context approach used in
    /// <c>Vianigram.Settings</c>, <c>Vianigram.Notifications</c> and
    /// <c>Vianigram.Stickers</c>.
    ///
    /// <para><b>Supported requests</b>:</para>
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>messages.search#a0fda762</c><br/>
    ///     flags:# peer:InputPeer q:string from_id:flags.0?InputPeer
    ///     saved_peer_id:flags.2?InputPeer saved_reaction:flags.3?Vector&lt;Reaction&gt;
    ///     top_msg_id:flags.1?int filter:MessagesFilter min_date:int max_date:int
    ///     offset_id:int add_offset:int limit:int max_id:int min_id:int hash:long
    ///     = messages.Messages
    ///   </description></item>
    ///   <item><description>
    ///     <c>messages.searchGlobal#4bc6589a</c><br/>
    ///     flags:# broadcasts_only:flags.1?true groups_only:flags.2?true users_only:flags.3?true
    ///     folder_id:flags.0?int q:string filter:MessagesFilter min_date:int max_date:int
    ///     offset_rate:int offset_peer:InputPeer offset_id:int limit:int = messages.Messages
    ///   </description></item>
    ///   <item><description>
    ///     <c>contacts.search#11f812d8</c> q:string limit:int = contacts.Found
    ///   </description></item>
    /// </list>
    ///
    /// <para>All multi-byte integers are little-endian (TL convention). Vector
    /// uses the boxed constructor <c>0x1cb5c415</c> followed by an int32 length
    /// and the element payloads.</para>
    ///
    /// <para><b>InputPeer access-hash</b>: V1 encodes access_hash=0 for
    /// user/channel peers (the production path resolves access hashes through
    /// the MTProto session cache; the in-process composition root may
    /// supply a wrapped <see cref="Ports.Outbound.IMtProtoRpcPort"/> that
    /// rewrites the hash on the way out). The opaque peer-key format
    /// (<c>"user:42"</c> / <c>"chat:7"</c> / <c>"channel:1001"</c>) is parsed
    /// here.</para>
    /// </summary>
    internal static class TlEncoder
    {
        // ---- TL constructor ids ----------------------------------------------

        public const uint CtorMessagesSearch = 0xa0fda762;
        public const uint CtorMessagesSearchGlobal = 0x4bc6589a;
        public const uint CtorContactsSearch = 0x11f812d8;

        // InputPeer
        public const uint CtorInputPeerEmpty = 0x7f3b18ea;
        public const uint CtorInputPeerSelf = 0x7da07ec9;
        public const uint CtorInputPeerChat = 0x35a95cb9;
        public const uint CtorInputPeerUser = 0xdde8a54c;
        public const uint CtorInputPeerChannel = 0x27bcbbfc;

        // MessagesFilter
        public const uint CtorInputMessagesFilterEmpty = 0x57e2f66c;
        public const uint CtorInputMessagesFilterPhotos = 0x9609a51c;
        public const uint CtorInputMessagesFilterVideo = 0x9fc00e65;
        public const uint CtorInputMessagesFilterDocument = 0x9eddf188;
        public const uint CtorInputMessagesFilterVoice = 0x50f5c392;
        public const uint CtorInputMessagesFilterMusic = 0x3751b49e;
        public const uint CtorInputMessagesFilterUrl = 0x7ef0dd87;
        public const uint CtorInputMessagesFilterGif = 0xffc86587;
        public const uint CtorInputMessagesFilterPhoneCalls = 0x80c99768;

        public const uint CtorVector = 0x1cb5c415;

        // ---- Public API -------------------------------------------------------

        /// <summary>
        /// <c>messages.searchGlobal#4bc6589a</c> — V1 sends no folder_id, no
        /// flag-only switches; date bounds default to 0 (no constraint),
        /// offset_rate = 0.
        /// </summary>
        public static byte[] EncodeSearchGlobal(string query, SearchFilter filter, SearchCursor cursor)
        {
            if (cursor == null) throw new ArgumentNullException("cursor");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorMessagesSearchGlobal);
                w.Write((int)0); // flags: no folder_id, no broadcasts_only/groups_only/users_only
                WriteString(w, query ?? string.Empty);
                WriteFilter(w, filter);
                w.Write((int)0); // min_date
                w.Write((int)0); // max_date
                w.Write((int)0); // offset_rate
                WriteInputPeer(w, cursor.OffsetPeerKey); // offset_peer
                w.Write(cursor.OffsetId); // offset_id
                w.Write(cursor.Limit);    // limit
                return ms.ToArray();
            }
        }

        /// <summary>
        /// <c>messages.search#a0fda762</c> — V1 sends only the peer + query +
        /// filter + cursor; from_id / top_msg_id / saved_* flags are not set.
        /// </summary>
        public static byte[] EncodeSearchInChat(string peerKey, string query, SearchFilter filter, SearchCursor cursor)
        {
            if (cursor == null) throw new ArgumentNullException("cursor");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorMessagesSearch);
                w.Write((int)0); // flags: no from_id, no top_msg_id, no saved_*
                WriteInputPeer(w, peerKey); // peer
                WriteString(w, query ?? string.Empty);
                // from_id, top_msg_id, saved_peer_id, saved_reaction omitted (flags=0)
                WriteFilter(w, filter);
                w.Write((int)0);          // min_date
                w.Write((int)0);          // max_date
                w.Write(cursor.OffsetId); // offset_id
                w.Write((int)0);          // add_offset
                w.Write(cursor.Limit);    // limit
                w.Write((int)0);          // max_id
                w.Write((int)0);          // min_id
                w.Write((long)0);         // hash
                return ms.ToArray();
            }
        }

        /// <summary><c>contacts.search#11f812d8</c> q:string limit:int = contacts.Found</summary>
        public static byte[] EncodeContactsSearch(string query, int limit)
        {
            if (limit <= 0) limit = 20;
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorContactsSearch);
                WriteString(w, query ?? string.Empty);
                w.Write(limit);
                return ms.ToArray();
            }
        }

        // ---- TL primitives ----------------------------------------------------

        private static void WriteFilter(BinaryWriter w, SearchFilter filter)
        {
            switch (filter)
            {
                case SearchFilter.All:       w.Write(CtorInputMessagesFilterEmpty); break;
                case SearchFilter.Photos:    w.Write(CtorInputMessagesFilterPhotos); break;
                case SearchFilter.Videos:    w.Write(CtorInputMessagesFilterVideo); break;
                case SearchFilter.Documents: w.Write(CtorInputMessagesFilterDocument); break;
                case SearchFilter.Voice:     w.Write(CtorInputMessagesFilterVoice); break;
                case SearchFilter.Music:     w.Write(CtorInputMessagesFilterMusic); break;
                case SearchFilter.Url:       w.Write(CtorInputMessagesFilterUrl); break;
                case SearchFilter.GIF:       w.Write(CtorInputMessagesFilterGif); break;
                case SearchFilter.Phone:     w.Write(CtorInputMessagesFilterPhoneCalls); break;
                default:                     w.Write(CtorInputMessagesFilterEmpty); break;
            }
        }

        /// <summary>
        /// Encode an opaque peer key as an <c>InputPeer</c>. Empty / null /
        /// unknown maps to <c>inputPeerEmpty</c>. Format:
        /// <c>"user:&lt;id&gt;"</c>, <c>"chat:&lt;id&gt;"</c>,
        /// <c>"channel:&lt;id&gt;"</c>, <c>"self"</c>.
        /// </summary>
        private static void WriteInputPeer(BinaryWriter w, string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey))
            {
                w.Write(CtorInputPeerEmpty);
                return;
            }

            if (string.Equals(peerKey, "self", StringComparison.Ordinal))
            {
                w.Write(CtorInputPeerSelf);
                return;
            }

            int colon = peerKey.IndexOf(':');
            if (colon <= 0 || colon >= peerKey.Length - 1)
            {
                w.Write(CtorInputPeerEmpty);
                return;
            }

            string kind = peerKey.Substring(0, colon);
            string idText = peerKey.Substring(colon + 1);
            long id;
            if (!long.TryParse(idText, out id))
            {
                w.Write(CtorInputPeerEmpty);
                return;
            }

            if (string.Equals(kind, "user", StringComparison.Ordinal))
            {
                w.Write(CtorInputPeerUser);
                w.Write(id);
                w.Write((long)0); // access_hash — resolved by MTProto session cache
                return;
            }
            if (string.Equals(kind, "chat", StringComparison.Ordinal))
            {
                w.Write(CtorInputPeerChat);
                w.Write(id);
                return;
            }
            if (string.Equals(kind, "channel", StringComparison.Ordinal))
            {
                w.Write(CtorInputPeerChannel);
                w.Write(id);
                w.Write((long)0); // access_hash — resolved by MTProto session cache
                return;
            }

            w.Write(CtorInputPeerEmpty);
        }

        /// <summary>
        /// TL string encoding: 1 length byte + bytes + padding to 4-byte align
        /// (or, for length &gt;= 254, 0xFE + 3 length bytes + bytes + padding).
        /// </summary>
        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? new byte[0] : Encoding.UTF8.GetBytes(s);
            WriteBytes(w, bytes);
        }

        private static void WriteBytes(BinaryWriter w, byte[] bytes)
        {
            int len = bytes == null ? 0 : bytes.Length;
            int padding;
            if (len < 254)
            {
                w.Write((byte)len);
                if (len > 0) w.Write(bytes);
                padding = (4 - ((len + 1) % 4)) % 4;
            }
            else
            {
                w.Write((byte)254);
                w.Write((byte)(len & 0xFF));
                w.Write((byte)((len >> 8) & 0xFF));
                w.Write((byte)((len >> 16) & 0xFF));
                w.Write(bytes);
                padding = (4 - (len % 4)) % 4;
            }
            for (int i = 0; i < padding; i++) w.Write((byte)0);
        }
    }
}
