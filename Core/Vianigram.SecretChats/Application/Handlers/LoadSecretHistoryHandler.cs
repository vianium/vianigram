// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LoadSecretHistoryHandler.cs - Vianigram.SecretChats.Application.Handlers
// Handler for ISecretChatsApi.LoadHistoryAsync.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.SecretChats.Application.UseCases;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Application.Handlers
{
    /// <summary>
    /// Read a page of secret-chat history from the local repository. Secret
    /// ciphertext is never server-stored, so this method is pure local I/O —
    /// no MTProto round-trip. Messages are ordered oldest-first by
    /// <see cref="SecretMessage.SentAt"/>; ties broken by RandomId.
    ///
    /// <para><paramref name="OffsetMsgId"/> is the <c>RandomId</c> of the
    /// message the caller already has — when non-null, the handler returns
    /// the page of messages strictly older than that anchor (relative to
    /// the natural ordering above). Null means "from the start".</para>
    ///
    /// <para><see cref="SecretMessagePage.HasMoreOlder"/> is true when the
    /// returned page exhausts the limit and additional rows remain in the
    /// session below the page's tail. The in-memory store keeps the whole
    /// conversation in RAM; a future encrypted-SQLite repository will reuse
    /// the same shape.</para>
    /// </summary>
    internal sealed class LoadSecretHistoryHandler
    {
        private readonly ISecretChatRepository _repo;
        private readonly IComponentLogger _log;

        public LoadSecretHistoryHandler(ISecretChatRepository repo, ILogger log)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (log == null) throw new ArgumentNullException("log");
            _repo = repo;
            _log = new TimestampedLogger(log, "SecretChats.LoadHistory");
        }

        public async Task<Result<SecretMessagePage, SecretChatError>> HandleAsync(LoadSecretHistoryCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return Result<SecretMessagePage, SecretChatError>.Fail(SecretChatError.Unknown("null command"));
            if (cmd.Limit <= 0)
                return Result<SecretMessagePage, SecretChatError>.Ok(new SecretMessagePage(new SecretMessage[0], false, null));

            SecretSession session = await _repo.FindAsync(cmd.ChatId, ct).ConfigureAwait(false);
            if (session == null)
                return Result<SecretMessagePage, SecretChatError>.Fail(SecretChatError.ChatNotFound(cmd.ChatId.ToString()));

            IList<SecretMessage> snapshot = session.SnapshotMessages();
            // Sort oldest-first by SentAt; tie-break by RandomId for stability.
            var ordered = new List<SecretMessage>(snapshot);
            ordered.Sort(CompareOldestFirst);

            int startIndex = 0;
            if (cmd.OffsetMsgId.HasValue)
            {
                long anchor = cmd.OffsetMsgId.Value;
                // Find the anchor and start AFTER it (older rows for our purposes
                // are above; "next page" = the rows newer than the anchor).
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (ordered[i].RandomId == anchor)
                    {
                        startIndex = i + 1;
                        break;
                    }
                }
            }

            int available = ordered.Count - startIndex;
            if (available < 0) available = 0;
            int pageSize = available < cmd.Limit ? available : cmd.Limit;
            var page = new SecretMessage[pageSize];
            for (int i = 0; i < pageSize; i++) page[i] = ordered[startIndex + i];

            bool hasMore = (startIndex + pageSize) < ordered.Count;
            long? oldestRandomId = pageSize > 0 ? (long?)page[0].RandomId : null;

            _log.Debug("history page: chatId=" + cmd.ChatId + " count=" + pageSize +
                       " hasMore=" + hasMore + " offset=" + (cmd.OffsetMsgId.HasValue ? cmd.OffsetMsgId.Value.ToString() : "<null>"));

            return Result<SecretMessagePage, SecretChatError>.Ok(new SecretMessagePage(page, hasMore, oldestRandomId));
        }

        private static int CompareOldestFirst(SecretMessage a, SecretMessage b)
        {
            int c = a.SentAt.CompareTo(b.SentAt);
            if (c != 0) return c;
            return a.RandomId.CompareTo(b.RandomId);
        }
    }
}
