// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Application.UseCases
{
    /// <summary>
    /// Issue <c>messages.searchGlobal#4bc6589a</c> with the supplied query and
    /// filter. The handler builds an empty cursor (page 1) and returns a fresh
    /// <c>SearchSession</c>.
    /// </summary>
    public sealed class GlobalSearchCommand
    {
        public string Query { get; private set; }
        public SearchFilter Filter { get; private set; }
        public int PageSize { get; private set; }

        public GlobalSearchCommand(string query, SearchFilter filter, int pageSize = 20)
        {
            Query = query ?? string.Empty;
            Filter = filter;
            PageSize = pageSize <= 0 ? 20 : pageSize;
        }
    }
}
