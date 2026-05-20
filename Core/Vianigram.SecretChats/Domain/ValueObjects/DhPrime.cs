// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// Container for the DH parameters Telegram returns from
    /// <c>messages.getDhConfig</c>:
    ///   * <c>g</c> — small generator (typically 2 or 3).
    ///   * <c>p</c> — 2048-bit safe prime, 256 bytes big-endian.
    ///   * <c>version</c> — server-tracked version of the parameter set
    ///     so clients can refresh when it rotates.
    ///   * <c>random</c> — server-supplied additional 256 random bytes the
    ///     client mixes into its own RNG seed before deriving <c>a</c>.
    ///
    /// <para>
    /// We expose <c>p</c> as a <see cref="byte"/> array because the prime
    /// itself is non-secret (it's a published Telegram parameter, identical
    /// across all sessions until <c>version</c> rotates). The <c>random</c>
    /// nonce is also non-secret — it's mixed with locally-generated
    /// randomness inside the crypto port.
    /// </para>
    ///
    /// <para>
    /// Validation: the application layer validates <c>p</c> via
    /// <see cref="ISecretCryptoPort.ValidateDhPrimeAsync"/> — the native
    /// vault checks (a) <c>p</c> is exactly 2048 bits, (b) <c>p</c> is prime,
    /// (c) <c>(p-1)/2</c> is prime, (d) <c>g</c> is a quadratic residue mod
    /// <c>p</c>. Failure to validate aborts the session with
    /// <see cref="SecretChatErrorKind.InvalidKey"/>.
    /// </para>
    /// </summary>
    public sealed class DhPrime
    {
        /// <summary>Telegram's DH prime is always exactly 2048 bits = 256 bytes.</summary>
        public const int LengthBytes = 256;

        private readonly int _g;
        private readonly byte[] _p;
        private readonly int _version;
        private readonly byte[] _serverRandom;

        public DhPrime(int g, byte[] p, int version, byte[] serverRandom)
        {
            if (p == null) throw new ArgumentNullException("p");
            if (p.Length != LengthBytes)
                throw new ArgumentException("DH prime must be exactly " + LengthBytes + " bytes; got " + p.Length, "p");
            if (g != 2 && g != 3 && g != 4 && g != 5 && g != 6 && g != 7)
                throw new ArgumentException("DH generator g must be in {2,3,4,5,6,7}; got " + g, "g");
            _g = g;
            _p = (byte[])p.Clone();
            _version = version;
            _serverRandom = serverRandom == null ? new byte[0] : (byte[])serverRandom.Clone();
        }

        public int G { get { return _g; } }
        public int Version { get { return _version; } }

        /// <summary>Defensive copy of the 256-byte prime <c>p</c>.</summary>
        public byte[] CopyP()
        {
            byte[] copy = new byte[_p.Length];
            Buffer.BlockCopy(_p, 0, copy, 0, _p.Length);
            return copy;
        }

        /// <summary>Defensive copy of the server-supplied random nonce.</summary>
        public byte[] CopyServerRandom()
        {
            byte[] copy = new byte[_serverRandom.Length];
            Buffer.BlockCopy(_serverRandom, 0, copy, 0, _serverRandom.Length);
            return copy;
        }

        public override string ToString()
        {
            return "DhPrime{g=" + _g + ", version=" + _version + ", p=" + _p.Length + "B}";
        }
    }
}
