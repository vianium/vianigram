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
    /// Handles <see cref="SearchInChatCommand"/>: validates query and peer
    /// key, builds a fresh <see cref="SearchSession"/> bound to the peer,
    /// issues <c>messages.search#a0fda762</c> via
    /// <see cref="IMtProtoRpcPort"/>, records the page on the aggregate,
    /// drains domain events, and returns the session.
    ///
    /// Telegram requires query length &gt;= 1 for in-chat search; the V1 V0
    /// behavior matches that lower bound (the doc-spec'd 2-char minimum is
    /// enforced at the UI layer).
    /// </summary>
    internal sealed class SearchInChatHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly ISearchHistory _history;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SearchInChatHandler(
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
            _log = new TimestampedLogger(log, "Search.SearchInChat");
            _clock = clock;
        }

        public async Task<Result<SearchSession, SearchError>> HandleAsync(SearchInChatCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("null command"));
            if (string.IsNullOrEmpty(cmd.PeerKey))
                return Result<SearchSession, SearchError>.Fail(SearchError.InvalidValue("peerKey required"));

            var query = new SearchQuery(cmd.Query, cmd.PeerKey, cmd.Filter);
            // Allow empty-text queries when a filter is applied (e.g. "all
            // photos in this chat") — Telegram returns the unfiltered
            // peer's recent media. Reject only when both are empty.
            if (string.IsNullOrEmpty(query.Text) && cmd.Filter == SearchFilter.All)
                return Result<SearchSession, SearchError>.Fail(SearchError.QueryTooShort("in-chat search requires query or filter"));

            DateTime now = _clock.UtcNow;
            var cursor = SearchCursor.FirstPage(cmd.PageSize);
            var session = new SearchSession(query, cursor, now);
            session.BeginInitialLoad(now);

            byte[] request = TlEncoder.EncodeSearchInChat(cmd.PeerKey, query.Text, query.Filter, cursor);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.search rpc failed: " + mapped);
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
                var err = SearchError.Unknown("messages.search decode failed", ex);
                _log.Warn("messages.search decode failed: " + ex.Message);
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
