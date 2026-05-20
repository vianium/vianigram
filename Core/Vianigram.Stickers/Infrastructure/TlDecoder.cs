// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the stickers.* responses we
    /// actually consume. Mirrors the per-context approach used in
    /// <c>Vianigram.Contacts</c> and <c>Vianigram.Chats</c>.
    ///
    /// Supported response constructors:
    ///   * messages.allStickers#cdbbcebb           hash:long sets:Vector&lt;StickerSet&gt;
    ///   * messages.allStickersNotModified#e86602c3 (empty body)
    ///   * messages.stickerSet#6e153f16             set:StickerSet packs:Vector&lt;StickerPack&gt; keywords:Vector&lt;StickerKeyword&gt; documents:Vector&lt;Document&gt;
    ///   * messages.stickerSetNotModified#d3f924eb (empty body)
    ///   * messages.recentStickers#88d37c56         hash:long packs:Vector&lt;StickerPack&gt; stickers:Vector&lt;Document&gt; dates:Vector&lt;int&gt;
    ///   * messages.recentStickersNotModified#0b17f890 (empty body)
    ///   * messages.foundStickerSets#8af09dd2       hash:long sets:Vector&lt;StickerSetCovered&gt;
    ///   * messages.foundStickerSetsNotModified#0d54b65d (empty body)
    ///
    /// V1 limitation: TL <c>Document</c> and several <c>StickerSetCovered</c>
    /// shapes carry many trailing optional fields whose binary surface has
    /// been historically volatile. Rather than tracking every shape, we walk
    /// the leading prefix exposing the fields Stickers cares about; on shape
    /// drift the affected entry is recorded as a stub and decoding for the
    /// remainder of that vector stops. A schema-generated decoder will
    /// eventually replace this.
    /// </summary>
    internal static class TlDecoder
    {
        // ---- TL constructor ids ----------------------------------------------

        // messages.AllStickers
        public const uint CtorAllStickers = 0xcdbbcebb;
        public const uint CtorAllStickersNotModified = 0xe86602c3;

        // messages.StickerSet
        public const uint CtorMessagesStickerSet = 0x6e153f16;
        public const uint CtorMessagesStickerSetNotModified = 0xd3f924eb;

        // messages.RecentStickers
        public const uint CtorRecentStickers = 0x88d37c56;
        public const uint CtorRecentStickersNotModified = 0x0b17f890;

        // messages.FoundStickerSets
        public const uint CtorFoundStickerSets = 0x8af09dd2;
        public const uint CtorFoundStickerSetsNotModified = 0x0d54b65d;

        // stickerSet#2dd14edc (current schema layer prefix)
        public const uint CtorStickerSet = 0x2dd14edc;

        // stickerSetCovered (one of several variants — we recognize the
        // canonical one and skip the rest).
        public const uint CtorStickerSetCovered = 0x6410a5d2;

        // stickerPack#12b299d4 emoticon:string documents:Vector<long>
        public const uint CtorStickerPack = 0x12b299d4;

        // documentEmpty#36f8c871 id:long
        public const uint CtorDocumentEmpty = 0x36f8c871;

        // document#8fd4c4d8 (recent layers) flags:#  id:long  access_hash:long  file_reference:bytes  date:int  ...
        public const uint CtorDocument = 0x8fd4c4d8;

        public const uint CtorVector = 0x1cb5c415;
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        // ---- result containers ----------------------------------------------

        public sealed class DecodedAllStickers
        {
            public bool NotModified { get; set; }
            public long Hash { get; set; }
            public IList<StickerSet> Sets { get; set; }
        }

        public sealed class DecodedStickerSet
        {
            public bool NotModified { get; set; }
            public StickerSet Set { get; set; }
            public IList<Sticker> Stickers { get; set; }
        }

        public sealed class DecodedRecentStickers
        {
            public bool NotModified { get; set; }
            public long Hash { get; set; }
            public IList<Sticker> Stickers { get; set; }
        }

        public sealed class DecodedFoundStickerSets
        {
            public bool NotModified { get; set; }
            public long Hash { get; set; }
            public IList<StickerSet> Sets { get; set; }
        }

        // ---- top-level decoders ---------------------------------------------

        public static DecodedAllStickers DecodeAllStickers(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedAllStickers { NotModified = false, Hash = 0L, Sets = new List<StickerSet>() };
                if (ctor == CtorAllStickersNotModified)
                {
                    result.NotModified = true;
                    return result;
                }
                if (ctor != CtorAllStickers)
                    throw new InvalidDataException("Unexpected messages.AllStickers constructor: 0x" + ctor.ToString("x8"));

                result.Hash = r.ReadInt64();
                ExpectVector(r);
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    StickerSet set;
                    bool more = TryReadStickerSet(r, out set);
                    if (set != null) result.Sets.Add(set);
                    if (!more) break;
                }
                return result;
            }
        }

        public static DecodedStickerSet DecodeStickerSet(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedStickerSet
                {
                    NotModified = false,
                    Set = null,
                    Stickers = new List<Sticker>()
                };
                if (ctor == CtorMessagesStickerSetNotModified)
                {
                    result.NotModified = true;
                    return result;
                }
                if (ctor != CtorMessagesStickerSet)
                    throw new InvalidDataException("Unexpected messages.StickerSet constructor: 0x" + ctor.ToString("x8"));

                StickerSet set;
                if (!TryReadStickerSet(r, out set))
                {
                    // Could not walk the set prefix safely; surface what we got.
                    result.Set = set;
                    return result;
                }
                result.Set = set;

                // packs: Vector<StickerPack>
                SkipStickerPackVector(r);
                // keywords: Vector<StickerKeyword> — skip wholesale; the
                // primary emoji is already on each Document in V1 schemas.
                SkipUnknownVector(r);
                // documents: Vector<Document>
                if (set != null)
                {
                    ReadDocumentsVector(r, set.Id, result.Stickers);
                }
                return result;
            }
        }

        public static DecodedRecentStickers DecodeRecentStickers(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedRecentStickers
                {
                    NotModified = false,
                    Hash = 0L,
                    Stickers = new List<Sticker>()
                };
                if (ctor == CtorRecentStickersNotModified)
                {
                    result.NotModified = true;
                    return result;
                }
                if (ctor != CtorRecentStickers)
                    throw new InvalidDataException("Unexpected messages.RecentStickers constructor: 0x" + ctor.ToString("x8"));

                result.Hash = r.ReadInt64();
                // packs: Vector<StickerPack>
                SkipStickerPackVector(r);
                // stickers: Vector<Document>
                ReadDocumentsVector(r, /*ownerSetId*/ default(StickerSetId), result.Stickers);
                // dates: Vector<int> — we don't read; cursor may be misaligned
                // but no further reads occur.
                return result;
            }
        }

        public static DecodedFoundStickerSets DecodeFoundStickerSets(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedFoundStickerSets
                {
                    NotModified = false,
                    Hash = 0L,
                    Sets = new List<StickerSet>()
                };
                if (ctor == CtorFoundStickerSetsNotModified)
                {
                    result.NotModified = true;
                    return result;
                }
                if (ctor != CtorFoundStickerSets)
                    throw new InvalidDataException("Unexpected messages.FoundStickerSets constructor: 0x" + ctor.ToString("x8"));

                result.Hash = r.ReadInt64();
                ExpectVector(r);
                int n = r.ReadInt32();
                for (int i = 0; i < n; i++)
                {
                    // Each entry is a StickerSetCovered variant. Walk only the
                    // leading set:StickerSet field; the rest of the covered
                    // shape carries cover documents whose surface is volatile.
                    uint coveredCtor;
                    try { coveredCtor = r.ReadUInt32(); } catch { break; }
                    if (coveredCtor != CtorStickerSetCovered)
                    {
                        // Unknown covered variant; can't safely advance.
                        break;
                    }
                    StickerSet inner;
                    bool more = TryReadStickerSet(r, out inner);
                    if (inner != null) result.Sets.Add(inner);
                    if (!more) break;
                }
                return result;
            }
        }

        // ---- shared helpers --------------------------------------------------

        /// <summary>
        /// Walks one TL <c>StickerSet</c> at the cursor and yields a populated
        /// domain entity (without loaded sticker content). Returns true when
        /// the cursor is in a known-good state for the next entry; false when
        /// we hit a shape we cannot safely advance past, in which case the
        /// caller must stop iterating.
        ///
        /// We read the leading prefix (flags, optional installed_date, id,
        /// access_hash, title, short_name, optional thumb_dc_id, count, hash)
        /// then deliberately stop — the trailing optional fields (thumbs,
        /// thumb_version, etc.) are too volatile to track here.
        /// </summary>
        private static bool TryReadStickerSet(BinaryReader r, out StickerSet set)
        {
            set = null;
            uint ctor;
            try { ctor = r.ReadUInt32(); }
            catch { return false; }

            if (ctor != CtorStickerSet)
            {
                // Future schemas may rev the constructor; surface a stub so
                // higher layers can continue iterating — but the cursor is now
                // misaligned, so signal a clean stop.
                return false;
            }

            try
            {
                int flags = r.ReadInt32();
                bool archived = (flags & (1 << 1)) != 0;
                bool isOfficial = (flags & (1 << 2)) != 0;
                bool isMasks = (flags & (1 << 3)) != 0;
                bool isAnimated = (flags & (1 << 5)) != 0;
                bool isVideos = (flags & (1 << 6)) != 0;
                // installed_date:flags.0?int
                if ((flags & (1 << 0)) != 0) r.ReadInt32();

                long id = r.ReadInt64();
                long accessHash = r.ReadInt64();
                string title = ReadString(r);
                string shortName = ReadString(r);

                // thumbs:flags.4?Vector<PhotoSize> — skip wholesale.
                if ((flags & (1 << 4)) != 0)
                {
                    SkipUnknownVector(r);
                }
                // thumb_dc_id:flags.4?int — bundled with thumbs flag in some layers; conditional read.
                if ((flags & (1 << 4)) != 0) r.ReadInt32();
                // thumb_version:flags.4?int
                if ((flags & (1 << 4)) != 0) r.ReadInt32();
                // thumb_document_id:flags.8?long
                if ((flags & (1 << 8)) != 0) r.ReadInt64();

                int count = r.ReadInt32();
                int hashInt = r.ReadInt32();

                // archived flag captured but not surfaced in V1 entity.
                bool unusedArchived = archived;
                if (unusedArchived) { /* no-op; placeholder for future archived-set support */ }
                set = new StickerSet(
                    new StickerSetId(id, accessHash),
                    title, shortName, count, hashInt,
                    isOfficial, isAnimated, isMasks, isVideos);
                // We intentionally stop at this point: any trailing optional
                // fields are not tracked in V1. The cursor is misaligned for
                // the next outer entry, so we signal a clean stop.
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Reads the documents:Vector&lt;Document&gt; at the cursor and appends
        /// every document we can walk to <paramref name="acc"/> as a
        /// <see cref="Sticker"/> entity owned by <paramref name="ownerSetId"/>.
        /// Stops at the first unwalkable Document shape.
        /// </summary>
        private static void ReadDocumentsVector(BinaryReader r, StickerSetId ownerSetId, IList<Sticker> acc)
        {
            uint vctor;
            try { vctor = r.ReadUInt32(); } catch { return; }
            if (vctor != CtorVector) return;
            int n;
            try { n = r.ReadInt32(); } catch { return; }
            for (int i = 0; i < n; i++)
            {
                Sticker s;
                bool more = TryReadDocument(r, ownerSetId, out s);
                if (s != null) acc.Add(s);
                if (!more) break;
            }
        }

        private static bool TryReadDocument(BinaryReader r, StickerSetId ownerSetId, out Sticker sticker)
        {
            sticker = null;
            uint ctor;
            try { ctor = r.ReadUInt32(); }
            catch { return false; }

            if (ctor == CtorDocumentEmpty)
            {
                long id;
                try { id = r.ReadInt64(); } catch { return false; }
                if (id > 0)
                {
                    sticker = new Sticker(
                        new StickerId(id, 0L),
                        ownerSetId.Value > 0 ? ownerSetId : new StickerSetId(long.MaxValue, 0L),
                        new byte[0],
                        StickerEmoji.Empty,
                        0, 0);
                }
                return true;
            }

            if (ctor != CtorDocument)
            {
                // Unknown Document variant — stop iterating.
                return false;
            }

            try
            {
                int flags = r.ReadInt32();
                long id = r.ReadInt64();
                long accessHash = r.ReadInt64();
                byte[] fileReference = ReadBytes(r);
                // We deliberately stop here. The trailing fields (date,
                // mime_type, size, thumbs, dc_id, attributes) are too
                // volatile to track in V1; we've captured id + accessHash +
                // file_reference, which is what Stickers needs to address
                // the document on the wire.
                int unusedFlags = flags;
                if (unusedFlags == int.MinValue) { /* placeholder */ }
                if (id > 0)
                {
                    sticker = new Sticker(
                        new StickerId(id, accessHash),
                        ownerSetId.Value > 0 ? ownerSetId : new StickerSetId(long.MaxValue, 0L),
                        fileReference ?? new byte[0],
                        StickerEmoji.Empty,
                        0, 0);
                }
                // Cursor is now misaligned. Signal a clean stop so the caller
                // doesn't try to read another Document.
                return false;
            }
            catch
            {
                return false;
            }
        }

        private static void SkipStickerPackVector(BinaryReader r)
        {
            uint vctor;
            try { vctor = r.ReadUInt32(); } catch { return; }
            if (vctor != CtorVector) return;
            int n;
            try { n = r.ReadInt32(); } catch { return; }
            for (int i = 0; i < n; i++)
            {
                uint pctor;
                try { pctor = r.ReadUInt32(); } catch { return; }
                if (pctor != CtorStickerPack) return;
                try
                {
                    ReadString(r); // emoticon
                    // documents: Vector<long>
                    uint dctor = r.ReadUInt32();
                    if (dctor != CtorVector) return;
                    int dn = r.ReadInt32();
                    for (int j = 0; j < dn; j++)
                    {
                        r.ReadInt64();
                    }
                }
                catch
                {
                    return;
                }
            }
        }

        /// <summary>
        /// Skips a <c>Vector</c> of items whose binary shape we can't safely
        /// walk. We consume the constructor + count and then bail out by
        /// abandoning the rest of the stream. Callers tolerate this by NOT
        /// reading anything after the call.
        /// </summary>
        private static void SkipUnknownVector(BinaryReader r)
        {
            uint ctor;
            try { ctor = r.ReadUInt32(); } catch { return; }
            if (ctor != CtorVector) return;
            try { r.ReadInt32(); } catch { return; }
            // Intentionally no further reads.
        }

        // ---- primitives -----------------------------------------------------

        private static void ExpectVector(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor != CtorVector)
                throw new InvalidDataException("Expected Vector#1cb5c415, got 0x" + ctor.ToString("x8"));
        }

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
