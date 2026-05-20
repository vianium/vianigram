// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;

namespace Vianigram.Chats.Domain.ValueObjects
{
    /// <summary>
    /// One page of <see cref="DialogPreview"/> items returned by a paged dialog list query,
    /// plus the cursor needed to fetch the following page and a flag indicating
    /// whether more results exist on the server.
    /// </summary>
    public sealed class DialogPage
    {
        private static readonly DialogPreview[] _emptyArray = new DialogPreview[0];

        private readonly IList<DialogPreview> _items;
        private readonly DialogCursor _nextCursor;
        private readonly bool _hasMore;

        public DialogPage(IList<DialogPreview> items, DialogCursor nextCursor, bool hasMore)
        {
            _items = items ?? _emptyArray;
            _nextCursor = nextCursor ?? DialogCursor.Empty;
            _hasMore = hasMore;
        }

        public IList<DialogPreview> Items { get { return _items; } }
        public DialogCursor NextCursor { get { return _nextCursor; } }
        public bool HasMore { get { return _hasMore; } }

        public static DialogPage Empty
        {
            get { return new DialogPage(_emptyArray, DialogCursor.Empty, false); }
        }
    }
}
