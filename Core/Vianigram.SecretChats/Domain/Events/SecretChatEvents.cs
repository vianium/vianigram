// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.SecretChats.Domain.Events
{
    /// <summary>
    /// Emitted when the local user has issued <c>messages.requestEncryption</c>
    /// and the server returned <c>encryptedChatWaiting</c>. Session state at
    /// emission time is <see cref="SecretSessionState.Pending"/>.
    /// </summary>
    public sealed class SecretChatRequested : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public long PeerUserId { get; private set; }
        public DateTime At { get; private set; }

        public SecretChatRequested(SecretChatId chatId, long peerUserId, DateTime at)
        {
            ChatId = chatId;
            PeerUserId = peerUserId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the local user has accepted an incoming
    /// <c>encryptedChatRequested</c> by issuing <c>messages.acceptEncryption</c>
    /// with a freshly computed <c>g_b</c> and key fingerprint. Session state
    /// at emission time is <see cref="SecretSessionState.Established"/>.
    /// </summary>
    public sealed class SecretChatAccepted : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public KeyFingerprint Fingerprint { get; private set; }
        public DateTime At { get; private set; }

        public SecretChatAccepted(SecretChatId chatId, KeyFingerprint fingerprint, DateTime at)
        {
            ChatId = chatId;
            Fingerprint = fingerprint;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the session reaches <see cref="SecretSessionState.Established"/>
    /// — i.e. both sides have computed the auth_key and the fingerprints
    /// agree. Subscribers (Presentation, Chats virtual-entry registrar) react
    /// here.
    /// </summary>
    public sealed class SecretChatEstablished : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public KeyFingerprint Fingerprint { get; private set; }
        public DateTime At { get; private set; }

        public SecretChatEstablished(SecretChatId chatId, KeyFingerprint fingerprint, DateTime at)
        {
            ChatId = chatId;
            Fingerprint = fingerprint;
            At = at;
        }
    }

    /// <summary>
    /// Emitted on terminal session shutdown — either side issued discard, or
    /// a security check (fingerprint mismatch) aborted the session. The
    /// auth_key has been wiped from the aggregate by the time this fires.
    /// </summary>
    public sealed class SecretChatDiscarded : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public DiscardReason Reason { get; private set; }
        public DateTime At { get; private set; }

        public SecretChatDiscarded(SecretChatId chatId, DiscardReason reason, DateTime at)
        {
            ChatId = chatId;
            Reason = reason;
            At = at;
        }
    }

    /// <summary>
    /// Emitted whenever an inbound <c>encryptedMessage</c> has been
    /// successfully decrypted, fingerprint-verified, and committed to the
    /// session's history. Carries the <c>random_id</c> so subscribers can
    /// dedupe against optimistically-rendered local rows.
    /// </summary>
    public sealed class SecretMessageReceived : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public long RandomId { get; private set; }
        public DateTime At { get; private set; }

        public SecretMessageReceived(SecretChatId chatId, long randomId, DateTime at)
        {
            ChatId = chatId;
            RandomId = randomId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when an outbound <c>messages.sendEncrypted</c> has been
    /// acknowledged by the server. Carries the random id so the optimistic
    /// row can be reconciled with the persisted row.
    /// </summary>
    public sealed class SecretMessageSent : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public long RandomId { get; private set; }
        public DateTime At { get; private set; }

        public SecretMessageSent(SecretChatId chatId, long randomId, DateTime at)
        {
            ChatId = chatId;
            RandomId = randomId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the locally-derived key fingerprint disagrees with the
    /// peer-asserted value during <c>messages.acceptEncryption</c> or on an
    /// inbound <c>encryptedMessage</c>. The session is in
    /// <see cref="SecretSessionState.Discarded"/> by the time subscribers see
    /// this. Always pair with a corresponding
    /// <see cref="SecretChatDiscarded"/> for downstream UI flows.
    /// </summary>
    public sealed class KeyFingerprintMismatch : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public KeyFingerprint Expected { get; private set; }
        public KeyFingerprint Actual { get; private set; }
        public DateTime At { get; private set; }

        public KeyFingerprintMismatch(SecretChatId chatId, KeyFingerprint expected, KeyFingerprint actual, DateTime at)
        {
            ChatId = chatId;
            Expected = expected;
            Actual = actual;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a rekey sub-flow has finalized and the session has a new
    /// fingerprint. The rekey flow is currently stubbed; this event is wired
    /// so subscribers can be authored ahead of the full implementation.
    /// </summary>
    public sealed class KeyRekeyed : IDomainEvent
    {
        public SecretChatId ChatId { get; private set; }
        public KeyFingerprint PreviousFingerprint { get; private set; }
        public KeyFingerprint CurrentFingerprint { get; private set; }
        public DateTime At { get; private set; }

        public KeyRekeyed(SecretChatId chatId, KeyFingerprint previousFingerprint, KeyFingerprint currentFingerprint, DateTime at)
        {
            ChatId = chatId;
            PreviousFingerprint = previousFingerprint;
            CurrentFingerprint = currentFingerprint;
            At = at;
        }
    }
}
