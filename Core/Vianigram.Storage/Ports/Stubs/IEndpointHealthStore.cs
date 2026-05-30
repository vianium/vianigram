// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// Persistent reachability ledger for MTProto endpoints. Replaces the
    /// per-process static dictionary that TelegramDcOptions used to keep:
    /// the static one started empty on every cold launch, so a network
    /// that consistently blocks (say) `149.154.175.50:443` would have
    /// that endpoint re-discovered as dead every time. With this store,
    /// the cooldown survives launches and the staggered race naturally
    /// skips it until the cooldown expires.
    ///
    /// Implementations must:
    ///   - Be safe to call from any thread (SqliteDatabase has its own
    ///     gate; the store should not require external locking).
    ///   - Be cheap on the hot path. `LoadAllAsync` runs once at
    ///     composition; `UpsertAsync` runs once per success/failure and
    ///     is fire-and-forget from the caller's perspective.
    ///   - Apply their own TTL pruning (default: 7 days). Networks
    ///     change; ancient cooldowns become stale.
    /// </summary>
    public interface IEndpointHealthStore
    {
        Task<List<EndpointHealthRecord>> LoadAllAsync(CancellationToken ct);
        Task UpsertAsync(EndpointHealthRecord record, CancellationToken ct);
        Task PruneOlderThanAsync(System.DateTime cutoffUtc, CancellationToken ct);
    }
}
