// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Application.UseCases
{
    /// <summary>
    /// Issue <c>messages.search#a0fda762</c> against a single peer. The handler
    /// validates the peer key, builds the first-page cursor, and returns a
    /// fresh <c>SearchSession</c> bound to that peer.
    /// </summary>
    public sealed class SearchInChatCommand
    {
        public string PeerKey { get; private set; }
        public string Query { get; private set; }
        public SearchFilter Filter { get; private set; }
        public int PageSize { get; private set; }

        public SearchInChatCommand(string peerKey, string query, SearchFilter filter, int pageSize = 20)
        {
            PeerKey = peerKey;
            Query = query ?? string.Empty;
            Filter = filter;
            PageSize = pageSize <= 0 ? 20 : pageSize;
        }
    }
}
