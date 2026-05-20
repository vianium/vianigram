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
    /// Handles <see cref="SearchUsersCommand"/>: issues
    /// <c>contacts.search#11f812d8</c>, decodes the response into a list of
    /// <c>SearchHit(User|Chat|Channel)</c>, records the page on a fresh
    /// <see cref="SearchSession"/> (single-page session — Telegram does not
    /// page <c>contacts.search</c>; the entire result fits in <c>limit</c>),
    /// drains events, and returns.
    /// </summary>
    internal sealed class SearchUsersHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISearchHistory _history;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SearchUsersHandler(
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
            _log = new TimestampedLogger(log, "Search.SearchUsers");
            _clock = clock;
        }

        public async Task<Result<SearchSession, SearchError>> HandleAsync(SearchUsersCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("null command"));
            if (string.IsNullOrEmpty(cmd.Query))
                return Result<SearchSession, SearchError>.Fail(SearchError.QueryTooShort("contacts.search requires query"));

            var query = new SearchQuery(cmd.Query, null, SearchFilter.All);
            DateTime now = _clock.UtcNow;
            var cursor = SearchCursor.FirstPage(cmd.Limit);
            var session = new SearchSession(query, cursor, now);
            session.BeginInitialLoad(now);

            byte[] request = TlEncoder.EncodeContactsSearch(query.Text, cmd.Limit);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("contacts.search rpc failed: " + mapped);
                session.Fail(mapped, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
                return Result<SearchSession, SearchError>.Fail(mapped);
            }

            try
            {
                var hits = TlDecoder.DecodeContactsFound(rpcResult.Value);
                // contacts.search has no continuation cursor — record the
                // page and let RecordPage transition to Completed because
                // hits.Count >= TotalCount (we set TotalCount = hits.Count).
                int total = hits.Count;
                session.RecordPage(hits, cursor, total, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
            }
            catch (Exception ex)
            {
                var err = SearchError.Unknown("contacts.search decode failed", ex);
                _log.Warn("contacts.search decode failed: " + ex.Message);
                session.Fail(err, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
                return Result<SearchSession, SearchError>.Fail(err);
            }

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
