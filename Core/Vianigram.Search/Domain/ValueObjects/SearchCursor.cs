// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Search.Domain.ValueObjects
{
    /// <summary>
    /// Paging cursor used to drive the next <c>messages.search</c> /
    /// <c>messages.searchGlobal</c> roundtrip. Mirrors the wire fields
    /// Telegram expects: <c>offset_id</c>, <c>offset_date</c>,
    /// <c>offset_peer</c>, plus the page size <c>limit</c>.
    ///
    /// <para>The <c>add_offset</c> field is fixed at 0 in V1 (the cursor
    /// pattern is "tail of last page" — Telegram returns messages in
    /// descending date / id order and we ask for the next slice using the
    /// last-seen ids).</para>
    /// </summary>
    public sealed class SearchCursor
    {
        /// <summary><c>offset_id:int</c> — last seen message id (0 = start of page 1).</summary>
        public int OffsetId { get; private set; }

        /// <summary><c>offset_date:int</c> — Unix seconds (UTC). 0 = no constraint.</summary>
        public int OffsetDate { get; private set; }

        /// <summary>
        /// <c>offset_peer:InputPeer</c> serialized as the same opaque peer key
        /// shape used elsewhere in the context (<c>"user:42"</c> /
        /// <c>"chat:7"</c> / <c>"channel:1001"</c>). Null / empty maps to
        /// <c>inputPeerEmpty</c> on the wire.
        /// </summary>
        public string OffsetPeerKey { get; private set; }

        /// <summary>Page size requested from the server. V1 default = 20.</summary>
        public int Limit { get; private set; }

        public SearchCursor(int offsetId, int offsetDate, string offsetPeerKey, int limit)
        {
            if (limit <= 0 || limit > 100) throw new ArgumentOutOfRangeException("limit", "1..100");
            OffsetId = offsetId < 0 ? 0 : offsetId;
            OffsetDate = offsetDate < 0 ? 0 : offsetDate;
            OffsetPeerKey = string.IsNullOrEmpty(offsetPeerKey) ? null : offsetPeerKey;
            Limit = limit;
        }

        /// <summary>Cursor for the first page (offsets all zero, default page size 20).</summary>
        public static SearchCursor FirstPage(int limit = 20)
        {
            return new SearchCursor(0, 0, null, limit);
        }

        /// <summary>True when this cursor would request the first page (no offsets supplied).</summary>
        public bool IsFirstPage
        {
            get { return OffsetId == 0 && OffsetDate == 0 && OffsetPeerKey == null; }
        }

        /// <summary>
        /// Build the next-page cursor from the tail of the last received page.
        /// </summary>
        public SearchCursor Advance(int lastMessageId, int lastMessageDate, string lastMessagePeerKey)
        {
            return new SearchCursor(lastMessageId, lastMessageDate, lastMessagePeerKey, Limit);
        }

        public override string ToString()
        {
            return "SearchCursor(id=" + OffsetId + " date=" + OffsetDate + " peer=" + (OffsetPeerKey ?? "?") + " limit=" + Limit + ")";
        }
    }
}
