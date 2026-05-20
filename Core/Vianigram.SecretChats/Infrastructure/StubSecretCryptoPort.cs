// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Infrastructure
{
    /// <summary>
    /// Stub for <see cref="ISecretCryptoPort"/>. Returns DETERMINISTIC,
    /// NON-CRYPTOGRAPHIC dummy values so the rest of the pipeline (RPC
    /// framing, repository persistence, event flow) can be built and
    /// smoke-tested before the real WinMD helpers ship in
    /// <c>Vianigram.Core.Crypto</c>.
    ///
    /// <para><b>This adapter is NOT secure and MUST NOT ship in a
    /// production build.</b> Every operation logs at
    /// <see cref="LogLevel.Warn"/> on construction and on first call so the
    /// composition root can verify it isn't accidentally selected. The
    /// <see cref="ISecretChatsApi"/> requirement that secret chats be E2E-
    /// encrypted is INTENTIONALLY VIOLATED here — the goal is to compile
    /// and exercise the orchestration layer.</para>
    ///
    /// <para>This will be replaced with
    /// <c>NativeSecretCryptoPort</c>, which delegates to the native
    /// <c>Vianigram.Crypto.SecretChatCrypto</c> WinMD type for:
    /// <list type="bullet">
    ///   <item>2048-bit DH key generation (<c>g^a mod p</c> in native bignum).</item>
    ///   <item>Shared-secret derivation (<c>peerPublic^a mod p</c>).</item>
    ///   <item>AES-256-IGE encrypt/decrypt with the canonical msg_key/aes_key
    ///         derivation per Telegram's secret-chat scheme.</item>
    ///   <item>SHA-256(auth_key || g_a) emoji-key derivation.</item>
    /// </list>
    /// </para>
    /// </summary>
    public sealed class StubSecretCryptoPort : ISecretCryptoPort
    {
        private readonly IComponentLogger _log;
        private long _nextHandle;

        public StubSecretCryptoPort(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            _log = new TimestampedLogger(logger, "SecretChats.StubSecretCryptoPort");
            _nextHandle = 1L;
            _log.Warn("StubSecretCryptoPort active — secret chats are NOT actually encrypted. A later build wires the real crypto.");
        }

        public Task<Result<Unit, SecretChatError>> ValidateDhPrimeAsync(DhPrime prime, CancellationToken ct)
        {
            if (prime == null)
                return TaskFromResult(Result<Unit, SecretChatError>.Fail(SecretChatError.InvalidKey("prime is null")));
            // Real impl: native Miller-Rabin on p, on (p-1)/2, generator check.
            _log.Debug("stub: ValidateDhPrime g=" + prime.G + " v=" + prime.Version);
            return TaskFromResult(Result<Unit, SecretChatError>.Ok(Unit.Value));
        }

        public Task<Result<DhKeyPair, SecretChatError>> GenerateDhKeyAsync(DhPrime prime, CancellationToken ct)
        {
            if (prime == null)
                return TaskFromResult(Result<DhKeyPair, SecretChatError>.Fail(SecretChatError.InvalidKey("prime is null")));
            // Deterministic placeholder: a 256-byte buffer derived from the
            // private handle. NOT random. NOT secure. The real port calls
            // native RNG + bignum modular exponentiation.
            long handle = System.Threading.Interlocked.Increment(ref _nextHandle);
            byte[] pub = new byte[256];
            for (int i = 0; i < pub.Length; i++) pub[i] = (byte)((handle + i) & 0xFF);
            _log.Debug("stub: GenerateDhKey handle=" + handle);
            return TaskFromResult(Result<DhKeyPair, SecretChatError>.Ok(new DhKeyPair(pub, handle)));
        }

        public Task<Result<AuthKey, SecretChatError>> ComputeSharedSecretAsync(
            DhKeyPair ourKeyPair, byte[] peerPublic, DhPrime prime, CancellationToken ct)
        {
            if (ourKeyPair == null)
                return TaskFromResult(Result<AuthKey, SecretChatError>.Fail(SecretChatError.InvalidKey("ourKeyPair is null")));
            if (peerPublic == null || peerPublic.Length == 0)
                return TaskFromResult(Result<AuthKey, SecretChatError>.Fail(SecretChatError.InvalidKey("peerPublic empty")));
            if (prime == null)
                return TaskFromResult(Result<AuthKey, SecretChatError>.Fail(SecretChatError.InvalidKey("prime is null")));

            // Stub: blend the peer-public bytes with our handle to produce a
            // deterministic 256-byte buffer. Both sides derive the same
            // buffer if and only if they call this with matching inputs —
            // which they won't, because the public values differ. So the
            // stub doesn't actually produce matching auth_keys; it just
            // produces SOMETHING the rest of the pipeline can carry.
            byte[] raw = new byte[AuthKey.LengthBytes];
            for (int i = 0; i < raw.Length; i++)
            {
                int peerByte = peerPublic[i % peerPublic.Length];
                raw[i] = (byte)((peerByte ^ (i + (int)ourKeyPair.PrivateHandle)) & 0xFF);
            }
            var authKey = new AuthKey(raw);
            _log.Debug("stub: ComputeSharedSecret -> fp=" + authKey.Fingerprint);
            return TaskFromResult(Result<AuthKey, SecretChatError>.Ok(authKey));
        }

        public Task<Result<byte[], SecretChatError>> EncryptIgeAsync(AuthKey authKey, byte[] plaintext, CancellationToken ct)
        {
            if (authKey == null)
                return TaskFromResult(Result<byte[], SecretChatError>.Fail(SecretChatError.InvalidKey("authKey null")));
            if (plaintext == null) plaintext = new byte[0];

            // Stub: prefix with the 8-byte fingerprint as the 'msg_key' (real
            // impl uses 16-byte SHA-256-derived msg_key) and copy plaintext.
            // No actual encryption.
            byte[] cipher = new byte[8 + plaintext.Length];
            long fp = authKey.Fingerprint.Value;
            for (int i = 0; i < 8; i++) cipher[i] = (byte)((fp >> (i * 8)) & 0xFF);
            Buffer.BlockCopy(plaintext, 0, cipher, 8, plaintext.Length);
            _log.Debug("stub: EncryptIge plaintext=" + plaintext.Length + "B (NOT actually encrypted)");
            return TaskFromResult(Result<byte[], SecretChatError>.Ok(cipher));
        }

        public Task<Result<byte[], SecretChatError>> DecryptIgeAsync(AuthKey authKey, byte[] cipher, CancellationToken ct)
        {
            if (authKey == null)
                return TaskFromResult(Result<byte[], SecretChatError>.Fail(SecretChatError.InvalidKey("authKey null")));
            if (cipher == null || cipher.Length < 8)
                return TaskFromResult(Result<byte[], SecretChatError>.Fail(SecretChatError.InvalidKey("cipher too short")));

            // Verify the stub-fingerprint prefix.
            long fp = authKey.Fingerprint.Value;
            for (int i = 0; i < 8; i++)
            {
                if (cipher[i] != (byte)((fp >> (i * 8)) & 0xFF))
                {
                    return TaskFromResult(Result<byte[], SecretChatError>.Fail(
                        SecretChatError.InvalidKey("stub-cipher fingerprint prefix mismatch")));
                }
            }
            byte[] plaintext = new byte[cipher.Length - 8];
            Buffer.BlockCopy(cipher, 8, plaintext, 0, plaintext.Length);
            _log.Debug("stub: DecryptIge -> " + plaintext.Length + "B");
            return TaskFromResult(Result<byte[], SecretChatError>.Ok(plaintext));
        }

        public EmojiKey RenderEmojiKey(AuthKey authKey)
        {
            if (authKey == null) throw new ArgumentNullException("authKey");
            // Current derivation: from fingerprint alone. The real port wires
            // SHA-256(auth_key || g_a) per stock Telegram clients.
            return EmojiKey.FromFingerprint(authKey.Fingerprint);
        }

        // ---- WP8.1 Task.FromResult shim ------------------------------------
        private static Task<T> TaskFromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
