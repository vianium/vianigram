// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// STUB of <c>Vianigram.Sync.Ports.Outbound.ISyncStateRepository</c>.
    /// </summary>
    public interface ISyncStateRepository
    {
        Task<SyncStateRecord> LoadAsync(CancellationToken ct);
        Task SaveAsync(SyncStateRecord state, CancellationToken ct);
        Task ClearAsync(CancellationToken ct);
    }
}
