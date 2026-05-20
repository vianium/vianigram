// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the Settings response shapes
    /// this context consumes. Mirrors the per-context approach used in
    /// <c>Vianigram.Notifications</c> and <c>Vianigram.Stickers</c>.
    ///
    /// Supported response constructors:
    ///
    ///   * langPackString#cad181f6 key:string value:string = LangPackString
    ///   * langPackStringPluralized#6c47ac9f flags:# key:string
    ///       zero_value:flags.0?string one_value:flags.1?string
    ///       two_value:flags.2?string few_value:flags.3?string
    ///       many_value:flags.4?string other_value:string = LangPackString
    ///   * langPackStringDeleted#2979eeb2 key:string = LangPackString
    ///
    ///   * langPackDifference#f385c1f6 lang_code:string from_version:int
    ///       version:int strings:Vector&lt;LangPackString&gt;
    ///       = LangPackDifference
    ///
    ///   * account.contentSettings#57e28221 flags:#
    ///       sensitive_enabled:flags.0?true sensitive_can_change:flags.1?true
    ///       = account.ContentSettings
    ///
    /// V1 limitation: pluralized strings collapse to <see cref="LangPackString.Other"/>
    /// (Telegram's "other" plural form is the canonical fallback). The
    /// per-language plural rules table will land with the future
    /// <c>Vianigram.I18n</c> context.
    /// </summary>
    internal static class TlDecoder
    {
        // ---- TL constructor ids ----------------------------------------------
        public const uint CtorLangPackString = 0xcad181f6;
        public const uint CtorLangPackStringPluralized = 0x6c47ac9f;
        public const uint CtorLangPackStringDeleted = 0x2979eeb2;
        public const uint CtorLangPackDifference = 0xf385c1f6;
        public const uint CtorAccountContentSettings = 0x57e28221;

        public const uint CtorVector = 0x1cb5c415;
        public const uint CtorBoolTrue = 0x997275b5;
        public const uint CtorBoolFalse = 0xbc799737;

        /// <summary>
        /// Decoded form of a single <c>LangPackString</c>. Carries the
        /// originating constructor in <see cref="Kind"/> so the caller can
        /// distinguish a plain pair from a pluralized one or a tombstone.
        /// </summary>
        public sealed class LangPackString
        {
            public string Key { get; set; }
            public string Other { get; set; }
            /// <summary><see cref="CtorLangPackString"/>, <see cref="CtorLangPackStringPluralized"/>, or <see cref="CtorLangPackStringDeleted"/>.</summary>
            public uint Kind { get; set; }
        }

        /// <summary>
        /// Decoded form of <c>langPackDifference</c>. The strings list mirrors
        /// the wire <c>Vector&lt;LangPackString&gt;</c>.
        /// </summary>
        public sealed class LangPackDifference
        {
            public string LangCode { get; set; }
            public int FromVersion { get; set; }
            public int Version { get; set; }
            public IList<LangPackString> Strings { get; set; }
        }

        /// <summary>Decoded form of <c>account.contentSettings</c>.</summary>
        public sealed class ContentSettings
        {
            public bool SensitiveEnabled { get; set; }
            public bool SensitiveCanChange { get; set; }
        }

        // ---- Public API -------------------------------------------------------

        /// <summary>
        /// Decode a top-level <c>langPackDifference</c> response. Throws
        /// <see cref="InvalidDataException"/> on shape drift; handlers wrap
        /// the failure as <see cref="Settings.Domain.SettingsError"/>.
        /// </summary>
        public static LangPackDifference DecodeLangPackDifference(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorLangPackDifference)
                    throw new InvalidDataException("Unexpected langPackDifference constructor: 0x" + ctor.ToString("x8"));

                var diff = new LangPackDifference
                {
                    LangCode = ReadString(r),
                    FromVersion = r.ReadInt32(),
                    Version = r.ReadInt32()
                };
                diff.Strings = DecodeStringsVector(r);
                return diff;
            }
        }

        /// <summary>
        /// Decode a top-level <c>Vector&lt;LangPackString&gt;</c> response —
        /// the shape returned by <c>langpack.getStrings</c>. The outer Vector
        /// constructor is read here.
        /// </summary>
        public static IList<LangPackString> DecodeStringsResponse(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                return DecodeStringsVector(r);
            }
        }

        /// <summary>Decode <c>account.contentSettings</c>.</summary>
        public static ContentSettings DecodeContentSettings(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorAccountContentSettings)
                    throw new InvalidDataException("Unexpected account.contentSettings constructor: 0x" + ctor.ToString("x8"));
                int flags = r.ReadInt32();
                return new ContentSettings
                {
                    SensitiveEnabled = (flags & (1 << 0)) != 0,
                    SensitiveCanChange = (flags & (1 << 1)) != 0
                };
            }
        }

        /// <summary>
        /// Convenience: derive a <see cref="LanguagePack"/> value object from
        /// a decoded <see cref="LangPackDifference"/>. The base lang code is
        /// not surfaced over the difference shape — the caller threads it
        /// through from the request side.
        /// </summary>
        public static LanguagePack ToLanguagePack(LangPackDifference diff, string baseLangCode)
        {
            if (diff == null) throw new ArgumentNullException("diff");
            string code = string.IsNullOrEmpty(diff.LangCode) ? "en" : diff.LangCode;
            int version = diff.Version >= 0 ? diff.Version : 0;
            return new LanguagePack(code, version, baseLangCode);
        }

        // ---- TL primitives ----------------------------------------------------

        private static IList<LangPackString> DecodeStringsVector(BinaryReader r)
        {
            uint vectorCtor = r.ReadUInt32();
            if (vectorCtor != CtorVector)
                throw new InvalidDataException("Expected Vector ctor, got 0x" + vectorCtor.ToString("x8"));
            int count = r.ReadInt32();
            if (count < 0 || count > 1000000) throw new InvalidDataException("vector length out of range: " + count);
            var list = new List<LangPackString>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(DecodeLangPackString(r));
            }
            return list;
        }

        private static LangPackString DecodeLangPackString(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorLangPackString)
            {
                string key = ReadString(r);
                string value = ReadString(r);
                return new LangPackString { Key = key, Other = value, Kind = ctor };
            }
            if (ctor == CtorLangPackStringPluralized)
            {
                int flags = r.ReadInt32();
                string key = ReadString(r);
                string zero = (flags & (1 << 0)) != 0 ? ReadString(r) : null;
                string one = (flags & (1 << 1)) != 0 ? ReadString(r) : null;
                string two = (flags & (1 << 2)) != 0 ? ReadString(r) : null;
                string few = (flags & (1 << 3)) != 0 ? ReadString(r) : null;
                string many = (flags & (1 << 4)) != 0 ? ReadString(r) : null;
                string other = ReadString(r);
                // Reference the optional plural forms so the compiler doesn't
                // warn about unused locals — V1 collapses to "other" but the
                // bytes must be consumed for the cursor to advance correctly.
                if (zero == null && one == null && two == null && few == null && many == null) { /* no-op */ }
                return new LangPackString { Key = key, Other = other, Kind = ctor };
            }
            if (ctor == CtorLangPackStringDeleted)
            {
                string key = ReadString(r);
                return new LangPackString { Key = key, Other = null, Kind = ctor };
            }
            throw new InvalidDataException("Unexpected LangPackString constructor: 0x" + ctor.ToString("x8"));
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
