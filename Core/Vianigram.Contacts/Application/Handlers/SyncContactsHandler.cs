// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Contacts.Application.UseCases;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Domain.Events;
using Vianigram.Contacts.Infrastructure;
using Vianigram.Contacts.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;

namespace Vianigram.Contacts.Application.Handlers
{
    /// <summary>
    /// Issues <c>contacts.getContacts#5dd69e12</c>, decodes the response, and
    /// reconciles the resulting <see cref="Contact"/> list with the local
    /// <see cref="ContactBook"/> aggregate. Persists, then drains and publishes
    /// the staged domain events.
    ///
    /// Errors:
    ///   - Network / cancellation -&gt; <see cref="ContactsError.NetworkError"/>.
    ///   - TL decode errors        -&gt; <see cref="ContactsError.Unknown"/> with cause.
    ///   - Server <c>contactsNotModified</c> -&gt; success with the current Snapshot.
    ///   - The handler does NOT throw across the public boundary (cancellation
    ///     bubbles up as <see cref="OperationCanceledException"/>, which the
    ///     <see cref="ContactsApplication"/> catches and re-throws).
    /// </summary>
    internal sealed class SyncContactsHandler
    {
        private readonly IContactRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SyncContactsHandler(IContactRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Contacts.SyncContacts");
            _clock = clock;
        }

        public async Task<Result<IList<Contact>, ContactsError>> HandleAsync(SyncContactsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("null command"));

            ContactBook book = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeGetContacts(cmd.Hash);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("contacts.getContacts failed: " + mapped);
                return Result<IList<Contact>, ContactsError>.Fail(mapped);
            }

            TlDecoder.DecodedContacts decoded;
            try
            {
                decoded = TlDecoder.DecodeContacts(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("getContacts decode failed", ex));
            }

            DateTime now = _clock.UtcNow;
            if (decoded.NotModified)
            {
                // Server says nothing changed since the last hash — return the cached snapshot.
                return Result<IList<Contact>, ContactsError>.Ok(book.Snapshot());
            }

            book.ApplyServerSync(decoded.Contacts, now);
            await _repo.SaveAsync(book, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(book, _bus);

            return Result<IList<Contact>, ContactsError>.Ok(book.Snapshot());
        }
    }
}
