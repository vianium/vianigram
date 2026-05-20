// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Infrastructure
{
    /// <summary>
    /// In-memory repository: keeps the single <see cref="StickerLibrary"/>
    /// aggregate in process memory guarded by a private monitor.
    ///
    /// Sufficient for cold-start, sync, and UI consumption until the
    /// SQLite-backed repository in <c>Vianigram.Storage</c> is built.
    /// Hot-swap point: replace the binding in
    /// <see cref="Vianigram.Stickers.Composition.StickersCompositionRoot"/>
    /// with the persistent adapter and the application layer is unchanged.
    ///
    /// Thread-safety: all read/write paths take a lock on a private gate
    /// object. We intentionally hand back the live aggregate (NOT a copy) so
    /// handlers can mutate it in place — the lock here only serializes the
    /// load/save/delete transitions, not domain mutations. Application-layer
    /// callers serialize their own aggregate access by single-threading
    /// command handling per <see cref="Application.StickersApplication"/>.
    /// </summary>
    public sealed class InMemoryStickerRepository : IStickerRepository
    {
        private readonly object _gate = new object();
        private StickerLibrary _library;

        public Task<StickerLibrary> LoadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                if (_library == null) _library = new StickerLibrary();
                return Task.FromResult(_library);
            }
        }

        public Task SaveAsync(StickerLibrary library, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (library == null) return Task.FromResult<object>(null);
            lock (_gate)
            {
                _library = library;
            }
            return Task.FromResult<object>(null);
        }

        public Task DeleteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            lock (_gate)
            {
                _library = null;
            }
            return Task.FromResult<object>(null);
        }
    }
}
