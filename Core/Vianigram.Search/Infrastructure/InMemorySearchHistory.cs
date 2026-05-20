// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Search.Domain;
using Vianigram.Search.Domain.ValueObjects;
using Vianigram.Search.Ports.Outbound;

namespace Vianigram.Search.Infrastructure
{
    /// <summary>
    /// In-memory search-history store: every query lives in process memory
    /// guarded by a private monitor, capped at <see cref="Capacity"/> entries
    /// (default = 50), most-recent first.
    ///
    /// Sufficient for cold-start, tests, and UI consumption while the
    /// LocalSettings-backed adapter in <c>Vianigram.Storage</c> (or the App
    /// composition root) is built. Hot-swap point: replace the binding in
    /// <see cref="Vianigram.Search.Composition.SearchCompositionRoot"/> with
    /// the persistent adapter and the application layer is unchanged.
    ///
    /// <para><b>LRU semantics</b>: <see cref="RecordAsync"/> moves an
    /// existing entry to the front (matched by <c>SearchQuery.Text</c>
    /// ordinal). When inserting past capacity, the oldest entry is evicted.
    /// Empty-text queries are silently dropped — the architecture doc spec'd
    /// only non-empty user-typed queries enter history.</para>
    ///
    /// <para><b>Thread-safety</b>: every operation takes a lock on a private
    /// gate object.</para>
    /// </summary>
    public sealed class InMemorySearchHistory : ISearchHistory
    {
        private readonly object _gate = new object();
        private readonly LinkedList<SearchQuery> _entries = new LinkedList<SearchQuery>();
        private readonly int _capacity;

        public InMemorySearchHistory(int capacity = 50)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException("capacity", "must be positive");
            _capacity = capacity;
        }

        public int Capacity { get { return _capacity; } }

        public Task<Result<Unit, SearchError>> RecordAsync(SearchQuery query, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (query == null)
                return Task.FromResult(Result<Unit, SearchError>.Fail(SearchError.InvalidValue("query required")));
            if (string.IsNullOrEmpty(query.Text))
                return Task.FromResult(Result<Unit, SearchError>.Ok(Unit.Value));

            lock (_gate)
            {
                // Remove any existing entry with the same text; insert at front.
                var node = _entries.First;
                while (node != null)
                {
                    var next = node.Next;
                    if (string.Equals(node.Value.Text, query.Text, StringComparison.Ordinal))
                    {
                        _entries.Remove(node);
                    }
                    node = next;
                }

                _entries.AddFirst(query);

                while (_entries.Count > _capacity)
                {
                    _entries.RemoveLast();
                }
            }

            return Task.FromResult(Result<Unit, SearchError>.Ok(Unit.Value));
        }

        public Task<Result<IList<SearchQuery>, SearchError>> GetRecentAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            List<SearchQuery> copy;
            lock (_gate)
            {
                copy = new List<SearchQuery>(_entries.Count);
                foreach (var q in _entries) copy.Add(q);
            }
            return Task.FromResult(Result<IList<SearchQuery>, SearchError>.Ok((IList<SearchQuery>)copy));
        }

        public Task<Result<Unit, SearchError>> ClearAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _entries.Clear();
            }
            return Task.FromResult(Result<Unit, SearchError>.Ok(Unit.Value));
        }
    }
}
