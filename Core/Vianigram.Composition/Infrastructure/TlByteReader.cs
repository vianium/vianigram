// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// TlByteReader.cs
// Minimal TL deserializer used by the typed RPC stubs in
// MtProtoChannelAdapter.TypedStubs.cs. Mirrors TlByteBuilder one-for-one:
// little-endian primitives, TL bytes/string with 1-byte vs 0xFE+3-byte length
// prefix and 4-byte alignment, BoolTrue / BoolFalse, and Vector<T> (0x1cb5c415).
//
// The reader is permissive: it does not fail on extra trailing bytes (the
// adapter only consumes what each per-method decoder needs), but it WILL
// throw EndOfStreamException on premature truncation and a plain Exception
// on a constructor mismatch (via ExpectConstructor) so unexpected wire
// payloads surface as a typed error rather than silent corruption.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class TlByteReader
    {
        public const uint BoolTrueCtor = 0x997275b5;
        public const uint BoolFalseCtor = 0xbc799737;
        public const uint VectorCtor = 0x1cb5c415;

        private readonly byte[] _buf;
        private int _pos;

        public TlByteReader(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException("buffer");
            _buf = buffer;
            _pos = 0;
        }

        public int Position { get { return _pos; } }
        public int Length { get { return _buf.Length; } }
        public bool EndOfStream { get { return _pos >= _buf.Length; } }

        public uint ReadUInt32()
        {
            EnsureAvailable(4);
            uint v = (uint)(_buf[_pos]
                | (_buf[_pos + 1] << 8)
                | (_buf[_pos + 2] << 16)
                | (_buf[_pos + 3] << 24));
            _pos += 4;
            return v;
        }

        public int ReadInt32()
        {
            return unchecked((int)ReadUInt32());
        }

        public long ReadInt64()
        {
            return unchecked((long)ReadUInt64());
        }

        public ulong ReadUInt64()
        {
            EnsureAvailable(8);
            ulong v = 0;
            for (int i = 0; i < 8; i++)
            {
                v |= ((ulong)_buf[_pos + i]) << (8 * i);
            }
            _pos += 8;
            return v;
        }

        public double ReadDouble()
        {
            EnsureAvailable(8);
            byte[] tmp = new byte[8];
            Buffer.BlockCopy(_buf, _pos, tmp, 0, 8);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(tmp);
            }
            _pos += 8;
            return BitConverter.ToDouble(tmp, 0);
        }

        public byte[] ReadBytes()
        {
            EnsureAvailable(1);
            int first = _buf[_pos++];
            int len;
            int prefix;

            if (first == 254)
            {
                EnsureAvailable(3);
                int b1 = _buf[_pos++];
                int b2 = _buf[_pos++];
                int b3 = _buf[_pos++];
                len = b1 | (b2 << 8) | (b3 << 16);
                prefix = 4;
            }
            else
            {
                len = first;
                prefix = 1;
            }

            EnsureAvailable(len);
            byte[] body = new byte[len];
            Buffer.BlockCopy(_buf, _pos, body, 0, len);
            _pos += len;

            int total = prefix + len;
            int rem = total % 4;
            if (rem != 0)
            {
                int pad = 4 - rem;
                EnsureAvailable(pad);
                _pos += pad;
            }
            return body;
        }

        public string ReadString()
        {
            byte[] body = ReadBytes();
            return Encoding.UTF8.GetString(body, 0, body.Length);
        }

        public bool ReadBool()
        {
            uint ctor = ReadUInt32();
            if (ctor == BoolTrueCtor) return true;
            if (ctor == BoolFalseCtor) return false;
            throw new InvalidDataException("Expected BoolTrue/BoolFalse, got 0x" + ctor.ToString("x8"));
        }

        public IList<T> ReadVector<T>(Func<TlByteReader, T> readItem)
        {
            if (readItem == null) throw new ArgumentNullException("readItem");
            uint ctor = ReadUInt32();
            if (ctor != VectorCtor)
            {
                throw new InvalidDataException("Expected Vector ctor 0x1cb5c415, got 0x" + ctor.ToString("x8"));
            }
            int count = ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException("Negative Vector count " + count);
            }
            var list = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                list.Add(readItem(this));
            }
            return list;
        }

        /// <summary>
        /// Read the next 4 bytes and assert they match <paramref name="id"/>.
        /// Throws on mismatch. Used at decode entry-points where we want a
        /// fail-fast on an unexpected response constructor.
        /// </summary>
        public void ExpectConstructor(uint id)
        {
            uint actual = ReadUInt32();
            if (actual != id)
            {
                throw new InvalidDataException(
                    "Expected ctor 0x" + id.ToString("x8") + ", got 0x" + actual.ToString("x8"));
            }
        }

        public uint PeekUInt32()
        {
            EnsureAvailable(4);
            return (uint)(_buf[_pos]
                | (_buf[_pos + 1] << 8)
                | (_buf[_pos + 2] << 16)
                | (_buf[_pos + 3] << 24));
        }

        public void Skip(int bytes)
        {
            if (bytes < 0) throw new ArgumentOutOfRangeException("bytes");
            EnsureAvailable(bytes);
            _pos += bytes;
        }

        private void EnsureAvailable(int n)
        {
            if (_pos + n > _buf.Length)
            {
                throw new EndOfStreamException(
                    "TlByteReader: need " + n + " bytes at " + _pos + " (have " + (_buf.Length - _pos) + ")");
            }
        }
    }
}
