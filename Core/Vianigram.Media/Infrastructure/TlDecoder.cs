// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;

namespace Vianigram.Media.Infrastructure
{
    /// <summary>
    /// TL deserializers for the response shapes the Media context expects.
    /// MVP-grade — enough to pull the byte payload out of <c>upload.file</c>
    /// and to detect a CDN redirect so the orchestrator can route around it.
    /// The native production decoder lives in <c>Vianigram.Core.Tl</c>.
    ///
    /// <para>Constructor IDs (TL layer 214):</para>
    /// <list type="bullet">
    ///   <item><description><c>upload.file               0x096a18d5</c> —
    ///         (type:storage.FileType, mtime:int, bytes:bytes)</description></item>
    ///   <item><description><c>upload.fileCdnRedirect    0xf18cda44</c> —
    ///         (dc_id, file_token, encryption_key, encryption_iv, file_hashes)</description></item>
    ///   <item><description><c>boolTrue                  0x997275b5</c></description></item>
    ///   <item><description><c>boolFalse                 0xbc799737</c></description></item>
    /// </list>
    /// </summary>
    internal static class TlDecoder
    {
        public const uint CtorUploadFile = 0x096a18d5u;
        public const uint CtorUploadFileCdnRedirect = 0xf18cda44u;

        public const uint CtorBoolTrue = 0x997275b5u;
        public const uint CtorBoolFalse = 0xbc799737u;

        /// <summary>
        /// Decode an <c>upload.file</c> response and extract the raw chunk
        /// bytes. Returns <c>false</c> if the payload is a CDN redirect or
        /// otherwise unrecognised; callers should treat that as a fall-back
        /// path.
        /// </summary>
        public static bool TryDecodeUploadFile(byte[] payload, out byte[] bytes)
        {
            bytes = null;
            if (payload == null || payload.Length < 4) return false;

            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    uint ctor = r.ReadUInt32();
                    if (ctor != CtorUploadFile) return false;

                    // type : storage.FileType (boxed) — we read the 4-byte ctor
                    // and ignore it; production decoder maps to a typed enum.
                    r.ReadUInt32();
                    r.ReadInt32(); // mtime

                    bytes = ReadBytes(r);
                    return true;
                }
            }
            catch
            {
                bytes = null;
                return false;
            }
        }

        /// <summary>
        /// Decode the boxed <c>Bool</c> response of
        /// <c>upload.saveFilePart</c> / <c>upload.saveBigFilePart</c>.
        /// Returns <c>false</c> on unrecognised payloads.
        /// </summary>
        public static bool TryDecodeBool(byte[] payload, out bool value)
        {
            value = false;
            if (payload == null || payload.Length < 4) return false;
            try
            {
                using (var ms = new MemoryStream(payload, false))
                using (var r = new BinaryReader(ms, Encoding.UTF8))
                {
                    uint ctor = r.ReadUInt32();
                    if (ctor == CtorBoolTrue) { value = true; return true; }
                    if (ctor == CtorBoolFalse) { value = false; return true; }
                    return false;
                }
            }
            catch
            {
                return false;
            }
        }

        // ---------- Helpers ----------

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

            var bytes = r.ReadBytes(len);
            int padding = (4 - (consumed % 4)) % 4;
            for (int i = 0; i < padding; i++) r.ReadByte();
            return bytes;
        }
    }
}
