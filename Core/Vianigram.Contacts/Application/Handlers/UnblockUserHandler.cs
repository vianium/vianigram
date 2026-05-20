// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Application.UseCases;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Domain.ValueObjects;
using Vianigram.Contacts.Infrastructure;
using Vianigram.Contacts.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;

namespace Vianigram.Contacts.Application.Handlers
{
    /// <summary>
    /// Issues <c>contacts.unblock#b550d328</c> and mirrors the unblock locally.
    /// Symmetric to <see cref="BlockUserHandler"/>.
    /// </summary>
    internal sealed class UnblockUserHandler
    {
        private readonly IContactRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public UnblockUserHandler(IContactRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Contacts.UnblockUser");
            _clock = clock;
        }

        public async Task<Result<Unit, ContactsError>> HandleAsync(UnblockUserCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, ContactsError>.Fail(ContactsError.Unknown("null command"));

            ContactBook book = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeUnblock(cmd.Target.Value, cmd.AccessHash, /*myStoriesFrom*/ false);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("contacts.unblock failed: " + mapped);
                return Result<Unit, ContactsError>.Fail(mapped);
            }

            book.Unblock(cmd.Target, _clock.UtcNow);
            await _repo.SaveAsync(book, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(book, _bus);
            return Result<Unit, ContactsError>.Ok(Unit.Value);
        }
    }
}
