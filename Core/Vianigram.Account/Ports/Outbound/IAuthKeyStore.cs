// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Account.Ports.Outbound
{
    /// <summary>
    /// Outbound port for persisting per-DC auth_key material. Implementations
    /// MUST encrypt at rest (DataProtectionProvider scope = LOCAL=user) before
    /// writing storage, and decrypt on load. Plaintext only crosses the
    /// boundary inside <see cref="AuthKeyRecord"/>.
    /// </summary>
    public interface IAuthKeyStore
    {
        /// <summary>Returns null if no record exists for the given DC.</summary>
        Task<AuthKeyRecord> LoadAsync(int dcId, CancellationToken ct);

        Task SaveAsync(int dcId, AuthKeyRecord record, CancellationToken ct);

        Task DeleteAsync(int dcId, CancellationToken ct);
    }
}
