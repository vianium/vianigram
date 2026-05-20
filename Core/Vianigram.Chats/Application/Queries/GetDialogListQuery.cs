// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Queries
{
    /// <summary>
    /// Read-side query for a paged window into the dialog catalog.
    /// Resolves to <c>Result&lt;DialogPage, ChatError&gt;</c> at the handler.
    /// </summary>
    public sealed class GetDialogListQuery
    {
        public int Limit { get; private set; }
        public DialogCursor Cursor { get; private set; }

        public GetDialogListQuery(int limit, DialogCursor cursor)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException("limit");
            Limit = limit;
            Cursor = cursor ?? DialogCursor.Empty;
        }
    }
}
