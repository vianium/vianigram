// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Contacts.Application.UseCases
{
    /// <summary>
    /// Fetch the blocked-list page (<c>contacts.getBlocked#9a868f80</c>). V1
    /// pulls the first page only; pagination will arrive when the UI grows
    /// virtualized lists. The handler ignores <see cref="MyStoriesFrom"/> in
    /// V1 (Stories blocking is a future feature; see
    /// docs/managed-architecture/04-contacts.md §12).
    /// </summary>
    public sealed class GetBlockedListCommand
    {
        public int Offset { get; private set; }
        public int Limit { get; private set; }
        public bool MyStoriesFrom { get; private set; }

        public static readonly GetBlockedListCommand FirstPage = new GetBlockedListCommand(0, 100, false);

        public GetBlockedListCommand(int offset, int limit, bool myStoriesFrom)
        {
            Offset = offset < 0 ? 0 : offset;
            Limit = limit <= 0 ? 100 : limit;
            MyStoriesFrom = myStoriesFrom;
        }
    }
}
