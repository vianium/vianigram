// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// TlByteBuilder.cs
// Fluent TL serializer used by the typed RPC stubs in
// MtProtoChannelAdapter.TypedStubs.cs. Wraps a MemoryStream and offers a
// minimal set of TL primitives matching the layer-214 wire format:
//
//   * little-endian fixed-width integers (int32 / int64 / uint32 / uint64)
//   * IEEE-754 double
//   * TL bytes / string with the standard 1-byte vs 0xFE+3-byte length prefix
//     and zero-padded 4-byte alignment
//   * BoolTrue (0x997275b5) / BoolFalse (0xbc799737)
//   * Vector<T> (0x1cb5c415 + count + per-item writer)
//
// The builder is single-use; ToArray() snapshots the current buffer.
//
// All public surface is C#-6 friendly (no nameof, no $""), so this compiles
// with the VS2013 toolchain target the rest of the managed tree uses.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Minimal fluent TL byte builder. Each Write* method returns
    /// <c>this</c> so callers can chain a whole TL function body in one go.
    /// </summary>
    public sealed class TlByteBuilder
    {
        public const uint BoolTrueCtor = 0x997275b5;
        public const uint BoolFalseCtor = 0xbc799737;
        public const uint VectorCtor = 0x1cb5c415;

        private readonly MemoryStream _ms;

        public TlByteBuilder()
        {
            _ms = new MemoryStream();
        }

        public TlByteBuilder WriteUInt32(uint v)
        {
            _ms.WriteByte((byte)(v & 0xff));
            _ms.WriteByte((byte)((v >> 8) & 0xff));
            _ms.WriteByte((byte)((v >> 16) & 0xff));
            _ms.WriteByte((byte)((v >> 24) & 0xff));
            return this;
        }

        public TlByteBuilder WriteInt32(int v)
        {
            return WriteUInt32(unchecked((uint)v));
        }

        public TlByteBuilder WriteInt64(long v)
        {
            ulong uv = unchecked((ulong)v);
            return WriteUInt64(uv);
        }

        public TlByteBuilder WriteUInt64(ulong v)
        {
            for (int i = 0; i < 8; i++)
            {
                _ms.WriteByte((byte)((v >> (8 * i)) & 0xff));
            }
            return this;
        }

        public TlByteBuilder WriteDouble(double v)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            // TL doubles are little-endian; BitConverter is LE on all our targets,
            // but stay safe and reverse if a future host turns out to be BE.
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes);
            }
            _ms.Write(bytes, 0, 8);
            return this;
        }

        /// <summary>
        /// TL string: same wire encoding as TL bytes — UTF-8 body with a
        /// 1-byte length prefix when length &lt; 254, else 0xFE + 3-byte LE
        /// length, then the body, then zero-padding to a 4-byte boundary.
        /// </summary>
        public TlByteBuilder WriteString(string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s)
                ? new byte[0]
                : Encoding.UTF8.GetBytes(s);
            return WriteBytes(bytes);
        }

        public TlByteBuilder WriteBytes(byte[] b)
        {
            byte[] body = b ?? new byte[0];
            int len = body.Length;
            int total;

            if (len <= 253)
            {
                _ms.WriteByte((byte)len);
                total = 1 + len;
            }
            else
            {
                _ms.WriteByte(254);
                _ms.WriteByte((byte)(len & 0xff));
                _ms.WriteByte((byte)((len >> 8) & 0xff));
                _ms.WriteByte((byte)((len >> 16) & 0xff));
                total = 4 + len;
            }

            if (len > 0) _ms.Write(body, 0, len);

            int rem = total % 4;
            if (rem != 0)
            {
                int pad = 4 - rem;
                for (int i = 0; i < pad; i++) _ms.WriteByte(0);
            }
            return this;
        }

        public TlByteBuilder WriteBool(bool v)
        {
            return WriteUInt32(v ? BoolTrueCtor : BoolFalseCtor);
        }

        /// <summary>
        /// TL Vector&lt;T&gt; — magic 0x1cb5c415, then int32 count, then each
        /// item written via the supplied callback.
        /// </summary>
        public TlByteBuilder WriteVector<T>(IList<T> items, Action<TlByteBuilder, T> writeItem)
        {
            if (writeItem == null) throw new ArgumentNullException("writeItem");
            WriteUInt32(VectorCtor);
            int count = items == null ? 0 : items.Count;
            WriteInt32(count);
            for (int i = 0; i < count; i++)
            {
                writeItem(this, items[i]);
            }
            return this;
        }

        /// <summary>
        /// Append a pre-encoded TL fragment verbatim. Used when a sub-builder
        /// produces a complete TL object (e.g. inputMediaPoll) that is then
        /// embedded in a larger request.
        /// </summary>
        public TlByteBuilder WriteRaw(byte[] raw)
        {
            if (raw == null || raw.Length == 0) return this;
            _ms.Write(raw, 0, raw.Length);
            return this;
        }

        public byte[] ToArray()
        {
            return _ms.ToArray();
        }
    }
}
