// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Messages.Domain.ValueObjects
{
    /// <summary>
    /// Identity of a message — either a server-assigned id (positive) or a
    /// client-assigned temporary id (negative, monotonically decreasing per
    /// session) used for optimistic-UI sends prior to server ACK.
    ///
    /// Telegram itself reserves negative IDs for client-local messages, so the
    /// two ranges never collide.
    /// </summary>
    public sealed class MessageId : IEquatable<MessageId>
    {
        private MessageId(long serverId, long? clientTempId)
        {
            ServerId = serverId;
            ClientTempId = clientTempId;
        }

        public long ServerId { get; private set; }

        public long? ClientTempId { get; private set; }

        public bool IsConfirmed
        {
            get { return ServerId > 0; }
        }

        public bool IsPending
        {
            get { return !IsConfirmed; }
        }

        public static MessageId Confirmed(long serverId)
        {
            if (serverId <= 0) throw new ArgumentOutOfRangeException("serverId", "serverId must be positive");
            return new MessageId(serverId, null);
        }

        public static MessageId Pending(long clientTempId)
        {
            if (clientTempId >= 0) throw new ArgumentOutOfRangeException("clientTempId", "clientTempId must be negative");
            return new MessageId(0, clientTempId);
        }

        public bool Equals(MessageId other)
        {
            if (ReferenceEquals(other, null)) return false;
            return ServerId == other.ServerId && ClientTempId == other.ClientTempId;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as MessageId);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)(ServerId ^ (ServerId >> 32));
                if (ClientTempId.HasValue)
                {
                    long t = ClientTempId.Value;
                    hash = (hash * 397) ^ (int)(t ^ (t >> 32));
                }
                return hash;
            }
        }

        public override string ToString()
        {
            return IsConfirmed
                ? "MessageId(srv=" + ServerId + ")"
                : "MessageId(tmp=" + (ClientTempId.HasValue ? ClientTempId.Value : 0) + ")";
        }
    }
}
