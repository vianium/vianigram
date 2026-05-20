// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;

namespace Vianigram.Composition.Infrastructure
{
    internal sealed class TelegramDhConfig
    {
        public TelegramDhConfig(bool notModified, int g, byte[] p, int version, byte[] random)
        {
            NotModified = notModified;
            G = g;
            P = p ?? new byte[0];
            Version = version;
            Random = random ?? new byte[0];
        }

        public bool NotModified { get; private set; }
        public int G { get; private set; }
        public byte[] P { get; private set; }
        public int Version { get; private set; }
        public byte[] Random { get; private set; }
    }

    internal static class TelegramDhConfigCodec
    {
        private const uint CtorGetDhConfig = 0x26cf8950;
        private const uint CtorDhConfigNotModified = 0xc0e24635;
        private const uint CtorDhConfig = 0x2c221edd;

        public static byte[] EncodeGetDhConfig(int version, int randomLength)
        {
            return new TlByteBuilder()
                .WriteUInt32(CtorGetDhConfig)
                .WriteInt32(version)
                .WriteInt32(randomLength)
                .ToArray();
        }

        public static TelegramDhConfig DecodeDhConfig(byte[] payload)
        {
            if (payload == null || payload.Length < 4)
                throw new InvalidDataException("empty messages.DhConfig payload");

            var r = new TlByteReader(payload);
            uint ctor = r.ReadUInt32();
            if (ctor == CtorDhConfigNotModified)
            {
                return new TelegramDhConfig(true, 0, new byte[0], 0, r.ReadBytes());
            }

            if (ctor == CtorDhConfig)
            {
                int g = r.ReadInt32();
                byte[] p = r.ReadBytes();
                int version = r.ReadInt32();
                byte[] random = r.ReadBytes();
                return new TelegramDhConfig(false, g, p, version, random);
            }

            throw new InvalidDataException(
                "unexpected messages.DhConfig constructor 0x" + ctor.ToString("x8"));
        }
    }
}
