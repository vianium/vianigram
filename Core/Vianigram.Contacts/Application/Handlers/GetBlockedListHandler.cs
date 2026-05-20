// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Application.UseCases;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Infrastructure;
using Vianigram.Contacts.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;

namespace Vianigram.Contacts.Application.Handlers
{
    /// <summary>
    /// Issues <c>contacts.getBlocked#9a868f80</c>, decodes the response, and
    /// reconciles the blocked sub-set on the local <see cref="ContactBook"/>.
    /// V1 fetches a single page; future iterations will paginate via offset.
    /// </summary>
    internal sealed class GetBlockedListHandler
    {
        private readonly IContactRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public GetBlockedListHandler(IContactRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Contacts.GetBlockedList");
            _clock = clock;
        }

        public async Task<Result<IList<long>, ContactsError>> HandleAsync(GetBlockedListCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<long>, ContactsError>.Fail(ContactsError.Unknown("null command"));

            ContactBook book = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeGetBlocked(cmd.Offset, cmd.Limit, cmd.MyStoriesFrom);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("contacts.getBlocked failed: " + mapped);
                return Result<IList<long>, ContactsError>.Fail(mapped);
            }

            TlDecoder.DecodedBlocked decoded;
            try
            {
                decoded = TlDecoder.DecodeBlocked(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<IList<long>, ContactsError>.Fail(ContactsError.Unknown("getBlocked decode failed", ex));
            }

            // Only reconcile when fetching the first page; otherwise we'd over-prune.
            if (cmd.Offset == 0)
            {
                book.ApplyBlockedSync(decoded.BlockedUserIds, _clock.UtcNow);
                await _repo.SaveAsync(book, ct).ConfigureAwait(false);
                HandlerEventBridge.Drain(book, _bus);
            }

            return Result<IList<long>, ContactsError>.Ok(decoded.BlockedUserIds);
        }
    }
}
