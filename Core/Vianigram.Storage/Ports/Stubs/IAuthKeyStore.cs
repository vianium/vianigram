// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// STUB of <c>Vianigram.Account.Ports.Outbound.IAuthKeyStore</c>.
    /// Carried locally until the Account context publishes its real outbound
    /// port. When that happens, replace this file with a ProjectReference and
    /// update <see cref="Infrastructure.Repositories.JsonAuthKeyStore"/> to
    /// implement the real interface.
    /// <para>
    /// Tracked: <c>plans/piped-petting-charm.md</c>.
    /// </para>
    /// </summary>
    public interface IAuthKeyStore
    {
        /// <summary>
        /// Reads the persisted <paramref name="dcId"/> auth key blob, or
        /// <c>null</c> when no record exists for that data center.
        /// </summary>
        Task<AuthKeyRecord> GetAsync(int dcId, CancellationToken ct);

        /// <summary>
        /// Stores or replaces the auth key blob for <paramref name="record"/>'s
        /// data center.
        /// </summary>
        Task PutAsync(AuthKeyRecord record, CancellationToken ct);

        /// <summary>
        /// Removes the auth key for the given data center. No-op if absent.
        /// </summary>
        Task DeleteAsync(int dcId, CancellationToken ct);

        /// <summary>
        /// Drops every persisted auth key (used by full-reset / wipe-on-failure).
        /// </summary>
        Task ClearAsync(CancellationToken ct);
    }
}
