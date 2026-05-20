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
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;

namespace Vianigram.Contacts.Application.Handlers
{
    /// <summary>
    /// Issues <c>contacts.search#11f812d8</c>. Search results are NOT folded
    /// into the local contact book — they are merely visible users (e.g. the
    /// person typed <c>@foo</c> who is not in the user's contacts). Hits that
    /// happen to already be in the book retain their saved-contact status; new
    /// hits are returned to the caller as-is for "Add contact" UX.
    /// </summary>
    internal sealed class SearchContactsHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;

        public SearchContactsHandler(IMtProtoRpcPort rpc, ILogger log)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Contacts.SearchContacts");
        }

        public async Task<Result<IList<Contact>, ContactsError>> HandleAsync(SearchContactsCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("null command"));
            if (string.IsNullOrEmpty(cmd.Query))
                return Result<IList<Contact>, ContactsError>.Ok(new Contact[0]);

            byte[] request = TlEncoder.EncodeSearch(cmd.Query, cmd.Limit);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("contacts.search failed: " + mapped);
                return Result<IList<Contact>, ContactsError>.Fail(mapped);
            }

            try
            {
                var decoded = TlDecoder.DecodeFound(rpcResult.Value);
                return Result<IList<Contact>, ContactsError>.Ok(decoded.Users);
            }
            catch (Exception ex)
            {
                return Result<IList<Contact>, ContactsError>.Fail(ContactsError.Unknown("search decode failed", ex));
            }
        }
    }
}
