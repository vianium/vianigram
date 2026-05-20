// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Search.Domain;
using Vianigram.Search.Domain.Entities;
using Vianigram.Search.Domain.ValueObjects;

namespace Vianigram.Search.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Search bounded context (V1 shape). Every method
    /// is async, takes a <see cref="CancellationToken"/>, and returns
    /// <c>Result&lt;T, SearchError&gt;</c>; no exceptions cross this boundary.
    ///
    /// <para><b>Surface</b>: three search modalities (global / in-chat / users)
    /// and two session operations (load-more / cancel). Each search call
    /// returns a fresh <see cref="SearchSession"/>; the caller passes that
    /// session back to <see cref="LoadMoreAsync"/> / <see cref="CancelAsync"/>.
    /// </para>
    ///
    /// <para><b>CLR-event surface</b>: <see cref="ResultsArrived"/> mirrors the
    /// internal <c>ResultsArrived</c> domain event so XAML / UI consumers can
    /// subscribe without taking an <c>IEventBus</c> dependency.</para>
    ///
    /// Cross-context callers wrap this API behind ACL adapters defined in
    /// <c>Vianigram.Composition</c> (one adapter per consuming context — see
    /// <c>docs/managed-architecture/12-search.md §9</c>).
    /// </summary>
    public interface ISearchApi
    {
        /// <summary>
        /// Issue <c>messages.searchGlobal#4bc6589a</c> with the supplied query
        /// and filter. Returns a fresh <see cref="SearchSession"/> in
        /// <c>PageLoaded</c> (or <c>Completed</c> when only one page exists).
        /// </summary>
        Task<Result<SearchSession, SearchError>> SearchGlobalAsync(string query, SearchFilter filter, CancellationToken ct);

        /// <summary>
        /// Issue <c>messages.search#a0fda762</c> against a single peer (chat /
        /// channel / user). <paramref name="peerKey"/> uses the same opaque
        /// shape as the rest of the context (e.g. <c>"user:42"</c>,
        /// <c>"chat:7"</c>, <c>"channel:1001"</c>).
        /// </summary>
        Task<Result<SearchSession, SearchError>> SearchInChatAsync(string peerKey, string query, SearchFilter filter, CancellationToken ct);

        /// <summary>
        /// Issue <c>contacts.search#11f812d8</c>. Note: the same RPC is owned
        /// by the Contacts bounded context, but the Search context exposes it
        /// for the global "@username" search bar — the result list is a
        /// distinct discovery scenario from the contact list.
        /// </summary>
        Task<Result<SearchSession, SearchError>> SearchUsersAsync(string query, CancellationToken ct);

        /// <summary>
        /// Continue a paged session with its current cursor. Returns the same
        /// session reference with new hits appended and the cursor advanced.
        /// Calling on a terminal session is a no-op success.
        /// </summary>
        Task<Result<SearchSession, SearchError>> LoadMoreAsync(SearchSession session, CancellationToken ct);

        /// <summary>
        /// Cancel an in-flight session. Idempotent — cancelling a finished
        /// session returns success. The next pending RPC, if any, will be
        /// short-circuited and the session's <c>State</c> set to
        /// <c>Cancelled</c>.
        /// </summary>
        Task<Result<Unit, SearchError>> CancelAsync(SearchSession session, CancellationToken ct);

        /// <summary>
        /// CLR event raised whenever a page of results lands on a session.
        /// Mirrors the <c>ResultsArrived</c> domain event in CLR-event shape so
        /// XAML / UI layers don't take an <c>IEventBus</c> dependency.
        /// Multicast, thread-safe add/remove.
        /// </summary>
        event EventHandler<SearchResultsEventArgs> ResultsArrived;
    }
}
