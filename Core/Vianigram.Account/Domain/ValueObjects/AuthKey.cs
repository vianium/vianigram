// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// MTProto auth key wrapper. Represents the 256-byte long-lived auth_key
    /// plus its derived <see cref="AuthKeyId"/> (lower 64 bits of SHA1).
    ///
    /// Public surface only exposes the at-rest ciphertext (encrypted via
    /// <c>DataProtectionProvider</c>) and the key id. Plaintext bytes are kept
    /// internal and only readable from inside this assembly so the crypto /
    /// adapter ports can hand them to the native MTProto channel.
    ///
    /// See principles.md §M3 (Key material isolation): managed code never
    /// surfaces auth_key plaintext through public API.
    /// </summary>
    public sealed class AuthKey
    {
        private readonly byte[] _plaintext; // 256 bytes; never returned publicly
        private readonly byte[] _ciphertext;
        private readonly ulong _id;

        private AuthKey(byte[] plaintext, byte[] ciphertext, ulong id)
        {
            _plaintext = plaintext;
            _ciphertext = ciphertext;
            _id = id;
        }

        /// <summary>
        /// Construct from freshly-generated key material plus the at-rest
        /// ciphertext that the storage adapter has already produced. The
        /// adapter is responsible for not retaining a plaintext copy.
        /// </summary>
        public static AuthKey FromGenerated(byte[] plaintext, byte[] ciphertext, ulong id)
        {
            if (plaintext == null) throw new ArgumentNullException("plaintext");
            if (plaintext.Length != 256) throw new ArgumentException("auth_key must be 256 bytes", "plaintext");
            if (ciphertext == null) throw new ArgumentNullException("ciphertext");
            return new AuthKey(plaintext, ciphertext, id);
        }

        /// <summary>
        /// Rehydrate from at-rest ciphertext. Plaintext is unavailable until a
        /// crypto adapter calls back through internal API.
        /// </summary>
        public static AuthKey FromCiphertext(byte[] ciphertext, ulong id)
        {
            if (ciphertext == null) throw new ArgumentNullException("ciphertext");
            return new AuthKey(null, ciphertext, id);
        }

        public ulong Id
        {
            get { return _id; }
        }

        /// <summary>
        /// Encrypted-at-rest blob. Safe to log length, never log contents.
        /// </summary>
        public byte[] EncryptedAtRest
        {
            get { return _ciphertext; }
        }

        /// <summary>
        /// Internal accessor for the crypto/adapter layer to hand the key bytes
        /// to the native MTProto channel. Returns null if the AuthKey was
        /// rehydrated from ciphertext and not yet decrypted by an adapter.
        /// </summary>
        internal byte[] PlaintextOrNull()
        {
            return _plaintext;
        }

        public override string ToString()
        {
            return "auth_key(id=" + _id + ", at_rest=" + (_ciphertext == null ? 0 : _ciphertext.Length) + "B)";
        }
    }
}
