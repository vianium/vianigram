// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage.Streams;

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// 256-byte negotiated DH shared secret for one secret chat session.
    ///
    /// <para>
    /// <b>Isolation rule (principles.md §M3):</b> the raw bytes are private
    /// and are NOT exposed via a public property. Other bounded contexts
    /// must never persist or read these bytes; persistence happens through
    /// <c>ISecretChatRepository</c> + <c>ISecretCryptoPort</c>, which know to
    /// route raw bytes through the native crypto vault (in production —
    /// stub today). The only public facets are derived, non-secret values:
    /// the <see cref="Fingerprint"/> and the visual <see cref="EmojiKey"/>.
    /// </para>
    ///
    /// <para>
    /// To keep the door closed even within the assembly, the raw-byte
    /// accessors are <c>internal</c> and intentionally allocate fresh copies
    /// — callers can't pin a reference to the live buffer. The class also
    /// provides <see cref="Wipe"/> so the discard / logout path can zero the
    /// buffer before the GC reclaims it.
    /// </para>
    ///
    /// <para>
    /// A future revision will replace the in-managed-memory storage with an
    /// opaque <c>RootKeyHandle</c> indexed into <c>Vianigram.Core.Crypto</c>'s
    /// native vault; this class will then hold only that handle.
    /// </para>
    /// </summary>
    public sealed class AuthKey
    {
        /// <summary>The Telegram secret-chat auth_key is exactly 256 bytes (2048-bit DH output).</summary>
        public const int LengthBytes = 256;

        private readonly byte[] _bytes;
        private readonly KeyFingerprint _fingerprint;
        private bool _wiped;

        /// <summary>
        /// Construct from a freshly negotiated 256-byte DH shared secret. The
        /// caller transfers ownership of the array; we copy defensively so
        /// the source can be wiped independently. Computes the fingerprint
        /// once at construction and caches it.
        /// </summary>
        internal AuthKey(byte[] rawBytes)
        {
            if (rawBytes == null) throw new ArgumentNullException("rawBytes");
            if (rawBytes.Length != LengthBytes)
                throw new ArgumentException("auth_key must be exactly 256 bytes; got " + rawBytes.Length, "rawBytes");
            _bytes = new byte[LengthBytes];
            System.Buffer.BlockCopy(rawBytes, 0, _bytes, 0, LengthBytes);
            _fingerprint = ComputeFingerprint(_bytes);
            _wiped = false;
        }

        /// <summary>
        /// 64-bit fingerprint (last 8 bytes of <c>SHA1(auth_key)</c>) — non-
        /// secret; safe to render to UI / logs.
        /// </summary>
        public KeyFingerprint Fingerprint { get { return _fingerprint; } }

        /// <summary>True after <see cref="Wipe"/> has been called.</summary>
        public bool IsWiped { get { return _wiped; } }

        /// <summary>
        /// Internal accessor: returns a defensive copy of the raw bytes.
        /// ONLY <c>ISecretCryptoPort</c> implementations and the repository
        /// adapter (for encrypted-at-rest serialization) should call this.
        /// Marked <c>internal</c> so other bounded contexts cannot reach it
        /// at all.
        /// </summary>
        internal byte[] CopyBytes()
        {
            EnsureNotWiped();
            byte[] copy = new byte[LengthBytes];
            System.Buffer.BlockCopy(_bytes, 0, copy, 0, LengthBytes);
            return copy;
        }

        /// <summary>
        /// Internal: rehydrate from previously-persisted (and decrypted by
        /// the repository) raw bytes. Distinct factory so the call sites are
        /// auditable.
        /// </summary>
        internal static AuthKey FromPersistedBytes(byte[] persisted)
        {
            return new AuthKey(persisted);
        }

        /// <summary>
        /// Zero the underlying buffer. Called by the aggregate root on
        /// <c>Discard</c> and by the repository on <c>Delete</c> /
        /// account-logout. After wiping, all accessors throw.
        /// </summary>
        public void Wipe()
        {
            if (_wiped) return;
            for (int i = 0; i < _bytes.Length; i++) _bytes[i] = 0;
            _wiped = true;
        }

        private void EnsureNotWiped()
        {
            if (_wiped) throw new InvalidOperationException("auth_key has been wiped");
        }

        public override string ToString()
        {
            // Never render the bytes — only the fingerprint identifies the key
            // outside the boundary.
            return "AuthKey{" + _fingerprint + "}";
        }

        // ---- fingerprint computation ----------------------------------------

        /// <summary>
        /// Telegram's secret-chat key fingerprint = last 8 bytes of
        /// <c>SHA1(auth_key)</c>, read as a little-endian <see cref="long"/>.
        /// Reference: TDLib's <c>DhHandshake::calc_key_id</c>:
        /// <code>
        ///   UInt&lt;160&gt; sha;
        ///   sha1(auth_key, sha.raw);
        ///   return as&lt;int64&gt;(sha.raw + 12);
        /// </code>
        /// </summary>
        internal static KeyFingerprint ComputeFingerprint(byte[] keyBytes)
        {
            if (keyBytes == null) throw new ArgumentNullException("keyBytes");
            // SHA1 is acceptable here: we are matching Telegram's wire-level
            // key id, not using SHA1 for confidentiality. The fingerprint is
            // a public 64-bit value used as a stable integrity tag —
            // switching hashes would fork the protocol. WP8.1 exposes SHA1
            // via WinRT's HashAlgorithmProvider (not System.Security.Cryptography).
            HashAlgorithmProvider provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha1);
            IBuffer input = CryptographicBuffer.CreateFromByteArray(keyBytes);
            IBuffer hashed = provider.HashData(input);
            byte[] digest;
            CryptographicBuffer.CopyToByteArray(hashed, out digest);
            long fp = 0L;
            for (int i = 0; i < 8; i++)
            {
                fp |= ((long)digest[12 + i]) << (i * 8);
            }
            return new KeyFingerprint(fp);
        }
    }
}
