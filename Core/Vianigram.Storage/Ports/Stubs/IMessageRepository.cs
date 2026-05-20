// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// STUB of <c>Vianigram.Messages.Ports.Outbound.IMessageRepository</c>.
    /// </summary>
    public interface IMessageRepository
    {
        /// <summary>
        /// Returns up to <paramref name="limit"/> messages for a peer, in
        /// descending message-id order, optionally before <paramref name="beforeMessageId"/>.
        /// </summary>
        Task<IList<MessageRecord>> ListAsync(long peerId, int beforeMessageId, int limit, CancellationToken ct);

        Task<MessageRecord> GetAsync(long peerId, int messageId, CancellationToken ct);

        Task UpsertAsync(MessageRecord message, CancellationToken ct);

        Task DeleteAsync(long peerId, int messageId, CancellationToken ct);

        Task DeleteByPeerAsync(long peerId, CancellationToken ct);

        Task ClearAsync(CancellationToken ct);
    }
}
