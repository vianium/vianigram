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
    /// Handles <see cref="LoadMoreCommand"/>: continues a paged session with
    /// its current cursor.
    ///
    /// <para><b>Routing</b>: the handler picks the RPC based on
    /// <c>session.Query.IsScopedToPeer</c> — peer-scoped sessions go through
    /// <c>messages.search</c>, unscoped sessions through
    /// <c>messages.searchGlobal</c>. <c>contacts.search</c> sessions never
    /// reach this handler because they're already <c>Completed</c> after the
    /// first page.</para>
    /// </summary>
    internal sealed class LoadMoreHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public LoadMoreHandler(
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Search.LoadMore");
            _clock = clock;
        }

        public async Task<Result<SearchSession, SearchError>> HandleAsync(LoadMoreCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<SearchSession, SearchError>.Fail(SearchError.Unknown("null command"));
            var session = cmd.Session;
            if (session.IsTerminal)
            {
                // Idempotent — nothing more to load.
                return Result<SearchSession, SearchError>.Ok(session);
            }
            if (session.State != SessionState.PageLoaded)
            {
                return Result<SearchSession, SearchError>.Fail(
                    SearchError.InvalidValue("LoadMore requires PageLoaded (was " + session.State + ")"));
            }

            DateTime now = _clock.UtcNow;
            session.BeginLoadMore(now);

            byte[] request;
            if (session.Query.IsScopedToPeer)
            {
                request = TlEncoder.EncodeSearchInChat(session.Query.PeerKey, session.Query.Text, session.Query.Filter, session.Cursor);
            }
            else
            {
                request = TlEncoder.EncodeSearchGlobal(session.Query.Text, session.Query.Filter, session.Cursor);
            }

            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("LoadMore rpc failed: " + mapped);
                session.Fail(mapped, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
                return Result<SearchSession, SearchError>.Fail(mapped);
            }

            try
            {
                var page = TlDecoder.DecodeMessagesPage(rpcResult.Value);
                var nextCursor = TlDecoder.AdvanceCursor(session.Cursor, page);
                session.RecordPage(page.Hits, nextCursor, page.TotalCount, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
                return Result<SearchSession, SearchError>.Ok(session);
            }
            catch (Exception ex)
            {
                var err = SearchError.Unknown("LoadMore decode failed", ex);
                _log.Warn("LoadMore decode failed: " + ex.Message);
                session.Fail(err, _clock.UtcNow);
                HandlerEventBridge.Drain(session, _bus);
                return Result<SearchSession, SearchError>.Fail(err);
            }
        }
    }
}
