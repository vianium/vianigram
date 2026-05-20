// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Ports.Outbound
{
    /// <summary>
    /// Outbound port for the cryptographic primitives the SecretChats
    /// context needs. The production adapter wraps
    /// <c>Vianigram.Core.Crypto</c>'s WinMD surface (Curve25519 / 2048-bit
    /// DH and AES-256-IGE). The stub implementation
    /// (<c>StubSecretCryptoPort</c>) returns deterministic dummy values so
    /// the rest of the pipeline can be built and smoke-tested.
    ///
    /// <para><b>Isolation contract (M3):</b> raw key bytes never flow
    /// through this interface as <c>byte[]</c> on the public managed
    /// surface; they are encapsulated in <see cref="AuthKey"/> instances
    /// (whose internals stay private to this assembly). Inputs that ARE
    /// <c>byte[]</c> here — DH primes, public values, ciphertexts — are
    /// all non-secret per the secret-chat protocol.</para>
    ///
    /// <para>This interface will be implemented by an adapter that delegates
    /// to the WinMD type <c>Vianigram.Crypto.SecretChatCrypto</c> (yet to
    /// ship). Methods like
    /// <c>GenerateDhKeyAsync</c>, <c>ComputeSharedSecret</c>,
    /// <c>EncryptIge</c>, <c>DecryptIge</c> map 1-to-1 to native helpers.
    /// The stub port today carries the same shape so downstream handlers
    /// don't need rewrites.</para>
    /// </summary>
    public interface ISecretCryptoPort
    {
        /// <summary>
        /// Validate a server-supplied DH parameter set: <c>p</c> must be a
        /// 2048-bit safe prime; <c>g</c> must be a quadratic residue mod
        /// <c>p</c>; <c>(p-1)/2</c> must also be prime. Production: native
        /// Miller-Rabin via the crypto vault. Stub: returns Ok() unconditionally
        /// (clearly logged).
        /// </summary>
        Task<Result<Unit, SecretChatError>> ValidateDhPrimeAsync(DhPrime prime, CancellationToken ct);

        /// <summary>
        /// Generate an ephemeral DH private exponent (256 cryptographically-
        /// random bytes) and return both the secret handle and the
        /// corresponding public value <c>g_a = g^a mod p</c>. The private
        /// exponent stays inside the crypto vault; only <c>g_a</c> escapes.
        ///
        /// <para>The returned <see cref="DhKeyPair"/> handle is consumed by a
        /// later <see cref="ComputeSharedSecretAsync"/> call; it has no
        /// other uses.</para>
        /// </summary>
        Task<Result<DhKeyPair, SecretChatError>> GenerateDhKeyAsync(DhPrime prime, CancellationToken ct);

        /// <summary>
        /// Compute the shared secret <c>auth_key = peerPublic^ourPrivate
        /// mod p</c> and return it wrapped in <see cref="AuthKey"/> (which
        /// caches the fingerprint and keeps the bytes private). After this
        /// call the supplied <see cref="DhKeyPair"/> is consumed and the
        /// private exponent is wiped from the vault.
        /// </summary>
        Task<Result<AuthKey, SecretChatError>> ComputeSharedSecretAsync(
            DhKeyPair ourKeyPair, byte[] peerPublic, DhPrime prime, CancellationToken ct);

        /// <summary>
        /// Encrypt a plaintext <c>decryptedMessage</c> envelope using
        /// AES-256-IGE keyed off the session's <c>auth_key</c>, including the
        /// 16-byte <c>msg_key</c> prefix per Telegram's MTProto2-on-secret-
        /// chats scheme. Returns the wire bytes ready to feed into
        /// <c>messages.sendEncrypted.data</c>.
        /// </summary>
        Task<Result<byte[], SecretChatError>> EncryptIgeAsync(AuthKey authKey, byte[] plaintext, CancellationToken ct);

        /// <summary>
        /// Decrypt a wire <c>encryptedMessage.bytes</c> payload (16-byte
        /// msg_key prefix + ciphertext) and return the inner plaintext.
        /// Validates the embedded MAC against the auth_key; mismatch surfaces
        /// as <see cref="SecretChatErrorKind.InvalidKey"/>.
        /// </summary>
        Task<Result<byte[], SecretChatError>> DecryptIgeAsync(AuthKey authKey, byte[] cipher, CancellationToken ct);

        /// <summary>
        /// Compute the visual emoji-key rendering for an established session.
        /// Production uses <c>SHA-256(auth_key || g_a)</c> per Telegram
        /// reference; stub derives 4 glyphs from the fingerprint alone.
        /// </summary>
        EmojiKey RenderEmojiKey(AuthKey authKey);
    }

    /// <summary>
    /// Opaque handle wrapping (a) the public DH value <c>g_a</c> the caller
    /// must put on the wire, and (b) a private-key index into the crypto
    /// vault. Single-use — passed to
    /// <see cref="ISecretCryptoPort.ComputeSharedSecretAsync"/> and discarded.
    ///
    /// <para>The public bytes are exposed because Telegram puts them on the
    /// wire; the private index is a non-secret integer the vault uses to
    /// look up the in-process secret exponent.</para>
    /// </summary>
    public sealed class DhKeyPair
    {
        private readonly byte[] _publicBytes;
        private readonly long _privateHandle;

        public DhKeyPair(byte[] publicBytes, long privateHandle)
        {
            _publicBytes = publicBytes ?? new byte[0];
            _privateHandle = privateHandle;
        }

        /// <summary>The 256-byte big-endian <c>g^x mod p</c> for our private exponent.</summary>
        public byte[] PublicBytes
        {
            get
            {
                byte[] copy = new byte[_publicBytes.Length];
                System.Buffer.BlockCopy(_publicBytes, 0, copy, 0, _publicBytes.Length);
                return copy;
            }
        }

        /// <summary>Vault-internal handle for the private exponent. Non-secret.</summary>
        public long PrivateHandle { get { return _privateHandle; } }
    }
}
