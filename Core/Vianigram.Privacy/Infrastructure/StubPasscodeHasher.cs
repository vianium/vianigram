// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Text;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Infrastructure
{
    /// <summary>
    /// Deterministic stub <see cref="IPasscodeHasher"/> that derives the hash
    /// from <c>SHA256(pin || salt)</c> repeated 1024 rounds. Sufficient for
    /// unit tests and the build-verification gate; NOT a production-strength
    /// KDF.
    ///
    /// <para><b>Production replacement</b>: <c>Pbkdf2HmacSha512Hasher</c>
    /// wraps the same primitive that <c>Vianium.Crypto.SrpClient</c> already
    /// uses for SRP password derivation (PBKDF2-HMAC-SHA512 from the Crypto
    /// WinMD). The composition root rebinds <see cref="IPasscodeHasher"/> to
    /// that implementation; every other layer is unchanged.</para>
    ///
    /// <para><b>Salt format</b>: 16 bytes from a managed
    /// <see cref="System.Random"/>. The production adapter MUST switch to a
    /// CSPRNG (the <c>Vianium.Crypto</c> WinMD exposes one) — this stub is
    /// intentionally weaker so a "production with stub" deploy is obvious in
    /// telemetry: <see cref="Kind"/> reports "stub".</para>
    /// </summary>
    public sealed class StubPasscodeHasher : IPasscodeHasher
    {
        private const int Rounds = 1024;
        private const int Sha256Len = 32;
        private const int DefaultSaltLength = 16;

        private readonly Random _rng;
        private readonly int _saltLength;

        public StubPasscodeHasher() : this(DefaultSaltLength, new Random())
        {
        }

        public StubPasscodeHasher(int saltLength, Random rng)
        {
            if (saltLength <= 0) throw new ArgumentOutOfRangeException("saltLength");
            if (rng == null) throw new ArgumentNullException("rng");
            _saltLength = saltLength;
            _rng = rng;
        }

        public string Kind { get { return PasscodeHasherKinds.Stub; } }
        public int SaltLength { get { return _saltLength; } }

        public byte[] GenerateSalt()
        {
            byte[] salt = new byte[_saltLength];
            // System.Random is NOT cryptographically strong; the production
            // adapter swaps in CryptographicBuffer.GenerateRandom.
            _rng.NextBytes(salt);
            return salt;
        }

        public byte[] ComputeHash(string pin, byte[] salt)
        {
            if (pin == null) pin = string.Empty;
            byte[] pinBytes = Encoding.UTF8.GetBytes(pin);
            byte[] effectiveSalt = salt == null ? new byte[0] : salt;

            // Seed = SHA256(pin || salt) using a hand-rolled SHA-256 to avoid
            // pulling System.Security.Cryptography into a phone build
            // (which has limited managed crypto). We compose
            // 1024 rounds for a tiny work factor — plenty for tests, NOT
            // production.
            byte[] state = Sha256Light.Hash(Concat(pinBytes, effectiveSalt));
            for (int i = 1; i < Rounds; i++)
            {
                state = Sha256Light.Hash(Concat(state, effectiveSalt));
            }
            return state;
        }

        private static byte[] Concat(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        // ---- Hand-rolled SHA-256 (light) -----------------------------------
        // FIPS 180-4. Used only in the stub so we don't take a dependency on
        // System.Security.Cryptography for tests. The production adapter
        // calls into the Vianium.Crypto WinMD instead.
        private static class Sha256Light
        {
            private static readonly uint[] K = new uint[]
            {
                0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
                0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
                0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
                0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
                0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
                0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
                0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
                0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
            };

            public static byte[] Hash(byte[] message)
            {
                // Pad: 1 bit, 0s, 64-bit length.
                long bitLen = (long)message.Length * 8;
                int padLen = (56 - (message.Length + 1) % 64 + 64) % 64;
                byte[] padded = new byte[message.Length + 1 + padLen + 8];
                Buffer.BlockCopy(message, 0, padded, 0, message.Length);
                padded[message.Length] = 0x80;
                for (int i = 0; i < 8; i++)
                {
                    padded[padded.Length - 1 - i] = (byte)(bitLen >> (8 * i));
                }

                uint h0 = 0x6a09e667, h1 = 0xbb67ae85, h2 = 0x3c6ef372, h3 = 0xa54ff53a;
                uint h4 = 0x510e527f, h5 = 0x9b05688c, h6 = 0x1f83d9ab, h7 = 0x5be0cd19;

                uint[] w = new uint[64];
                for (int chunk = 0; chunk < padded.Length; chunk += 64)
                {
                    for (int i = 0; i < 16; i++)
                    {
                        int o = chunk + i * 4;
                        w[i] = ((uint)padded[o] << 24)
                             | ((uint)padded[o + 1] << 16)
                             | ((uint)padded[o + 2] << 8)
                             | ((uint)padded[o + 3]);
                    }
                    for (int i = 16; i < 64; i++)
                    {
                        uint s0 = Ror(w[i - 15], 7) ^ Ror(w[i - 15], 18) ^ (w[i - 15] >> 3);
                        uint s1 = Ror(w[i - 2], 17) ^ Ror(w[i - 2], 19) ^ (w[i - 2] >> 10);
                        w[i] = w[i - 16] + s0 + w[i - 7] + s1;
                    }

                    uint a = h0, b = h1, c = h2, d = h3, e = h4, f = h5, g = h6, hv = h7;
                    for (int i = 0; i < 64; i++)
                    {
                        uint S1 = Ror(e, 6) ^ Ror(e, 11) ^ Ror(e, 25);
                        uint ch = (e & f) ^ (~e & g);
                        uint t1 = hv + S1 + ch + K[i] + w[i];
                        uint S0 = Ror(a, 2) ^ Ror(a, 13) ^ Ror(a, 22);
                        uint mj = (a & b) ^ (a & c) ^ (b & c);
                        uint t2 = S0 + mj;
                        hv = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
                    }
                    h0 += a; h1 += b; h2 += c; h3 += d; h4 += e; h5 += f; h6 += g; h7 += hv;
                }

                byte[] result = new byte[Sha256Len];
                WriteUInt32BE(result, 0, h0);
                WriteUInt32BE(result, 4, h1);
                WriteUInt32BE(result, 8, h2);
                WriteUInt32BE(result, 12, h3);
                WriteUInt32BE(result, 16, h4);
                WriteUInt32BE(result, 20, h5);
                WriteUInt32BE(result, 24, h6);
                WriteUInt32BE(result, 28, h7);
                return result;
            }

            private static uint Ror(uint x, int n) { return (x >> n) | (x << (32 - n)); }

            private static void WriteUInt32BE(byte[] dst, int offset, uint v)
            {
                dst[offset] = (byte)(v >> 24);
                dst[offset + 1] = (byte)(v >> 16);
                dst[offset + 2] = (byte)(v >> 8);
                dst[offset + 3] = (byte)(v);
            }
        }
    }
}
