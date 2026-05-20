// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.Entities;
using Vianigram.Messages.Domain.ValueObjects;

namespace Vianigram.Messages.Ports.Outbound
{
    /// <summary>
    /// Storage port for the Messages context. Implementations may be in-memory
    /// or SQLite-backed. The aggregate root sits behind this port so handlers
    /// never touch persistence directly.
    /// </summary>
    public interface IMessageRepository
    {
        /// <summary>
        /// Get the in-memory aggregate root for the peer, creating an empty one
        /// if none exists yet. Always succeeds.
        /// </summary>
        MessageStream GetOrCreateStream(string peerKey);

        /// <summary>
        /// Look up a stream without creating it. Returns null if not present.
        /// </summary>
        MessageStream FindStream(string peerKey);

        /// <summary>
        /// Persist a single message — used both for incoming messages and for
        /// the resulting confirmed-state of optimistic sends.
        /// </summary>
        Task<Result<Unit, MessageError>> UpsertMessageAsync(string peerKey, Message message, CancellationToken ct);

        /// <summary>
        /// Persist a batch of messages, e.g. a history page. Implementations
        /// should make this transactional where the storage backend supports it.
        /// </summary>
        Task<Result<Unit, MessageError>> UpsertMessagesAsync(string peerKey, IList<Message> messages, CancellationToken ct);

        /// <summary>
        /// Get the in-memory snapshot of messages for the peer, sorted newest-first
        /// up to <paramref name="limit"/>, optionally older than <paramref name="offsetMsgId"/>.
        /// </summary>
        Task<Result<IList<Message>, MessageError>> ListMessagesAsync(string peerKey, long? offsetMsgId, int limit, CancellationToken ct);
    }
}
