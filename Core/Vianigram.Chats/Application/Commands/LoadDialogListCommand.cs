// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Commands
{
    /// <summary>
    /// Fetch one page of dialogs starting at <see cref="Cursor"/>.
    /// Limit is clamped server-side; for the cold first-page case, callers
    /// usually pass <see cref="DialogCursor.Empty"/>.
    /// </summary>
    public sealed class LoadDialogListCommand
    {
        public int Limit { get; private set; }
        public DialogCursor Cursor { get; private set; }

        public LoadDialogListCommand(int limit, DialogCursor cursor)
        {
            if (limit <= 0) throw new ArgumentOutOfRangeException("limit", "limit must be positive");
            Limit = limit;
            Cursor = cursor ?? DialogCursor.Empty;
        }
    }
}
