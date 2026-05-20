// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Chats.Application.Commands;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.Events;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Infrastructure;
using Vianigram.Chats.Ports.Outbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Result;

namespace Vianigram.Chats.Application.Handlers
{
    /// <summary>
    /// Issues messages.getDialogs against the configured RPC port, decodes the response,
    /// upserts the resulting <see cref="Dialog"/> aggregates into the repository, and
    /// publishes <see cref="DialogListSynced"/> on the event bus.
    ///
    /// Errors:
    ///   - Network / cancellation -&gt; <see cref="ChatError.NetworkError"/>.
    ///   - TL decode errors        -&gt; <see cref="ChatError.Unknown"/> with the cause attached.
    ///   - The handler does NOT throw across the public boundary.
    /// </summary>
    public sealed class LoadDialogListHandler
    {
        private readonly IDialogRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;

        public LoadDialogListHandler(IDialogRepository repo, IMtProtoRpcPort rpc, IEventBus bus)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
        }

        public async Task<Result<DialogPage, ChatError>> HandleAsync(LoadDialogListCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<DialogPage, ChatError>.Fail(ChatError.Unknown("null command"));

            byte[] response;
            try
            {
                byte[] payload = TlEncoder.EncodeGetDialogs(cmd.Limit, cmd.Cursor, /*folderId*/ null, /*hash*/ 0L);
                response = await _rpc.CallAsync(payload, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw; // propagate cancellation; not a domain error
            }
            catch (Exception ex)
            {
                return Result<DialogPage, ChatError>.Fail(ChatError.NetworkError("getDialogs RPC failed", ex));
            }

            TlDecoder.DecodedDialogList decoded;
            try
            {
                decoded = TlDecoder.DecodeGetDialogsResponse(response);
            }
            catch (Exception ex)
            {
                return Result<DialogPage, ChatError>.Fail(ChatError.Unknown("getDialogs decode failed", ex));
            }

            if (decoded.NotModified)
            {
                // Treat as "page is fresh, nothing to upsert" — return empty page so callers
                // can render from cache.
                return Result<DialogPage, ChatError>.Ok(DialogPage.Empty);
            }

            try
            {
                await _repo.UpsertManyAsync(decoded.Dialogs, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<DialogPage, ChatError>.Fail(ChatError.Unknown("repository upsert failed", ex));
            }

            var previews = new List<DialogPreview>(decoded.Dialogs.Count);
            for (int i = 0; i < decoded.Dialogs.Count; i++)
            {
                previews.Add(decoded.Dialogs[i].ToPreview(string.Empty));
            }

            DateTime now = DateTime.UtcNow;
            _bus.Publish(new DialogListSynced(decoded.Dialogs.Count, now));

            var page = new DialogPage(previews, decoded.NextCursor, decoded.HasMore);
            return Result<DialogPage, ChatError>.Ok(page);
        }
    }
}
