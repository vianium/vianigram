// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Ports.Inbound
{
    /// <summary>
    /// Public surface of the SecretChats bounded context.
    /// Every method is async, takes a <see cref="CancellationToken"/>, and
    /// returns <c>Result&lt;T, SecretChatError&gt;</c>; no exceptions cross
    /// this boundary.
    ///
    /// <para>Consumers: presentation/ViewModels, other contexts via ACL
    /// adapters, composition root for wiring.</para>
    ///
    /// <para>The current surface is narrower than
    /// <c>docs/managed-architecture/08-secret-chats.md §5</c> — rekey,
    /// per-message TTL, encrypted media, and read-history are planned for
    /// later.</para>
    /// </summary>
    public interface ISecretChatsApi
    {
        /// <summary>Initiate a new secret chat with <paramref name="userId"/>
        /// (<c>messages.requestEncryption</c>).</summary>
        Task<Result<SecretSession, SecretChatError>> RequestAsync(long userId, CancellationToken ct);

        /// <summary>Accept an incoming <c>encryptedChatRequested</c>
        /// (<c>messages.acceptEncryption</c>).</summary>
        Task<Result<SecretSession, SecretChatError>> AcceptAsync(SecretChatId id, CancellationToken ct);

        /// <summary>Encrypt and send a UTF-8 text body
        /// (<c>messages.sendEncrypted</c>).</summary>
        Task<Result<Unit, SecretChatError>> SendTextAsync(SecretChatId id, string text, CancellationToken ct);

        /// <summary>Discard a session (<c>messages.discardEncryption</c>),
        /// wipe the auth_key, and stage a discard event.</summary>
        Task<Result<Unit, SecretChatError>> DiscardAsync(SecretChatId id, CancellationToken ct);

        /// <summary>
        /// Update the secret-chat self-destruct timer (also known as the
        /// "session TTL"). Builds and dispatches a
        /// <c>decryptedMessageActionSetMessageTTL#a1733aec</c> wrapped in a
        /// <c>decryptedMessageService</c> envelope; the AES-IGE-encrypted
        /// ciphertext is shipped via <c>messages.sendEncryptedService</c>.
        /// Pass <paramref name="seconds"/> = 0 to disable.
        /// </summary>
        Task<Result<Unit, SecretChatError>> SetSelfDestructTimerAsync(SecretChatId id, int seconds, CancellationToken ct);

        /// <summary>
        /// Returns a page of locally-stored history for the supplied secret
        /// chat. Reads the in-memory aggregate via the repository port —
        /// secret-chat ciphertext is NEVER server-stored, so this method
        /// does not (and cannot) hit MTProto. Messages are ordered
        /// oldest-first.
        ///
        /// <para><paramref name="offsetMsgId"/> is the
        /// <see cref="SecretMessage.RandomId"/> the caller already has; pass
        /// <c>null</c> to start from the oldest. <paramref name="limit"/> is
        /// clamped to a positive integer; values &lt;= 0 produce an empty
        /// page.</para>
        /// </summary>
        Task<Result<SecretMessagePage, SecretChatError>> LoadHistoryAsync(SecretChatId id, long? offsetMsgId, int limit, CancellationToken ct);

        /// <summary>Synchronously fetch the persisted session, or null if
        /// none is known. Returns the live aggregate; do NOT mutate from
        /// outside the bounded context.</summary>
        SecretSession GetSession(SecretChatId id);

        /// <summary>Render the visual emoji-key fingerprint for an established
        /// session. Returns null when the session is not Established or
        /// unknown.</summary>
        EmojiKey GetEmojiKey(SecretChatId id);

        /// <summary>Raised on every session lifecycle change (Requested,
        /// Accepted, Established, Discarded, FingerprintMismatch, Rekeyed).
        /// Multicast; thread-safe add/remove.</summary>
        event EventHandler<SecretChatChangedEventArgs> SessionChanged;

        /// <summary>Raised on every successful inbound decrypted message.</summary>
        event EventHandler<SecretMessageReceivedEventArgs> MessageReceived;
    }
}
