// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Search.Domain;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Ports.Outbound
{
    /// <summary>
    /// Outbound port for the recent-queries cache. The in-memory
    /// implementation (<c>InMemorySearchHistory</c>) keeps the LRU list in
    /// process memory; the persistent adapter wraps
    /// <c>Windows.Storage.ApplicationData.Current.LocalSettings</c> and lives
    /// in <c>Vianigram.Storage</c> (or the App composition root) where the
    /// WinRT dependency is acceptable.
    ///
    /// <para><b>Behavior</b>: LRU with a fixed capacity (V1 = 50 entries).
    /// <see cref="RecordAsync"/> moves an existing entry to the front;
    /// inserting past capacity evicts the oldest entry. Equality is by
    /// <c>SearchQuery.Text</c> ordinal (case-sensitive — matches Telegram
    /// server behavior).</para>
    ///
    /// <para>Implementations MUST be thread-safe; the application layer issues
    /// concurrent reads while a single record is in flight.</para>
    /// </summary>
    public interface ISearchHistory
    {
        /// <summary>Maximum number of entries the implementation will retain.</summary>
        int Capacity { get; }

        /// <summary>Record a query as the most-recent entry. Idempotent / no-op for empty queries.</summary>
        Task<Result<Unit, SearchError>> RecordAsync(SearchQuery query, CancellationToken ct);

        /// <summary>Most-recent first. Returns at most <see cref="Capacity"/> entries.</summary>
        Task<Result<IList<SearchQuery>, SearchError>> GetRecentAsync(CancellationToken ct);

        /// <summary>Drop every stored entry.</summary>
        Task<Result<Unit, SearchError>> ClearAsync(CancellationToken ct);
    }
}
