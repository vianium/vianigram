// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Search.Application.Handlers;
using Vianigram.Search.Application.UseCases;
using Vianigram.Search.Domain;
using Vianigram.Search.Domain.Entities;
using Vianigram.Search.Domain.Events;
using Vianigram.Search.Domain.ValueObjects;
using Vianigram.Search.Ports.Inbound;
using Vianigram.Search.Ports.Outbound;

namespace Vianigram.Search.Application
{
    /// <summary>
    /// <see cref="ISearchApi"/> implementation. Dispatches each public method
    /// to the matching handler, surfaces results as
    /// <c>Result&lt;T, SearchError&gt;</c>, and re-broadcasts the
    /// <see cref="ResultsArrived"/> domain event on the kernel bus into a CLR
    /// event so XAML / UI consumers don't need an <see cref="IEventBus"/>
    /// dependency.
    ///
    /// All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="SearchError"/>.
    /// </summary>
    public sealed class SearchApplication : ISearchApi, IDisposable
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISearchHistory _history;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        private readonly GlobalSearchHandler _global;
        private readonly SearchInChatHandler _inChat;
        private readonly SearchUsersHandler _users;
        private readonly LoadMoreHandler _loadMore;
        private readonly CancelSearchHandler _cancel;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<SearchResultsEventArgs> ResultsArrived;

        public SearchApplication(
            IMtProtoRpcPort rpc,
            ISearchHistory history,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (history == null) throw new ArgumentNullException("history");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _rpc = rpc;
            _history = history;
            _bus = bus;
            _log = new TimestampedLogger(logger, "Search.Application");
            _clock = clock;

            _global = new GlobalSearchHandler(rpc, history, bus, logger, clock);
            _inChat = new SearchInChatHandler(rpc, history, bus, logger, clock);
            _users = new SearchUsersHandler(rpc, history, bus, logger, clock);
            _loadMore = new LoadMoreHandler(rpc, bus, logger, clock);
            _cancel = new CancelSearchHandler(bus, logger, clock);

            _subs = new IDisposable[]
            {
                bus.Subscribe<Domain.Events.ResultsArrived>(OnResultsArrived)
            };
        }

        public async Task<Result<SearchSession, SearchError>> SearchGlobalAsync(string query, SearchFilter filter, CancellationToken ct)
        {
            try
            {
                return await _global.HandleAsync(new GlobalSearchCommand(query, filter), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("SearchGlobalAsync failed", ex));
            }
        }

        public async Task<Result<SearchSession, SearchError>> SearchInChatAsync(string peerKey, string query, SearchFilter filter, CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(peerKey))
                    return Result<SearchSession, SearchError>.Fail(SearchError.InvalidValue("peerKey required"));
                return await _inChat.HandleAsync(new SearchInChatCommand(peerKey, query, filter), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("SearchInChatAsync failed", ex));
            }
        }

        public async Task<Result<SearchSession, SearchError>> SearchUsersAsync(string query, CancellationToken ct)
        {
            try
            {
                return await _users.HandleAsync(new SearchUsersCommand(query), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("SearchUsersAsync failed", ex));
            }
        }

        public async Task<Result<SearchSession, SearchError>> LoadMoreAsync(SearchSession session, CancellationToken ct)
        {
            try
            {
                if (session == null)
                    return Result<SearchSession, SearchError>.Fail(SearchError.InvalidValue("session required"));
                return await _loadMore.HandleAsync(new LoadMoreCommand(session), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("LoadMoreAsync failed", ex));
            }
        }

        public async Task<Result<Unit, SearchError>> CancelAsync(SearchSession session, CancellationToken ct)
        {
            try
            {
                if (session == null)
                    return Result<Unit, SearchError>.Fail(SearchError.InvalidValue("session required"));
                return await _cancel.HandleAsync(new CancelSearchCommand(session), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, SearchError>.Fail(SearchError.Unknown("CancelAsync failed", ex));
            }
        }

        // ---- bus -> CLR-event bridge -----------------------------------------

        private void OnResultsArrived(Domain.Events.ResultsArrived e)
        {
            var h = ResultsArrived;
            if (h == null) return;
            try { h(this, new SearchResultsEventArgs(e.SessionId, e.Page, e.LoadedCount, e.TotalCount, e.At)); }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }
    }
}
