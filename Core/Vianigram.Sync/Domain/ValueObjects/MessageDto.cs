// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Cross-context projection of a Telegram message — the minimum a downstream
    /// context (Messages, Notifications, Search) needs to consume a sync-emitted
    /// "remote message received" event without a typed dependency on Vianigram.Messages.
    ///
    /// This is intentionally lossy. The Messages context maintains the canonical
    /// MessageStream aggregate; this DTO is a neutral inter-context coordinate.
    ///
    /// All fields are immutable. Serializable POCO with primitives only — per
    /// principle 5 (process isolation seam).
    /// </summary>
    public sealed class MessageDto
    {
        public MessageDto(
            int id,
            string peerKey,
            long fromUserId,
            int date,
            string message,
            int replyToMessageId,
            bool isOutgoing,
            bool isMediaUnread,
            bool isSilent,
            int editDate)
        {
            if (id < 0) throw new ArgumentOutOfRangeException("id");
            Id = id;
            PeerKey = peerKey ?? string.Empty;
            FromUserId = fromUserId;
            Date = date;
            Message = message ?? string.Empty;
            ReplyToMessageId = replyToMessageId;
            IsOutgoing = isOutgoing;
            IsMediaUnread = isMediaUnread;
            IsSilent = isSilent;
            EditDate = editDate;
        }

        public int Id { get; private set; }
        public string PeerKey { get; private set; }
        public long FromUserId { get; private set; }
        public int Date { get; private set; }
        public string Message { get; private set; }
        public int ReplyToMessageId { get; private set; }
        public bool IsOutgoing { get; private set; }
        public bool IsMediaUnread { get; private set; }
        public bool IsSilent { get; private set; }
        public int EditDate { get; private set; }
    }
}
