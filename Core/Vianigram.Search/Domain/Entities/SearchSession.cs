// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Search.Domain.Events;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Domain.Entities
{
    /// <summary>
    /// Aggregate root for a single in-flight or completed search. Holds the
    /// original <see cref="SearchQuery"/>, the <see cref="SearchFilter"/>, the
    /// running result list, the paging <see cref="SearchCursor"/>, the
    /// server-reported total count, a stable session id (used to correlate
    /// cancellation), and a list of pending <see cref="IDomainEvent"/> drained
    /// by the handler after each state transition.
    ///
    /// <para><b>Lifecycle</b> (modelled as a small state machine on
    /// <see cref="State"/>):</para>
    /// <list type="bullet">
    ///   <item><description><c>Idle</c> — newly constructed; no roundtrip yet.</description></item>
    ///   <item><description><c>Loading</c> — first page request in flight.</description></item>
    ///   <item><description><c>PageLoaded</c> — at least one page was received; <c>Cursor</c> points to the next.</description></item>
    ///   <item><description><c>LoadingMore</c> — paging in flight (continuation of <c>PageLoaded</c>).</description></item>
    ///   <item><description><c>Completed</c> — server has nothing more (next page returned 0 or cursor exhausted).</description></item>
    ///   <item><description><c>Cancelled</c> — the consumer explicitly cancelled.</description></item>
    ///   <item><description><c>Failed</c> — a permanent error halted the session.</description></item>
    /// </list>
    /// <para>Mirrors the staging-events pattern used by the Settings
    /// <c>UserPreferences</c> aggregate: handlers call mutators, then
    /// <c>DequeuePendingEvents</c> after persistence/RPC succeeds and publish
    /// each event on the bus.</para>
    /// </summary>
    public sealed class SearchSession
    {
        private readonly Guid _sessionId;
        private readonly SearchQuery _query;
        private readonly List<SearchHit> _results;
        private readonly List<IDomainEvent> _pending;

        private SearchCursor _cursor;
        private int _totalCount;
        private SessionState _state;
        private DateTime _startedAt;
        private DateTime _lastUpdateAt;

        public SearchSession(SearchQuery query, SearchCursor initial, DateTime now)
        {
            if (query == null) throw new ArgumentNullException("query");
            if (initial == null) throw new ArgumentNullException("initial");

            _sessionId = Guid.NewGuid();
            _query = query;
            _cursor = initial;
            _results = new List<SearchHit>(32);
            _pending = new List<IDomainEvent>(4);
            _totalCount = 0;
            _state = SessionState.Idle;
            _startedAt = now;
            _lastUpdateAt = now;
        }

        // ---- identity / read accessors -----------------------------------------

        public Guid SessionId { get { return _sessionId; } }
        public SearchQuery Query { get { return _query; } }
        public SearchFilter Filter { get { return _query.Filter; } }
        public SearchCursor Cursor { get { return _cursor; } }
        public SessionState State { get { return _state; } }
        public int TotalCount { get { return _totalCount; } }
        public int LoadedCount { get { return _results.Count; } }
        public DateTime StartedAt { get { return _startedAt; } }
        public DateTime LastUpdateAt { get { return _lastUpdateAt; } }

        /// <summary>Defensive copy of the running result page.</summary>
        public IList<SearchHit> Results
        {
            get
            {
                if (_results.Count == 0) return new SearchHit[0];
                return _results.ToArray();
            }
        }

        /// <summary>True when no further <c>LoadMore</c> roundtrip is meaningful.</summary>
        public bool IsTerminal
        {
            get
            {
                return _state == SessionState.Completed
                    || _state == SessionState.Cancelled
                    || _state == SessionState.Failed;
            }
        }

        // ---- transitions -------------------------------------------------------

        /// <summary>
        /// Mark the session as loading the very first page. Stages a
        /// <see cref="SearchStarted"/> event.
        /// </summary>
        public void BeginInitialLoad(DateTime at)
        {
            if (_state != SessionState.Idle)
                throw new InvalidOperationException("BeginInitialLoad is only valid from Idle (was " + _state + ")");
            _state = SessionState.Loading;
            _lastUpdateAt = at;
            Stage(new SearchStarted(_sessionId, _query, at));
        }

        /// <summary>
        /// Mark the session as loading another page. Valid only from
        /// <see cref="SessionState.PageLoaded"/>.
        /// </summary>
        public void BeginLoadMore(DateTime at)
        {
            if (_state != SessionState.PageLoaded)
                throw new InvalidOperationException("BeginLoadMore is only valid from PageLoaded (was " + _state + ")");
            _state = SessionState.LoadingMore;
            _lastUpdateAt = at;
        }

        /// <summary>
        /// Append a freshly received page to the running result list, advance
        /// the cursor, update the server-reported total, and stage a
        /// <see cref="ResultsArrived"/> event. When <paramref name="hits"/> is
        /// empty AND the cursor would not advance, the session is automatically
        /// transitioned to <see cref="SessionState.Completed"/> (a
        /// <see cref="SearchCompleted"/> event is staged in addition).
        /// </summary>
        public void RecordPage(IList<SearchHit> hits, SearchCursor nextCursor, int totalCount, DateTime at)
        {
            if (hits == null) throw new ArgumentNullException("hits");
            if (nextCursor == null) throw new ArgumentNullException("nextCursor");
            if (_state != SessionState.Loading && _state != SessionState.LoadingMore)
                throw new InvalidOperationException("RecordPage requires Loading or LoadingMore (was " + _state + ")");

            for (int i = 0; i < hits.Count; i++)
            {
                if (hits[i] != null) _results.Add(hits[i]);
            }
            _cursor = nextCursor;
            if (totalCount >= 0) _totalCount = totalCount;
            _lastUpdateAt = at;

            Stage(new ResultsArrived(_sessionId, hits, _results.Count, _totalCount, at));

            bool exhausted = hits.Count == 0 || _results.Count >= _totalCount;
            if (exhausted)
            {
                _state = SessionState.Completed;
                Stage(new SearchCompleted(_sessionId, _query, _results.Count, _totalCount, at));
            }
            else
            {
                _state = SessionState.PageLoaded;
            }
        }

        /// <summary>Mark the session as cancelled by the consumer. Stages <see cref="SearchCancelled"/>.</summary>
        public void Cancel(DateTime at, string reason)
        {
            if (IsTerminal) return; // idempotent — cancelling a finished session is a no-op
            _state = SessionState.Cancelled;
            _lastUpdateAt = at;
            Stage(new SearchCancelled(_sessionId, reason ?? "user", at));
        }

        /// <summary>
        /// Mark the session as permanently failed. Carries the
        /// <see cref="SearchError"/> that triggered the transition.
        /// </summary>
        public void Fail(SearchError error, DateTime at)
        {
            if (error == null) throw new ArgumentNullException("error");
            if (IsTerminal) return;
            _state = SessionState.Failed;
            _lastUpdateAt = at;
            Stage(new SearchFailed(_sessionId, _query, error, at));
        }

        // ---- pending events ----------------------------------------------------

        /// <summary>Drain pending domain events for the handler to publish.</summary>
        public IList<IDomainEvent> DequeuePendingEvents()
        {
            if (_pending.Count == 0) return new IDomainEvent[0];
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        private void Stage(IDomainEvent evt)
        {
            _pending.Add(evt);
        }

        public override string ToString()
        {
            return "SearchSession(id=" + _sessionId + " state=" + _state +
                   " loaded=" + _results.Count + "/" + _totalCount + " " + _query + ")";
        }
    }

    /// <summary>State machine state for <see cref="SearchSession"/>.</summary>
    public enum SessionState
    {
        Idle = 0,
        Loading = 1,
        PageLoaded = 2,
        LoadingMore = 3,
        Completed = 4,
        Cancelled = 5,
        Failed = 6
    }
}
