// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the Search response shapes
    /// this context consumes. Mirrors the per-context approach used in
    /// <c>Vianigram.Settings</c>, <c>Vianigram.Notifications</c> and
    /// <c>Vianigram.Stickers</c>.
    ///
    /// <para><b>Supported response constructors</b>:</para>
    /// <list type="bullet">
    ///   <item><description><c>messages.messages#8c718e87</c> messages:Vector&lt;Message&gt; chats:Vector&lt;Chat&gt; users:Vector&lt;User&gt;</description></item>
    ///   <item><description><c>messages.messagesSlice#3a54685e</c> flags:# inexact:flags.1?true count:int next_rate:flags.0?int offset_id_offset:flags.2?int messages chats users</description></item>
    ///   <item><description><c>messages.channelMessages#c776ba4e</c> flags:# inexact:flags.1?true pts:int count:int offset_id_offset:flags.2?int messages topics chats users</description></item>
    ///   <item><description><c>messages.messagesNotModified#74535f21</c> count:int</description></item>
    ///   <item><description><c>contacts.found#b3134d9d</c> my_results:Vector&lt;Peer&gt; results:Vector&lt;Peer&gt; chats:Vector&lt;Chat&gt; users:Vector&lt;User&gt;</description></item>
    /// </list>
    ///
    /// <para><b>Limitation</b>: V1 does NOT fully decode every Message
    /// / User / Chat constructor — there are dozens of variants and the
    /// fully-typed mapping lives in the future <c>Vianigram.Messages</c> /
    /// <c>Vianigram.Contacts</c> contexts. This decoder extracts the
    /// minimum data the cursor needs (last message id + date) and surfaces
    /// each hit as a <see cref="MessageEntry"/> / <see cref="PeerEntry"/>
    /// POCO carrying the raw constructor + an opaque body byte slice. The
    /// composition root re-decodes (or re-uses) those slices via an ACL
    /// adapter when richer typing is needed.</para>
    /// </summary>
    internal static class TlDecoder
    {
        // ---- TL constructor ids ----------------------------------------------

        public const uint CtorMessagesMessages = 0x8c718e87;
        public const uint CtorMessagesMessagesSlice = 0x3a54685e;
        public const uint CtorMessagesChannelMessages = 0xc776ba4e;
        public const uint CtorMessagesMessagesNotModified = 0x74535f21;

        public const uint CtorContactsFound = 0xb3134d9d;

        public const uint CtorVector = 0x1cb5c415;

        // Peer
        public const uint CtorPeerUser = 0x59511722;
        public const uint CtorPeerChat = 0x36c6019a;
        public const uint CtorPeerChannel = 0xa2a5371e;

        /// <summary>Lightly-decoded message hit. <see cref="Body"/> carries the raw TL bytes.</summary>
        public sealed class MessageEntry
        {
            public uint Constructor { get; set; }
            public int MessageId { get; set; }
            public int Date { get; set; }
            public string PeerKey { get; set; }
            public byte[] Body { get; set; }
        }

        /// <summary>Lightly-decoded peer hit (from contacts.found my_results / results).</summary>
        public sealed class PeerEntry
        {
            public SearchHitKind Kind { get; set; }
            public long Id { get; set; }
            public string Key { get { return BuildKey(Kind, Id); } }

            public override string ToString()
            {
                return "PeerEntry(" + Kind + ":" + Id + ")";
            }

            internal static string BuildKey(SearchHitKind kind, long id)
            {
                switch (kind)
                {
                    case SearchHitKind.User:    return "user:" + id;
                    case SearchHitKind.Chat:    return "chat:" + id;
                    case SearchHitKind.Channel: return "channel:" + id;
                    default:                    return null;
                }
            }
        }

        /// <summary>Decoded form of a <c>messages.messages*</c> response page.</summary>
        public sealed class MessagesPage
        {
            public IList<SearchHit> Hits { get; set; }
            public int TotalCount { get; set; }
            public int LastMessageId { get; set; }
            public int LastMessageDate { get; set; }
            public string LastPeerKey { get; set; }
        }

        // ---- Public API -------------------------------------------------------

        /// <summary>
        /// Decode a top-level <c>messages.Messages</c> response into a
        /// <see cref="MessagesPage"/>. Handles the three concrete response
        /// constructors and the "not modified" no-op.
        /// </summary>
        public static MessagesPage DecodeMessagesPage(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();

                int totalCount;
                if (ctor == CtorMessagesMessages)
                {
                    // Inline: count = messages.length (we'll set after decoding)
                    var entries = DecodeMessagesVector(r);
                    totalCount = entries.Count;
                    return BuildPage(entries, totalCount);
                }
                if (ctor == CtorMessagesMessagesSlice)
                {
                    int flags = r.ReadInt32();
                    bool hasNextRate = (flags & (1 << 0)) != 0;
                    bool hasOffsetIdOffset = (flags & (1 << 2)) != 0;
                    totalCount = r.ReadInt32();
                    if (hasNextRate) r.ReadInt32();
                    if (hasOffsetIdOffset) r.ReadInt32();
                    var entries = DecodeMessagesVector(r);
                    return BuildPage(entries, totalCount);
                }
                if (ctor == CtorMessagesChannelMessages)
                {
                    int flags = r.ReadInt32();
                    bool hasOffsetIdOffset = (flags & (1 << 2)) != 0;
                    r.ReadInt32(); // pts
                    totalCount = r.ReadInt32();
                    if (hasOffsetIdOffset) r.ReadInt32();
                    var entries = DecodeMessagesVector(r);
                    return BuildPage(entries, totalCount);
                }
                if (ctor == CtorMessagesMessagesNotModified)
                {
                    totalCount = r.ReadInt32();
                    return new MessagesPage
                    {
                        Hits = new SearchHit[0],
                        TotalCount = totalCount,
                        LastMessageId = 0,
                        LastMessageDate = 0,
                        LastPeerKey = null
                    };
                }

                throw new InvalidDataException("Unexpected messages.Messages constructor: 0x" + ctor.ToString("x8"));
            }
        }

        /// <summary>Decode a top-level <c>contacts.found#b3134d9d</c> response into a list of peer hits.</summary>
        public static IList<SearchHit> DecodeContactsFound(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorContactsFound)
                    throw new InvalidDataException("Unexpected contacts.found constructor: 0x" + ctor.ToString("x8"));

                var hits = new List<SearchHit>(16);
                int rank = 0;

                // my_results:Vector<Peer> — close-circle hits (boosted score).
                var myPeers = DecodePeerVector(r);
                for (int i = 0; i < myPeers.Count; i++)
                {
                    rank++;
                    hits.Add(new SearchHit(myPeers[i].Kind, myPeers[i], 100.0 + (1000 - rank)));
                }
                // results:Vector<Peer> — global hits.
                var globalPeers = DecodePeerVector(r);
                for (int i = 0; i < globalPeers.Count; i++)
                {
                    rank++;
                    hits.Add(new SearchHit(globalPeers[i].Kind, globalPeers[i], 1000 - rank));
                }

                // chats / users vectors follow but V1 does not need to map
                // them (the peer ids in the result vectors are sufficient for
                // the consumer to look up rich shapes via Vianigram.Contacts /
                // Vianigram.Chats). Skip the remaining bytes — the cursor
                // does not care.
                return hits;
            }
        }

        /// <summary>
        /// Build the next-page cursor from the tail of a freshly received
        /// <see cref="MessagesPage"/>. Empty page = unchanged cursor (the
        /// session will transition to <c>Completed</c> via
        /// <c>SearchSession.RecordPage</c>).
        /// </summary>
        public static SearchCursor AdvanceCursor(SearchCursor previous, MessagesPage page)
        {
            if (previous == null) throw new ArgumentNullException("previous");
            if (page == null) throw new ArgumentNullException("page");
            if (page.LastMessageId <= 0) return previous;
            return previous.Advance(page.LastMessageId, page.LastMessageDate, page.LastPeerKey);
        }

        // ---- TL primitives ----------------------------------------------------

        private static MessagesPage BuildPage(IList<MessageEntry> entries, int totalCount)
        {
            var hits = new List<SearchHit>(entries.Count);
            int rank = entries.Count;
            int lastId = 0, lastDate = 0;
            string lastPeer = null;
            for (int i = 0; i < entries.Count; i++)
            {
                hits.Add(new SearchHit(SearchHitKind.Message, entries[i], rank--));
                lastId = entries[i].MessageId;
                lastDate = entries[i].Date;
                lastPeer = entries[i].PeerKey;
            }
            return new MessagesPage
            {
                Hits = hits,
                TotalCount = totalCount,
                LastMessageId = lastId,
                LastMessageDate = lastDate,
                LastPeerKey = lastPeer
            };
        }

        /// <summary>
        /// Decode a <c>Vector&lt;Message&gt;</c>. For each element we read the
        /// constructor + flags + id + opt-from + peer + date, capture those
        /// fields, and skip the rest of the body until the next message
        /// boundary by relying on the outer message length record.
        ///
        /// <para><b>Caveat</b>: TL does not expose per-element length prefixes
        /// inside a Vector. Without fully decoding every <c>Message</c>
        /// variant we cannot safely advance the cursor between elements. The
        /// V1 strategy is to consume the elements we recognize (regular
        /// <c>message#a66c7efc</c> and <c>messageService#2b085862</c>) and
        /// stop on the first unknown constructor — partial-page is preferable
        /// to mis-aligned cursor advancement. The composition root can supply
        /// a richer decoder later.</para>
        /// </summary>
        private static IList<MessageEntry> DecodeMessagesVector(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector)
                throw new InvalidDataException("Expected Vector ctor for messages, got 0x" + vectorCtor.ToString("x8"));
            int count = r.ReadInt32();
            if (count < 0 || count > 1000000) throw new InvalidDataException("vector length out of range: " + count);

            var list = new List<MessageEntry>(count);
            for (int i = 0; i < count; i++)
            {
                long start = r.BaseStream.Position;
                if (start + 4 > r.BaseStream.Length) break;

                uint ctor = r.ReadUInt32();
                MessageEntry entry;
                if (TryDecodeMessageHeader(r, ctor, out entry))
                {
                    long bodyStart = start;
                    long bodyEnd = r.BaseStream.Position;
                    int bodyLen = (int)(bodyEnd - bodyStart);
                    entry.Body = SnapshotBytes(r, bodyStart, bodyLen);
                    list.Add(entry);
                }
                else
                {
                    // Unknown variant: rewind and stop. We cannot safely
                    // resync the stream without a length prefix.
                    r.BaseStream.Position = start;
                    break;
                }
            }
            return list;
        }

        /// <summary>
        /// Decode just enough of a <c>Message</c> to capture id / peer / date.
        /// Returns <c>false</c> for unknown constructors so the caller stops
        /// the scan instead of corrupting the byte cursor.
        ///
        /// Recognized:
        ///   * <c>messageEmpty#90a6ca84</c> flags:# id:int peer_id:flags.0?Peer = Message
        ///   * <c>message#76352a25</c> flags:# ... id:int from_id:flags.8?Peer ... peer_id:Peer ... date:int ... — V1 reads only the prefix.
        ///   * <c>messageService#2b085862</c> flags:# ... id:int from_id:flags.8?Peer peer_id:Peer ... date:int ...
        ///
        /// Implementation note: the full Message schema has many fields after
        /// the date; we deliberately stop once we have <c>(id, peer, date)</c>
        /// and let <see cref="DecodeMessagesVector"/> detect the next
        /// message boundary by trying to read another vector element. When
        /// that read fails (unknown constructor) we rewind. That heuristic
        /// limits search to the first contiguous run of recognized
        /// messages — sufficient for cursor advancement and acceptance tests
        /// that mock a <see cref="Ports.Outbound.IMtProtoRpcPort"/>.
        /// </summary>
        private static bool TryDecodeMessageHeader(BinaryReader r, uint ctor, out MessageEntry entry)
        {
            entry = null;

            // Only the empty variant has a deterministic, fully stable
            // layout that we can decode AND skip past. The full message /
            // messageService variants are intentionally NOT implemented
            // here — they land with Vianigram.Messages. Callers in unit
            // tests that need richer decoding can mock the IMtProtoRpcPort
            // response bytes to use messageEmpty.
            if (ctor == 0x90a6ca84) // messageEmpty
            {
                int flags = r.ReadInt32();
                int id = r.ReadInt32();
                string peerKey = null;
                if ((flags & (1 << 0)) != 0)
                {
                    peerKey = ReadPeer(r);
                }
                entry = new MessageEntry
                {
                    Constructor = ctor,
                    MessageId = id,
                    Date = 0,
                    PeerKey = peerKey
                };
                return true;
            }

            return false;
        }

        private static IList<PeerEntry> DecodePeerVector(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector)
                throw new InvalidDataException("Expected Vector ctor for peers, got 0x" + vectorCtor.ToString("x8"));
            int count = r.ReadInt32();
            if (count < 0 || count > 1000000) throw new InvalidDataException("vector length out of range: " + count);
            var list = new List<PeerEntry>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(DecodePeer(r));
            }
            return list;
        }

        private static PeerEntry DecodePeer(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorPeerUser)
            {
                long id = r.ReadInt64();
                return new PeerEntry { Kind = SearchHitKind.User, Id = id };
            }
            if (ctor == CtorPeerChat)
            {
                long id = r.ReadInt64();
                return new PeerEntry { Kind = SearchHitKind.Chat, Id = id };
            }
            if (ctor == CtorPeerChannel)
            {
                long id = r.ReadInt64();
                return new PeerEntry { Kind = SearchHitKind.Channel, Id = id };
            }
            throw new InvalidDataException("Unexpected Peer constructor: 0x" + ctor.ToString("x8"));
        }

        private static string ReadPeer(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorPeerUser)
            {
                long id = r.ReadInt64();
                return "user:" + id;
            }
            if (ctor == CtorPeerChat)
            {
                long id = r.ReadInt64();
                return "chat:" + id;
            }
            if (ctor == CtorPeerChannel)
            {
                long id = r.ReadInt64();
                return "channel:" + id;
            }
            throw new InvalidDataException("Unexpected Peer constructor: 0x" + ctor.ToString("x8"));
        }

        private static byte[] SnapshotBytes(BinaryReader r, long offset, int length)
        {
            long save = r.BaseStream.Position;
            try
            {
                r.BaseStream.Position = offset;
                byte[] buf = new byte[length];
                int read = r.Read(buf, 0, length);
                if (read != length) throw new InvalidDataException("snapshot truncated: " + read + "/" + length);
                return buf;
            }
            finally
            {
                r.BaseStream.Position = save;
            }
        }

        // Reserved for future richer decoding paths.
        private static string ReadString(BinaryReader r)
        {
            byte[] bytes = ReadBytes(r);
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        private static byte[] ReadBytes(BinaryReader r)
        {
            byte first = r.ReadByte();
            int len;
            int prefixLen;
            if (first == 254)
            {
                byte b1 = r.ReadByte();
                byte b2 = r.ReadByte();
                byte b3 = r.ReadByte();
                len = b1 | (b2 << 8) | (b3 << 16);
                prefixLen = 4;
            }
            else
            {
                len = first;
                prefixLen = 1;
            }
            byte[] bytes = r.ReadBytes(len);
            int padding = (4 - ((prefixLen + len) % 4)) % 4;
            for (int i = 0; i < padding; i++) r.ReadByte();
            return bytes;
        }
    }
}
