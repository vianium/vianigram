// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Ports.Outbound
{
    /// <summary>
    /// Outbound port for persisting <see cref="SecretSession"/> aggregates.
    /// V1 implementation is in-memory
    /// (<c>InMemorySecretChatRepository</c>); the production adapter lives
    /// in <c>Vianigram.Storage</c>, encrypted at rest via
    /// <c>DataProtectionProvider</c>.
    ///
    /// <para><b>Key isolation contract:</b> implementations MUST NOT
    /// serialize the raw <c>auth_key</c> bytes via any path other than the
    /// pair <c>(ISecretCryptoPort.WrapAuthKeyAsync,
    /// ISecretCryptoPort.UnwrapAuthKeyAsync)</c>. The repository sees only
    /// the wrapped (encrypted-at-rest) ciphertext blob; the raw 256 bytes
    /// stay inside the crypto port. The MVP <c>InMemorySecretChatRepository</c>
    /// holds the live <see cref="AuthKey"/> reference in process memory and
    /// does not write to disk, so this contract is vacuously satisfied; the
    /// SQLite-backed adapter must enforce it explicitly.</para>
    ///
    /// <para>All operations are async to keep storage swap painless even
    /// though the in-memory implementation completes synchronously today.
    /// Implementations MUST be thread-safe.</para>
    /// </summary>
    public interface ISecretChatRepository
    {
        /// <summary>
        /// Returns the session keyed by <paramref name="chatId"/>, or null if
        /// none. Returning the live aggregate is encouraged (handlers mutate
        /// in place); storage adapters that re-hydrate per-call are also
        /// valid as long as <see cref="SaveAsync"/> commits the same
        /// reference.
        /// </summary>
        Task<SecretSession> FindAsync(SecretChatId chatId, CancellationToken ct);

        /// <summary>
        /// Persist (insert or update) the supplied aggregate.
        /// </summary>
        Task SaveAsync(SecretSession session, CancellationToken ct);

        /// <summary>
        /// Wipe the row for <paramref name="chatId"/> and zero the persisted
        /// auth_key blob. Idempotent.
        /// </summary>
        Task DeleteAsync(SecretChatId chatId, CancellationToken ct);

        /// <summary>
        /// Snapshot of every session currently persisted. Used at startup to
        /// rehydrate the in-memory map and by the Presentation layer for the
        /// "secret chats" sidebar. Order is implementation-defined.
        /// </summary>
        Task<IList<SecretSession>> ListAsync(CancellationToken ct);
    }
}
