// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Contacts.Application.UseCases
{
    /// <summary>
    /// Server-side search across users you can see (<c>contacts.search#11f812d8</c>).
    /// Limit is clamped server-side, but we also clamp here to prevent absurd
    /// values from reaching the wire.
    /// </summary>
    public sealed class SearchContactsCommand
    {
        public const int MaxLimit = 100;

        public string Query { get; private set; }
        public int Limit { get; private set; }

        public SearchContactsCommand(string query, int limit)
        {
            if (query == null) throw new ArgumentNullException("query");
            if (limit <= 0) throw new ArgumentOutOfRangeException("limit", "limit must be positive");
            Query = query;
            Limit = limit > MaxLimit ? MaxLimit : limit;
        }
    }
}
