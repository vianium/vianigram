// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Chats.Domain.ValueObjects
{
    /// <summary>
    /// Read-model projection of a <see cref="Vianigram.Chats.Domain.Entities.Dialog"/> for
    /// the dialog list UI. Carries enough metadata to render a row without exposing
    /// the aggregate. Immutable.
    ///
    /// LastMessageText is pre-truncated by the projector (target ~100 chars) so
    /// the UI doesn't decide truncation policy. LastMessageId is nullable because
    /// a fresh dialog may have no top message yet.
    /// </summary>
    public sealed class DialogPreview
    {
        private readonly PeerId _peer;
        private readonly string _title;
        private readonly string _lastMessageText;
        private readonly DateTime _lastMessageDate;
        private readonly int _unreadCount;
        private readonly bool _isPinned;
        private readonly bool _isMuted;
        private readonly DateTime? _mutedUntil;
        private readonly long? _lastMessageId;

        public DialogPreview(
            PeerId peer,
            string title,
            string lastMessageText,
            DateTime lastMessageDate,
            int unreadCount,
            bool isPinned,
            bool isMuted,
            DateTime? mutedUntil,
            long? lastMessageId)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            if (unreadCount < 0) throw new ArgumentOutOfRangeException("unreadCount", "must be >= 0");

            _peer = peer;
            _title = title ?? string.Empty;
            _lastMessageText = lastMessageText ?? string.Empty;
            _lastMessageDate = lastMessageDate;
            _unreadCount = unreadCount;
            _isPinned = isPinned;
            _isMuted = isMuted;
            _mutedUntil = mutedUntil;
            _lastMessageId = lastMessageId;
        }

        public PeerId Peer { get { return _peer; } }
        public string Title { get { return _title; } }
        public string LastMessageText { get { return _lastMessageText; } }
        public DateTime LastMessageDate { get { return _lastMessageDate; } }
        public int UnreadCount { get { return _unreadCount; } }
        public bool IsPinned { get { return _isPinned; } }
        public bool IsMuted { get { return _isMuted; } }
        public DateTime? MutedUntil { get { return _mutedUntil; } }
        public long? LastMessageId { get { return _lastMessageId; } }
    }
}
