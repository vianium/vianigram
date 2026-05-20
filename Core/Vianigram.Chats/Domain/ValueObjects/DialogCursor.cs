// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Chats.Domain.ValueObjects
{
    /// <summary>
    /// Pagination cursor for Telegram's messages.getDialogs.
    /// Wraps the (offset_date, offset_id, offset_peer) triple the server expects
    /// to resume a paged scan of the dialog catalog.
    ///
    /// <see cref="Empty"/> represents the start-of-list cursor (offset_date = 0,
    /// offset_id = 0, offset_peer = null) which the server interprets as "from the top".
    /// Immutable value object.
    /// </summary>
    public sealed class DialogCursor
    {
        private static readonly DialogCursor _empty = new DialogCursor(default(DateTime), 0L, null);

        private readonly DateTime _offsetDate;
        private readonly long _offsetId;
        private readonly PeerId _offsetPeer;

        public DialogCursor(DateTime offsetDate, long offsetId, PeerId offsetPeer)
        {
            _offsetDate = offsetDate;
            _offsetId = offsetId;
            _offsetPeer = offsetPeer;
        }

        public static DialogCursor Empty { get { return _empty; } }

        public DateTime OffsetDate { get { return _offsetDate; } }
        public long OffsetId { get { return _offsetId; } }
        public PeerId OffsetPeer { get { return _offsetPeer; } }

        public bool IsEmpty
        {
            get
            {
                return _offsetId == 0L && _offsetPeer == null && _offsetDate == default(DateTime);
            }
        }
    }
}
