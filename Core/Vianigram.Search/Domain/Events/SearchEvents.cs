// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Domain.Events
{
    /// <summary>
    /// Emitted when a <c>SearchSession</c> transitions from <c>Idle</c> to
    /// <c>Loading</c>, i.e. the first page roundtrip is about to be issued.
    /// Subscribers (presentation layer) typically toggle a "searching"
    /// indicator on receipt.
    /// </summary>
    public sealed class SearchStarted : IDomainEvent
    {
        public Guid SessionId { get; private set; }
        public SearchQuery Query { get; private set; }
        public DateTime At { get; private set; }

        public SearchStarted(Guid sessionId, SearchQuery query, DateTime at)
        {
            if (query == null) throw new ArgumentNullException("query");
            SessionId = sessionId;
            Query = query;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a page of results lands on the aggregate. Carries only the
    /// freshly received hits (<see cref="Page"/>) plus the new running totals
    /// — subscribers that need the full list re-read the aggregate / API.
    /// </summary>
    public sealed class ResultsArrived : IDomainEvent
    {
        public Guid SessionId { get; private set; }
        public IList<SearchHit> Page { get; private set; }
        public int LoadedCount { get; private set; }
        public int TotalCount { get; private set; }
        public DateTime At { get; private set; }

        public ResultsArrived(Guid sessionId, IList<SearchHit> page, int loadedCount, int totalCount, DateTime at)
        {
            SessionId = sessionId;
            Page = page ?? (IList<SearchHit>)new SearchHit[0];
            LoadedCount = loadedCount;
            TotalCount = totalCount;
            At = at;
        }
    }

    /// <summary>
    /// Emitted exactly once per session, when the server signals that no
    /// further pages will follow (empty page or <c>LoadedCount &gt;= TotalCount</c>).
    /// </summary>
    public sealed class SearchCompleted : IDomainEvent
    {
        public Guid SessionId { get; private set; }
        public SearchQuery Query { get; private set; }
        public int LoadedCount { get; private set; }
        public int TotalCount { get; private set; }
        public DateTime At { get; private set; }

        public SearchCompleted(Guid sessionId, SearchQuery query, int loadedCount, int totalCount, DateTime at)
        {
            if (query == null) throw new ArgumentNullException("query");
            SessionId = sessionId;
            Query = query;
            LoadedCount = loadedCount;
            TotalCount = totalCount;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the consumer cancels the session — either through
    /// <c>ISearchApi.CancelAsync</c> or because a newer query supersedes it
    /// inside the application orchestrator.
    /// </summary>
    public sealed class SearchCancelled : IDomainEvent
    {
        public Guid SessionId { get; private set; }
        public string Reason { get; private set; }
        public DateTime At { get; private set; }

        public SearchCancelled(Guid sessionId, string reason, DateTime at)
        {
            SessionId = sessionId;
            Reason = reason ?? "user";
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a session transitions into <c>Failed</c> with a permanent
    /// <see cref="SearchError"/>. <c>FloodWait</c> typically maps here too —
    /// callers retry after <c>RetryAfterSeconds</c>.
    /// </summary>
    public sealed class SearchFailed : IDomainEvent
    {
        public Guid SessionId { get; private set; }
        public SearchQuery Query { get; private set; }
        public SearchError Error { get; private set; }
        public DateTime At { get; private set; }

        public SearchFailed(Guid sessionId, SearchQuery query, SearchError error, DateTime at)
        {
            if (query == null) throw new ArgumentNullException("query");
            if (error == null) throw new ArgumentNullException("error");
            SessionId = sessionId;
            Query = query;
            Error = error;
            At = at;
        }
    }
}
