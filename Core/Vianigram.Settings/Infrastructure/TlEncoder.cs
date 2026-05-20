// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the Settings RPC shapes this
    /// context issues. Mirrors the per-context approach used in
    /// <c>Vianigram.Notifications</c> and <c>Vianigram.Stickers</c>.
    ///
    /// Supported requests:
    ///
    ///   * langpack.getLangPack#f2f2330a
    ///       lang_pack:string lang_code:string = LangPackDifference
    ///
    ///   * langpack.getStrings#efea3803
    ///       lang_pack:string lang_code:string keys:Vector&lt;string&gt;
    ///       = Vector&lt;LangPackString&gt;
    ///
    ///   * langpack.getDifference#cd984aa5
    ///       lang_pack:string lang_code:string from_version:int
    ///       = LangPackDifference
    ///
    ///   * account.getContentSettings#8b9b4dae = account.ContentSettings
    ///
    /// All multi-byte integers are little-endian (TL convention). Vector uses
    /// the boxed constructor 0x1cb5c415 followed by an int32 length and the
    /// element payloads.
    ///
    /// NOTE: the assignment prompt lists <c>langpack.getLangPack#cd984aa5</c>
    /// (which is actually <c>getDifference</c>'s CRC). The wire IDs honored
    /// here are the canonical ones from
    /// <c>td/generate/scheme/telegram_api.tl</c> so the bytes the host puts on
    /// the wire match what the server expects.
    /// </summary>
    internal static class TlEncoder
    {
        // ---- TL constructor ids ----------------------------------------------
        public const uint CtorLangpackGetLangPack = 0xf2f2330a;
        public const uint CtorLangpackGetStrings = 0xefea3803;
        public const uint CtorLangpackGetDifference = 0xcd984aa5;
        public const uint CtorAccountGetContentSettings = 0x8b9b4dae;

        public const uint CtorVector = 0x1cb5c415;

        // ---- Public API -------------------------------------------------------

        /// <summary>
        /// langpack.getLangPack#f2f2330a lang_pack:string lang_code:string = LangPackDifference
        /// </summary>
        public static byte[] EncodeGetLangPack(string langPack, string langCode)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorLangpackGetLangPack);
                WriteString(w, langPack);
                WriteString(w, langCode);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// langpack.getStrings#efea3803 lang_pack:string lang_code:string keys:Vector&lt;string&gt; = Vector&lt;LangPackString&gt;
        /// </summary>
        public static byte[] EncodeGetStrings(string langPack, string langCode, IList<string> keys)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorLangpackGetStrings);
                WriteString(w, langPack);
                WriteString(w, langCode);

                w.Write(CtorVector);
                int count = (keys == null) ? 0 : keys.Count;
                w.Write(count);
                for (int i = 0; i < count; i++) WriteString(w, keys[i]);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// langpack.getDifference#cd984aa5 lang_pack:string lang_code:string from_version:int = LangPackDifference
        /// </summary>
        public static byte[] EncodeGetDifference(string langPack, string langCode, int fromVersion)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorLangpackGetDifference);
                WriteString(w, langPack);
                WriteString(w, langCode);
                w.Write(fromVersion);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// account.getContentSettings#8b9b4dae = account.ContentSettings
        /// </summary>
        public static byte[] EncodeGetContentSettings()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAccountGetContentSettings);
                return ms.ToArray();
            }
        }

        // ---- TL primitives ----------------------------------------------------

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
