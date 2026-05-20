// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GzipResponseDecoder.cs — Vianigram.Composition.Infrastructure
//
// Telegram routinely wraps large RPC responses (typical hit:
// messages.getDialogs, users.getFullUser, channels.getDifference) in
// gzip_packed#3072cfa1 packed_data:bytes = Object. The native MTProto
// channel forwards the gzip_packed envelope to managed unchanged because
// WP 8.1's native SDK doesn't ship zlib; managed code, on the other
// hand, has System.IO.Compression.GZipStream.
//
// All RPC response bytes flowing through MtProtoChannelAdapter and
// AccountLoginMtProtoRpcPort pass through MaybeInflate first. Pass-
// through is cheap (one ctor peek) so it's safe to apply unconditionally.

using System;
using System.IO;
using System.IO.Compression;

namespace Vianigram.Composition.Infrastructure
{
    internal static class GzipResponseDecoder
    {
        // gzip_packed#3072cfa1 packed_data:bytes = Object;
        private const uint GzipPackedCtor = 0x3072cfa1u;

        /// <summary>
        /// If <paramref name="body"/> is a TL gzip_packed envelope, peel
        /// the bytes-typed payload, inflate it, and return the
        /// uncompressed inner object (which the caller decodes normally).
        /// Otherwise returns <paramref name="body"/> unchanged.
        /// </summary>
        public static byte[] MaybeInflate(byte[] body)
        {
            if (body == null || body.Length < 4) return body;
            uint ctor = (uint)(body[0]
                | (body[1] << 8)
                | (body[2] << 16)
                | (body[3] << 24));
            if (ctor != GzipPackedCtor) return body;

            try
            {
                int offset = 4;
                int length;
                if (offset >= body.Length) return body;

                byte first = body[offset];
                if (first == 0xFE)
                {
                    if (offset + 4 > body.Length) return body;
                    length = body[offset + 1]
                        | (body[offset + 2] << 8)
                        | (body[offset + 3] << 16);
                    offset += 4;
                }
                else
                {
                    length = first;
                    offset += 1;
                }

                if (length <= 0 || offset + length > body.Length) return body;

                using (var compressed = new MemoryStream(body, offset, length, false))
                using (var gz = new GZipStream(compressed, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[8192];
                    int read;
                    while ((read = gz.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                    }
                    return output.ToArray();
                }
            }
            catch
            {
                // Any decoding failure → fall back to the original bytes.
                // The caller's TL decoder will surface a more meaningful
                // "unexpected ctor" error than masking it here.
                return body;
            }
        }
    }
}
