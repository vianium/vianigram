// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.SecretChats.Domain.Events;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.SecretChats.Domain.Entities
{
    /// <summary>
    /// Aggregate root for one end-to-end secret chat with a single peer.
    ///
    /// <para><b>Identity:</b> <see cref="ChatId"/> (server-assigned during DH).
    /// While the session is in <see cref="SecretSessionState.Requesting"/> the
    /// chat id may be a placeholder (0 / negative); the server populates it
    /// when responding to <c>messages.requestEncryption</c>.</para>
    ///
    /// <para><b>State machine</b> (<see cref="SecretSessionState"/>):
    /// <c>Requesting → Pending → Established → (Renegotiating →) Established → Discarded</c>.
    /// Each transition method is the only entry point that can mutate
    /// <see cref="State"/>; invariants are checked locally and a domain event
    /// is staged for the handler to publish post-persistence.</para>
    ///
    /// <para><b>Key isolation (M3):</b> the session OWNS an
    /// <see cref="AuthKey"/> instance after DH, but the bytes inside it stay
    /// private. The aggregate exposes only the derived
    /// <see cref="Fingerprint"/>; encrypt/decrypt is delegated to
    /// <c>ISecretCryptoPort</c> and only that port reads the bytes.</para>
    ///
    /// <para><b>Sequence counters:</b> <see cref="OutSeq"/> and
    /// <see cref="InSeq"/> are tracked as 32-bit monotonic counters. The
    /// 100-entry replay-protection ring per
    /// <c>docs/managed-architecture/08-secret-chats.md §9</c> is planned.</para>
    ///
    /// <para><b>Event staging:</b> mutators append to a private pending list
    /// drained by <see cref="DequeuePendingEvents"/> after persistence. This
    /// keeps the aggregate dependency-free and makes the domain events
    /// transactional with the state change.</para>
    /// </summary>
    public sealed class SecretSession
    {
        private SecretChatId _chatId;
        private long _peerUserId;
        private bool _isInitiator;
        private SecretSessionState _state;
        private AuthKey _authKey;
        private DateTime _createdAt;
        private DateTime _lastActivityAt;
        private int _outSeq;
        private int _inSeq;
        private readonly Dictionary<long, SecretMessage> _messages;
        private readonly List<IDomainEvent> _pending;

        /// <summary>
        /// Construct an outgoing session in <see cref="SecretSessionState.Requesting"/>
        /// — the local user is initiating <c>messages.requestEncryption</c>.
        /// </summary>
        public static SecretSession StartOutgoing(SecretChatId chatId, long peerUserId, DateTime now)
        {
            if (peerUserId <= 0) throw new ArgumentException("peerUserId must be positive", "peerUserId");
            return new SecretSession(chatId, peerUserId, /*isInitiator*/ true, SecretSessionState.Requesting, now);
        }

        /// <summary>
        /// Construct a session for an incoming <c>encryptedChatRequested</c>
        /// (peer initiated) in <see cref="SecretSessionState.Pending"/>.
        /// </summary>
        public static SecretSession StartIncoming(SecretChatId chatId, long peerUserId, DateTime now)
        {
            if (peerUserId <= 0) throw new ArgumentException("peerUserId must be positive", "peerUserId");
            return new SecretSession(chatId, peerUserId, /*isInitiator*/ false, SecretSessionState.Pending, now);
        }

        private SecretSession(SecretChatId chatId, long peerUserId, bool isInitiator, SecretSessionState state, DateTime now)
        {
            _chatId = chatId;
            _peerUserId = peerUserId;
            _isInitiator = isInitiator;
            _state = state;
            _authKey = null;
            _createdAt = now;
            _lastActivityAt = now;
            _outSeq = 0;
            _inSeq = 0;
            _messages = new Dictionary<long, SecretMessage>();
            _pending = new List<IDomainEvent>(8);
        }

        // ---- read-only projection ------------------------------------------

        public SecretChatId ChatId { get { return _chatId; } }
        public long PeerUserId { get { return _peerUserId; } }
        public bool IsInitiator { get { return _isInitiator; } }
        public SecretSessionState State { get { return _state; } }
        public DateTime CreatedAt { get { return _createdAt; } }
        public DateTime LastActivityAt { get { return _lastActivityAt; } }
        public int OutSeq { get { return _outSeq; } }
        public int InSeq { get { return _inSeq; } }
        public int MessageCount { get { return _messages.Count; } }

        /// <summary>
        /// Derived 64-bit key fingerprint (low-byte SHA1 of the auth_key) once
        /// negotiation is complete. <see cref="KeyFingerprint"/> default-value
        /// (0) before that — call sites should gate on <see cref="HasKey"/>.
        /// </summary>
        public KeyFingerprint Fingerprint
        {
            get { return _authKey == null ? new KeyFingerprint(0L) : _authKey.Fingerprint; }
        }

        public bool HasKey { get { return _authKey != null && !_authKey.IsWiped; } }

        public IList<SecretMessage> SnapshotMessages()
        {
            var list = new List<SecretMessage>(_messages.Count);
            foreach (var kv in _messages) list.Add(kv.Value);
            return list;
        }

        // ---- transitions ---------------------------------------------------

        /// <summary>
        /// Server has acknowledged <c>messages.requestEncryption</c> with an
        /// <c>encryptedChatWaiting</c>; switch to <see cref="SecretSessionState.Pending"/>
        /// and update the chat-id with the server-assigned value (the local
        /// placeholder may have been 0).
        /// </summary>
        public void MarkRequestAcknowledged(SecretChatId serverAssignedId, DateTime now)
        {
            if (_state != SecretSessionState.Requesting)
                throw new InvalidOperationException("MarkRequestAcknowledged: expected state Requesting; was " + _state);
            _chatId = serverAssignedId;
            _state = SecretSessionState.Pending;
            _lastActivityAt = now;
            Stage(new SecretChatRequested(_chatId, _peerUserId, now));
        }

        /// <summary>
        /// Local user has accepted an incoming <c>encryptedChatRequested</c>:
        /// the negotiated <see cref="AuthKey"/> is installed and the session
        /// jumps to <see cref="SecretSessionState.Established"/>. Stages
        /// <see cref="SecretChatAccepted"/> and <see cref="SecretChatEstablished"/>.
        /// </summary>
        public void AcceptWithKey(AuthKey authKey, DateTime now)
        {
            if (authKey == null) throw new ArgumentNullException("authKey");
            if (_state != SecretSessionState.Pending)
                throw new InvalidOperationException("AcceptWithKey: expected state Pending; was " + _state);
            if (_isInitiator)
                throw new InvalidOperationException("AcceptWithKey: only the responder accepts");
            _authKey = authKey;
            _state = SecretSessionState.Established;
            _lastActivityAt = now;
            Stage(new SecretChatAccepted(_chatId, authKey.Fingerprint, now));
            Stage(new SecretChatEstablished(_chatId, authKey.Fingerprint, now));
        }

        /// <summary>
        /// Initiator side: peer has finalized DH; install the locally-computed
        /// <see cref="AuthKey"/> and verify its fingerprint matches the
        /// peer-asserted value (delivered with <c>encryptedChat</c>). Mismatch
        /// stages <see cref="KeyFingerprintMismatch"/> + transitions to
        /// <see cref="SecretSessionState.Discarded"/>; matched fingerprints
        /// transition to <see cref="SecretSessionState.Established"/>.
        /// </summary>
        public bool ConfirmWithKey(AuthKey authKey, KeyFingerprint peerAssertedFingerprint, DateTime now)
        {
            if (authKey == null) throw new ArgumentNullException("authKey");
            if (_state != SecretSessionState.Pending)
                throw new InvalidOperationException("ConfirmWithKey: expected state Pending; was " + _state);
            if (!_isInitiator)
                throw new InvalidOperationException("ConfirmWithKey: only the initiator confirms");

            if (authKey.Fingerprint != peerAssertedFingerprint)
            {
                authKey.Wipe();
                _state = SecretSessionState.Discarded;
                _lastActivityAt = now;
                Stage(new KeyFingerprintMismatch(_chatId, authKey.Fingerprint, peerAssertedFingerprint, now));
                Stage(new SecretChatDiscarded(_chatId, DiscardReason.FingerprintMismatch, now));
                return false;
            }

            _authKey = authKey;
            _state = SecretSessionState.Established;
            _lastActivityAt = now;
            Stage(new SecretChatEstablished(_chatId, authKey.Fingerprint, now));
            return true;
        }

        /// <summary>
        /// Append an outbound message to the session history and increment
        /// <see cref="OutSeq"/>. Stages <see cref="SecretMessageSent"/>.
        /// </summary>
        public void RecordOutgoing(SecretMessage message, DateTime now)
        {
            if (message == null) throw new ArgumentNullException("message");
            if (_state != SecretSessionState.Established)
                throw new InvalidOperationException("RecordOutgoing: session not Established (was " + _state + ")");
            if (!_messages.ContainsKey(message.RandomId))
            {
                _messages.Add(message.RandomId, message);
            }
            _outSeq++;
            _lastActivityAt = now;
            Stage(new SecretMessageSent(_chatId, message.RandomId, now));
        }

        /// <summary>
        /// Apply a successfully-decrypted inbound message. Stages
        /// <see cref="SecretMessageReceived"/>; bumps <see cref="InSeq"/>.
        /// Idempotent on duplicate <c>random_id</c> — Telegram retransmits.
        /// </summary>
        public void RecordIncoming(SecretMessage message, DateTime now)
        {
            if (message == null) throw new ArgumentNullException("message");
            if (_state != SecretSessionState.Established)
                throw new InvalidOperationException("RecordIncoming: session not Established (was " + _state + ")");
            if (_messages.ContainsKey(message.RandomId)) return; // dedupe
            _messages.Add(message.RandomId, message);
            _inSeq++;
            _lastActivityAt = now;
            Stage(new SecretMessageReceived(_chatId, message.RandomId, now));
        }

        /// <summary>
        /// Terminal transition: wipe the auth_key and stage
        /// <see cref="SecretChatDiscarded"/>. Idempotent — repeat calls after
        /// the first are no-ops so peer-discarded + local-discarded races are
        /// safe.
        /// </summary>
        public void Discard(DiscardReason reason, DateTime now)
        {
            if (_state == SecretSessionState.Discarded) return;
            if (_authKey != null) _authKey.Wipe();
            _state = SecretSessionState.Discarded;
            _lastActivityAt = now;
            Stage(new SecretChatDiscarded(_chatId, reason, now));
        }

        // ---- crypto-port handoff (internal) --------------------------------

        /// <summary>
        /// Internal accessor: hand the live <see cref="AuthKey"/> to a crypto
        /// port for AES-IGE encrypt/decrypt. The port reads the bytes via
        /// <see cref="AuthKey.CopyBytes"/>; the aggregate never lets a copy
        /// escape past the port boundary.
        /// </summary>
        internal AuthKey AuthKeyForCryptoPort()
        {
            return _authKey;
        }

        // ---- event staging --------------------------------------------------

        /// <summary>
        /// Drain pending domain events for the handler to publish post-
        /// persistence. Transactional with the state change.
        /// </summary>
        public IList<IDomainEvent> DequeuePendingEvents()
        {
            if (_pending.Count == 0) return new IDomainEvent[0];
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        private void Stage(IDomainEvent evt)
        {
            if (evt != null) _pending.Add(evt);
        }
    }
}
