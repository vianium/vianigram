// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Infrastructure
{
    /// <summary>
    /// Hand-written TL serialization for the small set of "updates.*" RPC
    /// requests that Sync issues. The full TL machinery lives in C++
    /// (Vianigram.Core.Tl) per principle P1, but the request bodies that Sync
    /// emits are tiny (5–7 ints + a couple of constructor ids) so it is cheaper
    /// to compose them inline than to round-trip through the WinMD bridge.
    ///
    /// Wire format (TL primitives we use):
    ///   - int32: 4 bytes little-endian.
    ///   - int64: 8 bytes little-endian.
    ///   - bytes: 1-byte length prefix (or 4-byte if &gt;=254), payload, padding to 4.
    ///   - bool: int32 0x997275b5 (true) / 0xbc799737 (false).
    ///   - boxed value: 4-byte little-endian constructor id followed by fields.
    ///   - vector&lt;T&gt;: 0x1cb5c415, int32 count, then count×T.
    ///
    /// Constructor ids reference Telegram MTProto layer 214.
    /// </summary>
    public static class TlEncoder
    {
        // ----- TL primitive constructor ids -----
        public const uint VectorId = 0x1cb5c415u;
        public const uint BoolTrue = 0x997275b5u;
        public const uint BoolFalse = 0xbc799737u;

        // ----- Filter / channel boxed values (stable across recent layers) -----
        public const uint InputChannelId = 0xf35aec28u;        // inputChannel#f35aec28
        public const uint InputChannelEmptyId = 0xee8c1e86u;   // inputChannelEmpty#ee8c1e86
        public const uint ChannelMessagesFilterEmptyId = 0x94d42ee7u; // channelMessagesFilterEmpty#94d42ee7

        // ----- updates.* RPC method constructor ids (layer 214) -----
        public const uint UpdatesGetStateId = 0xedd4882au;
        public const uint UpdatesGetDifferenceId = 0x19c2f763u;
        public const uint UpdatesGetChannelDifferenceId = 0x03173d78u;

        /// <summary>
        /// Encode updates.getState#edd4882a — no fields, just the constructor id.
        /// </summary>
        public static byte[] EncodeGetState()
        {
            byte[] buf = new byte[4];
            WriteUInt32(buf, 0, UpdatesGetStateId);
            return buf;
        }

        /// <summary>
        /// Encode updates.getDifference#19c2f763.
        /// Layout: ctor | flags(uint32) | pts(int32) | [pts_total_limit if flags&amp;1] | date(int32) | qts(int32).
        /// </summary>
        public static byte[] EncodeGetDifference(int pts, int date, int qts, int? ptsTotalLimit)
        {
            int size = 4 + 4 + 4 + 4 + 4; // ctor + flags + pts + date + qts
            uint flags = 0;
            if (ptsTotalLimit.HasValue)
            {
                size += 4;
                flags |= 1u; // bit 0 — pts_total_limit present
            }

            byte[] buf = new byte[size];
            int p = 0;
            WriteUInt32(buf, p, UpdatesGetDifferenceId); p += 4;
            WriteUInt32(buf, p, flags); p += 4;
            WriteInt32(buf, p, pts); p += 4;
            if (ptsTotalLimit.HasValue) { WriteInt32(buf, p, ptsTotalLimit.Value); p += 4; }
            WriteInt32(buf, p, date); p += 4;
            WriteInt32(buf, p, qts); p += 4;
            return buf;
        }

        /// <summary>
        /// Encode updates.getChannelDifference#03173d78 with channelMessagesFilterEmpty.
        /// Layout: ctor | flags(uint32) | inputChannel | filter | pts(int32) | limit(int32).
        /// flags bit 0 = force.
        /// </summary>
        public static byte[] EncodeGetChannelDifference(long channelId, long accessHash, int pts, int limit, bool force)
        {
            // Sizes:
            //   ctor (4) + flags (4) + inputChannel ctor (4) + channel_id (8) + access_hash (8)
            //   + filter ctor (4) + pts (4) + limit (4) = 40
            int size = 4 + 4 + 4 + 8 + 8 + 4 + 4 + 4;
            byte[] buf = new byte[size];
            int p = 0;

            uint flags = 0;
            if (force) flags |= 1u;

            WriteUInt32(buf, p, UpdatesGetChannelDifferenceId); p += 4;
            WriteUInt32(buf, p, flags); p += 4;

            // inputChannel#f35aec28 channel_id:int64 access_hash:int64
            WriteUInt32(buf, p, InputChannelId); p += 4;
            WriteInt64(buf, p, channelId); p += 8;
            WriteInt64(buf, p, accessHash); p += 8;

            // channelMessagesFilterEmpty#94d42ee7
            WriteUInt32(buf, p, ChannelMessagesFilterEmptyId); p += 4;

            WriteInt32(buf, p, pts); p += 4;
            WriteInt32(buf, p, limit); p += 4;
            return buf;
        }

        // ----- Writers used by the inline encoders above -----

        public static void WriteInt32(byte[] buf, int offset, int v)
        {
            buf[offset + 0] = (byte)(v & 0xFF);
            buf[offset + 1] = (byte)((v >> 8) & 0xFF);
            buf[offset + 2] = (byte)((v >> 16) & 0xFF);
            buf[offset + 3] = (byte)((v >> 24) & 0xFF);
        }

        public static void WriteUInt32(byte[] buf, int offset, uint v)
        {
            buf[offset + 0] = (byte)(v & 0xFFu);
            buf[offset + 1] = (byte)((v >> 8) & 0xFFu);
            buf[offset + 2] = (byte)((v >> 16) & 0xFFu);
            buf[offset + 3] = (byte)((v >> 24) & 0xFFu);
        }

        public static void WriteInt64(byte[] buf, int offset, long v)
        {
            ulong u = (ulong)v;
            buf[offset + 0] = (byte)(u & 0xFFu);
            buf[offset + 1] = (byte)((u >> 8) & 0xFFu);
            buf[offset + 2] = (byte)((u >> 16) & 0xFFu);
            buf[offset + 3] = (byte)((u >> 24) & 0xFFu);
            buf[offset + 4] = (byte)((u >> 32) & 0xFFu);
            buf[offset + 5] = (byte)((u >> 40) & 0xFFu);
            buf[offset + 6] = (byte)((u >> 48) & 0xFFu);
            buf[offset + 7] = (byte)((u >> 56) & 0xFFu);
        }
    }
}
