// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Notifications.Domain.Entities;

namespace Vianigram.Notifications.Ports.Outbound
{
    /// <summary>
    /// Outbound port for persisting the <see cref="NotificationProfile"/>
    /// aggregate. V1 implementation is in-memory
    /// (<c>InMemoryNotificationProfileRepository</c>); a SQLite / LocalSettings
    /// adapter will land in <c>Vianigram.Storage</c> and be hot-swapped at
    /// composition time.
    ///
    /// All operations are async to keep the storage swap painless even though
    /// the in-memory implementation completes synchronously today.
    ///
    /// Implementations MUST be thread-safe: the application uses a single
    /// aggregate per active account, and handlers may run on the thread pool.
    /// </summary>
    public interface INotificationProfileRepository
    {
        /// <summary>
        /// Returns the current aggregate. Never null — the repository
        /// synthesizes an empty <see cref="NotificationProfile"/> on first
        /// access.
        /// </summary>
        Task<NotificationProfile> LoadAsync(CancellationToken ct);

        /// <summary>Persist the supplied aggregate (typically the same reference returned by <see cref="LoadAsync"/>).</summary>
        Task SaveAsync(NotificationProfile profile, CancellationToken ct);

        /// <summary>Wipe the aggregate (used on logout / account switch).</summary>
        Task DeleteAsync(CancellationToken ct);
    }
}
