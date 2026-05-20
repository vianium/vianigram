// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Stickers.Application.UseCases
{
    /// <summary>
    /// Search published sticker sets via <c>messages.searchStickerSets#35705b8a</c>.
    /// The result is a discovery list — hits are NOT folded into the local
    /// installed collection. The handler exposes them as-is for "Install"
    /// flows.
    /// </summary>
    public sealed class SearchStickersCommand
    {
        public const int DefaultMaxResults = 50;

        public string Query { get; private set; }
        public bool ExcludeFeatured { get; private set; }
        public long Hash { get; private set; }

        public SearchStickersCommand(string query, bool excludeFeatured, long hash)
        {
            if (query == null) throw new ArgumentNullException("query");
            Query = query;
            ExcludeFeatured = excludeFeatured;
            Hash = hash;
        }

        public SearchStickersCommand(string query)
            : this(query, false, 0L)
        {
        }
    }
}
