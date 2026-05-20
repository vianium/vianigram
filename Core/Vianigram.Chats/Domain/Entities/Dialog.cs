// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Chats.Domain.Events;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Kernel.Events;

namespace Vianigram.Chats.Domain.Entities
{
    /// <summary>
    /// Aggregate root for a single conversation. Identified by <see cref="PeerId"/>.
    ///
    /// Owns dialog-level metadata (title, photo url, unread count, pin/mute/archive
    /// state, folder placement, channel-specific flags). Does NOT own message history —
    /// that belongs to the Messages bounded context. The dialog only carries a pointer
    /// (<see cref="LastMessageId"/>) into that history.
    ///
    /// Mutators stage <see cref="IDomainEvent"/> instances on a pending list. The caller
    /// (handler / repository) drains them via <see cref="DequeuePendingEvents"/> and
    /// publishes them on the bus once the persistence write succeeds. This keeps the
    /// aggregate dependency-free and makes events transactional with the state change.
    /// </summary>
    public sealed class Dialog
    {
        private readonly PeerId _peer;
        private readonly List<IDomainEvent> _pending;

        private string _title;
        private string _photoSmallUrl;
        private DateTime _lastActivityAt;
        private long _lastMessageId;
        private int _unreadCount;
        private bool _isPinned;
        private bool _isMuted;
        private DateTime? _mutedUntil;
        private bool _isVerified;
        private bool _isScam;
        private bool _isArchived;
        private int? _folderId;

        public Dialog(
            PeerId peer,
            string title,
            DateTime lastActivityAt,
            long lastMessageId,
            int unreadCount)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            if (unreadCount < 0) throw new ArgumentOutOfRangeException("unreadCount");

            _peer = peer;
            _title = title ?? string.Empty;
            _lastActivityAt = lastActivityAt;
            _lastMessageId = lastMessageId;
            _unreadCount = unreadCount;
            _pending = new List<IDomainEvent>(4);
        }

        public PeerId Peer { get { return _peer; } }
        public string Title { get { return _title; } }
        public string PhotoSmallUrl { get { return _photoSmallUrl; } }
        public DateTime LastActivityAt { get { return _lastActivityAt; } }
        public long LastMessageId { get { return _lastMessageId; } }
        public int UnreadCount { get { return _unreadCount; } }
        public bool IsPinned { get { return _isPinned; } }
        public bool IsMuted { get { return _isMuted; } }
        public DateTime? MutedUntil { get { return _mutedUntil; } }
        public bool IsVerified { get { return _isVerified; } }
        public bool IsScam { get { return _isScam; } }
        public bool IsArchived { get { return _isArchived; } }
        public int? FolderId { get { return _folderId; } }

        /// <summary>
        /// Bulk update from a server sync (messages.getDialogs). Compares each facet
        /// against the current state and emits one <see cref="DialogUpdated"/> per changed
        /// facet. Cheap for unchanged dialogs.
        /// </summary>
        public void ApplyServerUpdate(
            string title,
            string photoSmallUrl,
            DateTime lastActivityAt,
            long lastMessageId,
            int unreadCount,
            bool isPinned,
            bool isMuted,
            DateTime? mutedUntil,
            bool isVerified,
            bool isScam,
            bool isArchived,
            int? folderId,
            DateTime at)
        {
            if (unreadCount < 0) throw new ArgumentOutOfRangeException("unreadCount");

            if (!string.Equals(_title, title ?? string.Empty, StringComparison.Ordinal))
            {
                _title = title ?? string.Empty;
                Stage(new DialogUpdated(_peer, ChangeKind.Title, at));
            }

            if (!string.Equals(_photoSmallUrl ?? string.Empty, photoSmallUrl ?? string.Empty, StringComparison.Ordinal))
            {
                _photoSmallUrl = photoSmallUrl;
                Stage(new DialogUpdated(_peer, ChangeKind.Photo, at));
            }

            if (_lastMessageId != lastMessageId || _lastActivityAt != lastActivityAt)
            {
                _lastMessageId = lastMessageId;
                _lastActivityAt = lastActivityAt;
                Stage(new DialogUpdated(_peer, ChangeKind.LastMessage, at));
            }

            if (_unreadCount != unreadCount)
            {
                _unreadCount = unreadCount;
                Stage(new DialogUpdated(_peer, ChangeKind.UnreadCount, at));
            }

            if (_isPinned != isPinned)
            {
                _isPinned = isPinned;
                Stage(new DialogUpdated(_peer, ChangeKind.Pinned, at));
            }

            if (_isMuted != isMuted || _mutedUntil != mutedUntil)
            {
                _isMuted = isMuted;
                _mutedUntil = mutedUntil;
                Stage(new DialogUpdated(_peer, ChangeKind.Muted, at));
            }

            if (_isArchived != isArchived || _folderId != folderId)
            {
                _isArchived = isArchived;
                _folderId = folderId;
                Stage(new DialogUpdated(_peer, ChangeKind.Archived, at));
            }

            // Verified/Scam are channel-derived flags; they change rarely, no event channel
            // assigned in v1 (subscribers can re-read aggregate via repository).
            _isVerified = isVerified;
            _isScam = isScam;
        }

        /// <summary>
        /// Caller marks messages up to and including <paramref name="upToMessageId"/> as read.
        /// If the cursor reaches the latest message, unread is cleared. Always emits
        /// <see cref="ChangeKind.UnreadCount"/> when the count actually drops.
        /// </summary>
        public void MarkAsRead(int upToMessageId, DateTime at)
        {
            if (upToMessageId < 0) throw new ArgumentOutOfRangeException("upToMessageId");
            if (_unreadCount == 0) return;

            // V1 simplification: any read cursor at-or-past the top message clears unread.
            // A precise per-message accounting will arrive with Sync's pts pipeline.
            if (upToMessageId >= _lastMessageId)
            {
                _unreadCount = 0;
                Stage(new DialogUpdated(_peer, ChangeKind.UnreadCount, at));
            }
        }

        public void Pin(DateTime at)
        {
            if (_isPinned) return;
            _isPinned = true;
            Stage(new DialogUpdated(_peer, ChangeKind.Pinned, at));
        }

        public void Unpin(DateTime at)
        {
            if (!_isPinned) return;
            _isPinned = false;
            Stage(new DialogUpdated(_peer, ChangeKind.Pinned, at));
        }

        /// <summary>
        /// Mute for a duration, or forever if <paramref name="until"/> is null.
        /// Null window means "muted forever" (Telegram convention).
        /// </summary>
        public void Mute(TimeSpan? until, DateTime at)
        {
            DateTime? newUntil = until.HasValue ? (DateTime?)at.Add(until.Value) : null;
            if (_isMuted && _mutedUntil == newUntil) return;
            _isMuted = true;
            _mutedUntil = newUntil;
            Stage(new DialogUpdated(_peer, ChangeKind.Muted, at));
        }

        public void Unmute(DateTime at)
        {
            if (!_isMuted && !_mutedUntil.HasValue) return;
            _isMuted = false;
            _mutedUntil = null;
            Stage(new DialogUpdated(_peer, ChangeKind.Muted, at));
        }

        public void IncrementUnread(DateTime at)
        {
            _unreadCount = checked(_unreadCount + 1);
            Stage(new DialogUpdated(_peer, ChangeKind.UnreadCount, at));
        }

        public DialogPreview ToPreview(string lastMessageText)
        {
            return new DialogPreview(
                _peer,
                _title,
                lastMessageText ?? string.Empty,
                _lastActivityAt,
                _unreadCount,
                _isPinned,
                _isMuted,
                _mutedUntil,
                _lastMessageId == 0L ? (long?)null : _lastMessageId);
        }

        /// <summary>
        /// Drain pending domain events (for the handler to publish post-persistence).
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
            _pending.Add(evt);
        }
    }
}
