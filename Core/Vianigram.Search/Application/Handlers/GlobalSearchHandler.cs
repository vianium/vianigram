// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Search.Application.UseCases;
using Vianigram.Search.Domain;
using Vianigram.Search.Domain.Entities;
using Vianigram.Search.Domain.ValueObjects;
using Vianigram.Search.Infrastructure;
using Vianigram.Search.Ports.Outbound;

namespace Vianigram.Search.Application.Handlers
{
    /// <summary>
    /// Handles <see cref="GlobalSearchCommand"/>: validates the query, builds
    /// a fresh <see cref="SearchSession"/>, issues
    /// <c>messages.searchGlobal#4bc6589a</c> via <see cref="IMtProtoRpcPort"/>,
    /// records the page on the aggregate, drains domain events, and returns
    /// the session.
    ///
    /// Errors are surfaced as <see cref="SearchError"/>; no exceptions cross
    /// the boundary. <c>FLOOD_WAIT</c> is mapped via <see cref="RpcErrorMapper"/>.
    /// </summary>
    internal sealed class GlobalSearchHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISearchHistory _history;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public GlobalSearchHandler(
            IMtProtoRpcPort rpc,
            ISearchHistory history,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (history == null) throw new ArgumentNullException("history");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _rpc = rpc;
            _history = history;
            _bus = bus;
            _log = new TimestampedLogger(log, "Search.GlobalSearch");
            _clock = clock;
        }

        public async Task<Result<SearchSession, SearchError>> HandleAsync(GlobalSearchCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("null command"));

            var query = new SearchQuery(cmd.Query, null, cmd.Filter);
            if (string.IsNullOrEmpty(query.Text))
                return Result<SearchSession, SearchError>.Fail(SearchError.QueryTooShort("global search requires query"));

            DateTime now = _clock.UtcNow;
            var cursor = SearchCursor.FirstPage(cmd.PageSize);
            var session = new SearchSession(query, cursor, now);
            session.BeginInitialLoad(now);

            byte[] request = TlEncoder.EncodeSearchGlobal(query.Text, query.Filter, cursor);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("searchGlobal rpc failed: " + mapped);
                session.Fail(mapped, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
                return Result<SearchSession, SearchError>.Fail(mapped);
            }

            try
            {
                var page = TlDecoder.DecodeMessagesPage(rpcResult.Value);
                var nextCursor = TlDecoder.AdvanceCursor(cursor, page);
                session.RecordPage(page.Hits, nextCursor, page.TotalCount, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
            }
            catch (Exception ex)
            {
                var err = SearchError.Unknown("searchGlobal decode failed", ex);
                _log.Warn("searchGlobal decode failed: " + ex.Message);
                session.Fail(err, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
                return Result<SearchSession, SearchError>.Fail(err);
            }

            // Best-effort: record query in history; failures are non-fatal.
            try
            {
                var rec = await _history.RecordAsync(query, ct).ConfigureAwait(false);
                if (rec.IsFail)
                {
                    _log.Info("history record failed: " + rec.Error);
                }
            }
            catch (Exception ex)
            {
                _log.Info("history record threw: " + ex.Message);
            }

            return Result<SearchSession, SearchError>.Ok(session);
        }
    }
}
