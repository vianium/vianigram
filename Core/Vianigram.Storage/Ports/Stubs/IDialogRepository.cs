// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// STUB of <c>Vianigram.Chats.Ports.Outbound.IDialogRepository</c>.
    /// Migration: replace with a ProjectReference to <c>Vianigram.Chats</c>
    /// once that context publishes the real port.
    /// </summary>
    public interface IDialogRepository
    {
        Task<IList<DialogRecord>> ListAsync(CancellationToken ct);
        Task<DialogRecord> GetAsync(long peerId, CancellationToken ct);
        Task UpsertAsync(DialogRecord dialog, CancellationToken ct);
        Task DeleteAsync(long peerId, CancellationToken ct);
        Task ClearAsync(CancellationToken ct);
    }
}
