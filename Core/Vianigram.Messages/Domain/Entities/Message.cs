// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Domain.Entities
{
    /// <summary>
    /// One message in a dialog. Identity is <see cref="MessageId"/> — once a
    /// pending message receives its server id via <see cref="ConfirmSent"/>
    /// the identity flips from "pending" to "confirmed" and the body becomes
    /// effectively immutable per principle M4: subsequent edits/deletes are
    /// modelled as separate state transitions and emit their own events.
    /// </summary>
    public sealed class Message
    {
        private Message(
            string peerKey,
            MessageId id,
            long? fromUserId,
            DateTime date,
            MessageContent content,
            long? replyToMessageId,
            bool isOutgoing,
            DeliveryState state)
        {
            if (string.IsNullOrEmpty(peerKey)) throw new ArgumentException("peerKey required", "peerKey");
            if (id == null) throw new ArgumentNullException("id");
            if (content == null) throw new ArgumentNullException("content");

            PeerKey = peerKey;
            Id = id;
            FromUserId = fromUserId;
            Date = date;
            Content = content;
            ReplyToMessageId = replyToMessageId;
            IsOutgoing = isOutgoing;
            DeliveryState = state;
        }

        public string PeerKey { get; private set; }
        public MessageId Id { get; private set; }
        public long? FromUserId { get; private set; }
        public DateTime Date { get; private set; }
        public MessageContent Content { get; private set; }
        public long? ReplyToMessageId { get; private set; }
        public bool IsOutgoing { get; private set; }
        public DeliveryState DeliveryState { get; private set; }
        public DateTime? EditedAt { get; private set; }
        public bool IsDeleted { get; private set; }
        public string FailureReason { get; private set; }

        // ---------- Factory helpers ----------

        /// <summary>
        /// Create an outgoing message in the optimistic <see cref="DeliveryState.Sending"/>
        /// state. The MessageId carries a negative client-temp id; it is
        /// rewritten in place on server ACK via <see cref="ConfirmSent"/>.
        /// </summary>
        public static Message NewOptimistic(string peerKey, long clientTempId, string text, long? replyTo, DateTime nowUtc, long? fromUserId = null)
        {
            return new Message(
                peerKey,
                MessageId.Pending(clientTempId),
                fromUserId,
                nowUtc,
                new MessageContentText(text),
                replyTo,
                isOutgoing: true,
                state: DeliveryState.Sending);
        }

        /// <summary>
        /// Create a message reconstructed from server data (history fetch or
        /// new-message update). Always confirmed, default state Delivered.
        /// </summary>
        public static Message FromServer(
            string peerKey,
            long serverId,
            long? fromUserId,
            DateTime date,
            MessageContent content,
            long? replyToMessageId,
            bool isOutgoing,
            DeliveryState state = DeliveryState.Delivered)
        {
            return new Message(
                peerKey,
                MessageId.Confirmed(serverId),
                fromUserId,
                date,
                content,
                replyToMessageId,
                isOutgoing,
                state);
        }

        // ---------- State transitions ----------

        /// <summary>
        /// Promote a pending optimistic message to a confirmed one once the
        /// server returns the real id. Idempotent if already confirmed with
        /// the same id; throws on conflicting double-confirm.
        /// </summary>
        public void ConfirmSent(long serverId, DateTime serverDate)
        {
            if (serverId <= 0) throw new ArgumentOutOfRangeException("serverId", "serverId must be positive");

            if (Id.IsConfirmed)
            {
                if (Id.ServerId != serverId)
                    throw new InvalidOperationException("Message already confirmed with a different server id.");
                return;
            }

            Id = MessageId.Confirmed(serverId);
            Date = serverDate;
            DeliveryState = DeliveryState.Sent;
            FailureReason = null;
        }

        public void MarkDelivered()
        {
            if (DeliveryState == DeliveryState.Read) return;
            DeliveryState = DeliveryState.Delivered;
        }

        public void MarkRead()
        {
            DeliveryState = DeliveryState.Read;
        }

        public void MarkFailed(string reason)
        {
            DeliveryState = DeliveryState.Failed;
            FailureReason = reason ?? string.Empty;
        }

        /// <summary>
        /// Apply a server-acknowledged edit. The body swap is permitted — the
        /// immutable invariant of M4 lives at the event-log level: every edit
        /// produces a <c>MessageEdited</c> event so projections can replay history.
        /// </summary>
        public void Edit(MessageContent newContent, DateTime editedAt)
        {
            if (newContent == null) throw new ArgumentNullException("newContent");
            if (IsDeleted) throw new InvalidOperationException("Cannot edit a deleted message.");
            Content = newContent;
            EditedAt = editedAt;
        }

        public void MarkDeleted()
        {
            IsDeleted = true;
        }
    }
}
