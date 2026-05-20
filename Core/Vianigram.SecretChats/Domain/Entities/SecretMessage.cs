// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Domain.Entities
{
    /// <summary>
    /// One end-to-end-encrypted message inside a
    /// <see cref="SecretSession"/>. Identity is the pair
    /// <c>(SecretChatId, RandomId)</c> — the random-id is the client-side
    /// dedupe key used by Telegram on <c>messages.sendEncrypted*</c>.
    ///
    /// <para>
    /// Direction: <see cref="IsOutgoing"/> distinguishes locally-sent rows
    /// from inbound rows. <see cref="SenderUserId"/> carries the peer's
    /// user_id for inbound messages and the local user's id for outbound.
    /// </para>
    ///
    /// <para>
    /// Content is held as plaintext <see cref="Body"/> for now, because the
    /// full secret-chat storage (encrypted at rest via DataProtectionProvider)
    /// ships later alongside <c>Vianigram.Storage</c>'s encrypted SQLite
    /// repository. The repository adapter is the layer responsible for writing
    /// ciphertext to disk; the in-memory aggregate sees plaintext only after
    /// the adapter has decrypted.
    /// </para>
    ///
    /// <para>
    /// <see cref="MediaRef"/> is a free-form pointer (e.g. an
    /// <c>encryptedFile</c> id + access_hash + dc_id + key_fingerprint) into
    /// Vianigram.Media's encrypted-upload store. It currently carries a string
    /// placeholder; a typed <c>EncryptedFileRef</c> value object will replace
    /// it.
    /// </para>
    /// </summary>
    public sealed class SecretMessage
    {
        private readonly long _randomId;
        private readonly DateTime _sentAt;
        private readonly long _senderUserId;
        private readonly bool _isOutgoing;
        private readonly string _body;
        private readonly Ttl _ttl;
        private readonly string _mediaRef;

        public SecretMessage(
            long randomId,
            DateTime sentAt,
            long senderUserId,
            bool isOutgoing,
            string body,
            Ttl ttl,
            string mediaRef)
        {
            _randomId = randomId;
            _sentAt = sentAt;
            _senderUserId = senderUserId;
            _isOutgoing = isOutgoing;
            _body = body ?? string.Empty;
            _ttl = ttl;
            _mediaRef = mediaRef;
        }

        /// <summary>Client-generated nonce used by Telegram to dedupe
        /// <c>messages.sendEncrypted</c> calls. Stable across retries.</summary>
        public long RandomId { get { return _randomId; } }

        /// <summary>Server-supplied <c>date</c> for inbound messages, or
        /// <see cref="IClock.UtcNow"/> at send time for outbound.</summary>
        public DateTime SentAt { get { return _sentAt; } }

        public long SenderUserId { get { return _senderUserId; } }
        public bool IsOutgoing { get { return _isOutgoing; } }

        /// <summary>Plaintext message body (UTF-8 string content).</summary>
        public string Body { get { return _body; } }

        /// <summary>Per-message self-destruct timer; <see cref="Ttl.None"/>
        /// when no TTL was set on the envelope.</summary>
        public Ttl Ttl { get { return _ttl; } }

        /// <summary>Optional media-store reference (currently a placeholder).</summary>
        public string MediaRef { get { return _mediaRef; } }
    }
}
