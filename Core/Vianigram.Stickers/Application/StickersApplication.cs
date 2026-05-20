// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Stickers.Application.Handlers;
using Vianigram.Stickers.Application.UseCases;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Domain.Events;
using Vianigram.Stickers.Domain.ValueObjects;
using Vianigram.Stickers.Ports.Inbound;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Application
{
    /// <summary>
    /// <see cref="IStickersApi"/> implementation. Dispatches each public method
    /// to the matching handler, surfaces results as
    /// <c>Result&lt;T, StickersError&gt;</c>, and re-broadcasts internal domain
    /// events on the kernel bus into a CLR event
    /// (<see cref="LibraryChanged"/>) so XAML/UI consumers don't need an
    /// <see cref="IEventBus"/> dependency.
    ///
    /// All public methods are exception-free across the boundary: any
    /// unexpected failure is mapped to <see cref="StickersError"/>.
    /// </summary>
    public sealed class StickersApplication : IStickersApi, IDisposable
    {
        private readonly SyncStickersHandler _sync;
        private readonly InstallStickerSetHandler _install;
        private readonly UninstallStickerSetHandler _uninstall;
        private readonly GetStickerSetHandler _getSet;
        private readonly GetRecentStickersHandler _getRecent;
        private readonly FavoriteStickerHandler _favorite;
        private readonly SearchStickersHandler _search;

        private readonly IDisposable[] _subs;
        private bool _disposed;

        public event EventHandler<StickerLibraryChangedEventArgs> LibraryChanged;

        public StickersApplication(
            IMtProtoRpcPort rpc,
            IStickerRepository repo,
            IStickerCachePort cache,
            IEventBus bus,
            ILogger logger,
            IClock clock)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (repo == null) throw new ArgumentNullException("repo");
            if (cache == null) throw new ArgumentNullException("cache");
            if (bus == null) throw new ArgumentNullException("bus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            _sync = new SyncStickersHandler(repo, rpc, bus, logger, clock);
            _install = new InstallStickerSetHandler(repo, rpc, bus, logger, clock);
            _uninstall = new UninstallStickerSetHandler(repo, cache, rpc, bus, logger, clock);
            _getSet = new GetStickerSetHandler(repo, rpc, bus, logger, clock);
            _getRecent = new GetRecentStickersHandler(repo, rpc, bus, logger, clock);
            _favorite = new FavoriteStickerHandler(repo, rpc, bus, logger, clock);
            _search = new SearchStickersHandler(rpc, logger);

            _subs = new IDisposable[]
            {
                bus.Subscribe<StickerSetInstalled>(OnSetInstalled),
                bus.Subscribe<StickerSetUninstalled>(OnSetUninstalled),
                bus.Subscribe<StickerSetReordered>(OnSetReordered),
                bus.Subscribe<StickerUsedRecently>(OnStickerUsed),
                bus.Subscribe<StickerFavorited>(OnStickerFavorited),
                bus.Subscribe<StickersSynced>(OnLibrarySynced)
            };
        }

        // ---- IStickersApi ---------------------------------------------------

        public async Task<Result<IList<StickerSet>, StickersError>> SyncStickerSetsAsync(CancellationToken ct)
        {
            try
            {
                return await _sync.HandleAsync(SyncStickersCommand.Initial, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<StickerSet>, StickersError>.Fail(StickersError.Unknown("SyncStickerSetsAsync failed", ex));
            }
        }

        public async Task<Result<StickerSet, StickersError>> GetStickerSetAsync(StickerSetId id, CancellationToken ct)
        {
            try
            {
                if (id.Value <= 0)
                    return Result<StickerSet, StickersError>.Fail(StickersError.NotInExpectedState("set id required"));
                return await _getSet.HandleAsync(new GetStickerSetCommand(id), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<StickerSet, StickersError>.Fail(StickersError.Unknown("GetStickerSetAsync failed", ex));
            }
        }

        public async Task<Result<Unit, StickersError>> InstallSetAsync(StickerSetId id, CancellationToken ct)
        {
            try
            {
                if (id.Value <= 0)
                    return Result<Unit, StickersError>.Fail(StickersError.NotInExpectedState("set id required"));
                return await _install.HandleAsync(new InstallStickerSetCommand(id, /*archived*/ false), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, StickersError>.Fail(StickersError.Unknown("InstallSetAsync failed", ex));
            }
        }

        public async Task<Result<Unit, StickersError>> UninstallSetAsync(StickerSetId id, CancellationToken ct)
        {
            try
            {
                if (id.Value <= 0)
                    return Result<Unit, StickersError>.Fail(StickersError.NotInExpectedState("set id required"));
                return await _uninstall.HandleAsync(new UninstallStickerSetCommand(id), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, StickersError>.Fail(StickersError.Unknown("UninstallSetAsync failed", ex));
            }
        }

        public async Task<Result<IList<Sticker>, StickersError>> GetRecentAsync(CancellationToken ct)
        {
            try
            {
                return await _getRecent.HandleAsync(GetRecentStickersCommand.Default, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<Sticker>, StickersError>.Fail(StickersError.Unknown("GetRecentAsync failed", ex));
            }
        }

        public async Task<Result<Unit, StickersError>> FavoriteAsync(StickerId id, CancellationToken ct)
        {
            try
            {
                if (id.Value <= 0)
                    return Result<Unit, StickersError>.Fail(StickersError.NotInExpectedState("sticker id required"));
                return await _favorite.HandleAsync(new FavoriteStickerCommand(id, /*unfave*/ false), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<Unit, StickersError>.Fail(StickersError.Unknown("FavoriteAsync failed", ex));
            }
        }

        public async Task<Result<IList<StickerSet>, StickersError>> SearchAsync(string query, CancellationToken ct)
        {
            try
            {
                if (query == null)
                    return Result<IList<StickerSet>, StickersError>.Fail(StickersError.NotInExpectedState("query required"));
                return await _search.HandleAsync(new SearchStickersCommand(query), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                return Result<IList<StickerSet>, StickersError>.Fail(StickersError.Unknown("SearchAsync failed", ex));
            }
        }

        // ---- bus -> CLR-event bridge ----------------------------------------

        private void OnSetInstalled(StickerSetInstalled e)
        {
            Raise(new StickerLibraryChangedEventArgs(
                StickerLibraryChangedEventArgs.ChangeReason.SetInstalled, e.SetId, null, e.At));
        }

        private void OnSetUninstalled(StickerSetUninstalled e)
        {
            Raise(new StickerLibraryChangedEventArgs(
                StickerLibraryChangedEventArgs.ChangeReason.SetUninstalled, e.SetId, null, e.At));
        }

        private void OnSetReordered(StickerSetReordered e)
        {
            Raise(new StickerLibraryChangedEventArgs(
                StickerLibraryChangedEventArgs.ChangeReason.SetReordered, null, null, e.At));
        }

        private void OnStickerUsed(StickerUsedRecently e)
        {
            Raise(new StickerLibraryChangedEventArgs(
                StickerLibraryChangedEventArgs.ChangeReason.StickerUsed, null, e.StickerId, e.At));
        }

        private void OnStickerFavorited(StickerFavorited e)
        {
            var reason = e.Favored
                ? StickerLibraryChangedEventArgs.ChangeReason.StickerFavorited
                : StickerLibraryChangedEventArgs.ChangeReason.StickerUnfavorited;
            Raise(new StickerLibraryChangedEventArgs(reason, null, e.StickerId, e.At));
        }

        private void OnLibrarySynced(StickersSynced e)
        {
            Raise(new StickerLibraryChangedEventArgs(
                StickerLibraryChangedEventArgs.ChangeReason.LibrarySynced, null, null, e.At));
        }

        private void Raise(StickerLibraryChangedEventArgs args)
        {
            var h = LibraryChanged;
            if (h == null) return;
            try
            {
                h(this, args);
            }
            catch
            {
                // Swallow downstream subscriber faults — never poison the bus.
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _subs.Length; i++)
            {
                if (_subs[i] != null) _subs[i].Dispose();
            }
        }
    }
}
