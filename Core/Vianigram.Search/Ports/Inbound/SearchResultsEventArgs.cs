// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="ISearchApi.ResultsArrived"/> when a
    /// page of results lands on a session. Mirrors the
    /// <c>ResultsArrived</c> domain event in a CLR-event shape so XAML / UI
    /// layers that don't take an <c>IEventBus</c> dependency can still
    /// subscribe.
    /// </summary>
    public sealed class SearchResultsEventArgs : EventArgs
    {
        public Guid SessionId { get; private set; }
        public IList<SearchHit> Page { get; private set; }
        public int LoadedCount { get; private set; }
        public int TotalCount { get; private set; }
        public DateTime At { get; private set; }

        public SearchResultsEventArgs(Guid sessionId, IList<SearchHit> page, int loadedCount, int totalCount, DateTime at)
        {
            SessionId = sessionId;
            Page = page ?? (IList<SearchHit>)new SearchHit[0];
            LoadedCount = loadedCount;
            TotalCount = totalCount;
            At = at;
        }
    }
}
