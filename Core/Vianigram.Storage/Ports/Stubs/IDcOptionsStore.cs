// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// Persistent ledger of <c>dcOption</c> entries learnt from
    /// <c>help.getConfig</c>. The hardcoded bootstrap plan in
    /// <c>TelegramDcOptions</c> ships with only the canonical IPs known
    /// at build time; once we've handshake-opened any DC, we ask
    /// <c>help.getConfig</c> for the live list and persist it here so
    /// the NEXT cold start has multiple IPs per DC. This is critical
    /// for users whose ISP blocks the single hardcoded IP — a different
    /// IP for the same DC may be reachable.
    ///
    /// All-or-nothing replace semantics: <see cref="ReplaceAllAsync"/>
    /// takes the full set returned by the server and replaces the
    /// persisted set in one transaction. There is no partial update;
    /// the server's view is the source of truth.
    /// </summary>
    public interface IDcOptionsStore
    {
        Task<List<DcOptionRecord>> LoadAllAsync(CancellationToken ct);
        Task ReplaceAllAsync(IReadOnlyList<DcOptionRecord> records, CancellationToken ct);
        Task ClearAsync(CancellationToken ct);
    }
}
