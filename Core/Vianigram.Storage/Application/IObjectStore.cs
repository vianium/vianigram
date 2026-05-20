// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IObjectStore<T> — typed, file-or-database-backed aggregate root store.
// SQLite is the default backend; JsonObjectStore<T> remains for compatibility
// and migration. Both implement this port so adapters (JsonAuthKeyStore,
// JsonDialogRepository, ...) compose either one.

using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Storage.Application
{
    /// <summary>
    /// Generic aggregate-root key/value port. Implementations persist a single
    /// strongly-typed JSON document under a stable filename/scope. Used by the
    /// per-aggregate repositories (auth_keys, dialogs, messages, sync_state).
    /// </summary>
    /// <typeparam name="T">DataContract-serializable aggregate type.</typeparam>
    public interface IObjectStore<T> where T : class, new()
    {
        /// <summary>Loads the persisted document, returning <c>new T()</c> when absent.</summary>
        Task<T> LoadAsync(CancellationToken ct);

        /// <summary>Persists <paramref name="value"/>. Implementations must be crash-safe.</summary>
        Task SaveAsync(T value, CancellationToken ct);

        /// <summary>Removes the persisted document. No-op when already absent.</summary>
        Task DeleteAsync(CancellationToken ct);
    }
}
