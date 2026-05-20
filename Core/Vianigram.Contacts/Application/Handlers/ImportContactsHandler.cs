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
    /// Issues <c>contacts.importContacts#2c800be5</c> with the supplied phone
    /// rows and reconciles the resulting matched users with the local book.
    ///
    /// V1 behavior: send the entire request set in a single RPC call. The
    /// docs/managed-architecture/04-contacts.md §10 calls for chunking at 100
    /// rows with a 250 ms gap between calls — we deliberately leave that
    /// pacing to the host orchestration layer (<c>Vianigram.Composition</c>),
    /// which is the right place to decide on telemetry windows / cooperative
    /// throttling. This keeps the bounded context handler simple and
    /// composable.
    /// </summary>
    internal sealed class ImportContactsHandler
    {
        private readonly IContactRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ImportContactsHandler(IContactRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Contacts.ImportContacts");
            _clock = clock;
        }

        public async Task<Result<IList<Contact>, ContactsError>> HandleAsync(ImportContactsCommand cmd, CancellationToken ct)
        {
            if (cmd == null || cmd.Requests == null)
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("null command"));
            if (cmd.Requests.Count == 0)
                return Result<IList<Contact>, ContactsError>.Ok(new Contact[0]);

            ContactBook book = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeImportContacts(cmd.Requests);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("contacts.importContacts failed: " + mapped);
                return Result<IList<Contact>, ContactsError>.Fail(mapped);
            }

            TlDecoder.DecodedImportedContacts decoded;
            try
            {
                decoded = TlDecoder.DecodeImportedContacts(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("importContacts decode failed", ex));
            }

            DateTime now = _clock.UtcNow;
            for (int i = 0; i < decoded.Users.Count; i++)
            {
                book.AddOrUpdate(decoded.Users[i], now);
            }
            await _repo.SaveAsync(book, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(book, _bus);

            return Result<IList<Contact>, ContactsError>.Ok(decoded.Users);
        }
    }
}
