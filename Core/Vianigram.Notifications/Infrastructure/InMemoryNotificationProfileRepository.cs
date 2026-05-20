// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Notifications.Domain.Entities;
using Vianigram.Notifications.Ports.Outbound;

namespace Vianigram.Notifications.Infrastructure
{
    /// <summary>
    /// In-memory repository: keeps the single
    /// <see cref="NotificationProfile"/> aggregate in process memory guarded
    /// by a private monitor.
    ///
    /// Sufficient for cold-start, settings sync, and UI consumption while
    /// the LocalSettings / SQLite-backed adapter in <c>Vianigram.Storage</c>
    /// is built. Hot-swap point: replace the binding in
    /// <see cref="Vianigram.Notifications.Composition.NotificationsCompositionRoot"/>
    /// with the persistent adapter and the application layer is unchanged.
    ///
    /// Thread-safety: all read/write paths take a lock on a private gate
    /// object. We intentionally hand back the live aggregate (NOT a copy) so
    /// handlers can mutate it in place — the lock here only serializes the
    /// load/save/delete transitions, not domain mutations. Application-layer
    /// callers serialize their own aggregate access by single-threading
    /// command handling per <see cref="Application.NotificationsApplication"/>.
    /// </summary>
    public sealed class InMemoryNotificationProfileRepository : INotificationProfileRepository
    {
        private readonly object _gate = new object();
        private NotificationProfile _profile;

        public Task<NotificationProfile> LoadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_profile == null) _profile = new NotificationProfile();
                return Task.FromResult(_profile);
            }
        }

        public Task SaveAsync(NotificationProfile profile, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (profile == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                _profile = profile;
            }
            return Task.FromResult<object>(null);
        }

        public Task DeleteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _profile = null;
            }
            return Task.FromResult<object>(null);
        }
    }
}
