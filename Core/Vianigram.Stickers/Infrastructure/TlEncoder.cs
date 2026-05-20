// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the seven RPC shapes Stickers
    /// issues. Mirrors the per-context approach used in <c>Vianigram.Contacts</c>
    /// and <c>Vianigram.Chats</c> — Stickers only needs:
    ///
    ///   * messages.getAllStickers#b8a0a1a8
    ///   * messages.getStickerSet#c8a0ec74
    ///   * messages.installStickerSet#c78fe460
    ///   * messages.uninstallStickerSet#f96e55de
    ///   * messages.getRecentStickers#9da9403b
    ///   * messages.faveSticker#b9ffc55b
    ///   * messages.searchStickerSets#35705b8a
    ///
    /// Plus the helper TL constructors used inline:
    ///   - inputStickerSetID#9de7a269 (set reference by id+access_hash)
    ///   - inputStickerSetShortName#861cc8a0 (set reference by short_name)
    ///   - inputDocument#1abfb575 (sticker reference)
    ///   - boolTrue#997275b5 / boolFalse#bc799737
    ///
    /// All multi-byte integers are little-endian (TL convention). Strings use
    /// the standard TL byte-string framing (1- or 4-byte length prefix +
    /// padding to 4-byte alignment). Byte arrays use the same framing.
    /// </summary>
    internal static class TlEncoder
    {
        // ---- TL constructor ids ----------------------------------------------
        public const uint CtorGetAllStickers = 0xb8a0a1a8;
        public const uint CtorGetStickerSet = 0xc8a0ec74;
        public const uint CtorInstallStickerSet = 0xc78fe460;
        public const uint CtorUninstallStickerSet = 0xf96e55de;
        public const uint CtorGetRecentStickers = 0x9da9403b;
        public const uint CtorFaveSticker = 0xb9ffc55b;
        public const uint CtorSearchStickerSets = 0x35705b8a;

        public const uint CtorInputStickerSetID = 0x9de7a269;
        public const uint CtorInputStickerSetShortName = 0x861cc8a0;
        public const uint CtorInputDocument = 0x1abfb575;
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        // -------------------------------------------------------------------------
        // messages.getAllStickers#b8a0a1a8  hash:long
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetAllStickers(long hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetAllStickers);
                w.Write(hash);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.getStickerSet#c8a0ec74  stickerset:InputStickerSet hash:int
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetStickerSet(StickerSetId id, int hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetStickerSet);
                WriteInputStickerSetID(w, id);
                w.Write(hash);
                return ms.ToArray();
            }
        }

        public static byte[] EncodeGetStickerSetByShortName(string shortName, int hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetStickerSet);
                w.Write(CtorInputStickerSetShortName);
                WriteString(w, shortName ?? string.Empty);
                w.Write(hash);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.installStickerSet#c78fe460  stickerset:InputStickerSet archived:Bool
        // -------------------------------------------------------------------------
        public static byte[] EncodeInstallStickerSet(StickerSetId id, bool archived)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorInstallStickerSet);
                WriteInputStickerSetID(w, id);
                w.Write(archived ? CtorBoolTrue : CtorBoolFalse);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.uninstallStickerSet#f96e55de  stickerset:InputStickerSet
        // -------------------------------------------------------------------------
        public static byte[] EncodeUninstallStickerSet(StickerSetId id)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorUninstallStickerSet);
                WriteInputStickerSetID(w, id);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.getRecentStickers#9da9403b  flags:#  attached:flags.0?true  hash:long
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetRecentStickers(bool attached, long hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetRecentStickers);
                int flags = 0;
                if (attached) flags |= 1 << 0;
                w.Write(flags);
                w.Write(hash);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.faveSticker#b9ffc55b  id:InputDocument unfave:Bool
        // -------------------------------------------------------------------------
        public static byte[] EncodeFaveSticker(StickerId id, bool unfave)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorFaveSticker);
                WriteInputDocument(w, id);
                w.Write(unfave ? CtorBoolTrue : CtorBoolFalse);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.searchStickerSets#35705b8a  flags:#  exclude_featured:flags.0?true  q:string  hash:long
        // -------------------------------------------------------------------------
        public static byte[] EncodeSearchStickerSets(string query, bool excludeFeatured, long hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorSearchStickerSets);
                int flags = 0;
                if (excludeFeatured) flags |= 1 << 0;
                w.Write(flags);
                WriteString(w, query ?? string.Empty);
                w.Write(hash);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // helpers
        // -------------------------------------------------------------------------

        private static void WriteInputStickerSetID(BinaryWriter w, StickerSetId id)
        {
            w.Write(CtorInputStickerSetID);
            w.Write(id.Value);
            w.Write(id.AccessHash);
        }

        private static void WriteInputDocument(BinaryWriter w, StickerId id)
        {
            // inputDocument#1abfb575 id:long access_hash:long file_reference:bytes
            w.Write(CtorInputDocument);
            w.Write(id.Value);
            w.Write(id.AccessHash);
            // V1 callers pass empty file_reference; the handler does NOT need it
            // for fave / unfave operations (the server resolves by document id).
            WriteBytes(w, new byte[0]);
        }

        /// <summary>
        /// TL string encoding: 1 length byte + bytes + padding to 4-byte align
        /// (or, for length >= 254, 0xFE + 3 length bytes + bytes + padding).
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
