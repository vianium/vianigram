// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Stickers bounded context (V1 shape).
    /// Every method is async, takes a <see cref="CancellationToken"/>, and
    /// returns <c>Result&lt;T, StickersError&gt;</c>; no exceptions cross this
    /// boundary.
    ///
    /// Consumers: presentation/ViewModels, other contexts via ACL adapters,
    /// composition root for wiring.
    /// </summary>
    public interface IStickersApi
    {
        /// <summary>Full sync of installed sets (<c>messages.getAllStickers#b8a0a1a8</c>).</summary>
        Task<Result<IList<StickerSet>, StickersError>> SyncStickerSetsAsync(CancellationToken ct);

        /// <summary>Fetch the body of a specific set (<c>messages.getStickerSet#c8a0ec74</c>).</summary>
        Task<Result<StickerSet, StickersError>> GetStickerSetAsync(StickerSetId id, CancellationToken ct);

        /// <summary>Install a sticker set (<c>messages.installStickerSet#c78fe460</c>).</summary>
        Task<Result<Unit, StickersError>> InstallSetAsync(StickerSetId id, CancellationToken ct);

        /// <summary>Uninstall a sticker set (<c>messages.uninstallStickerSet#f96e55de</c>).</summary>
        Task<Result<Unit, StickersError>> UninstallSetAsync(StickerSetId id, CancellationToken ct);

        /// <summary>Recently-used stickers (<c>messages.getRecentStickers#9da9403b</c>).</summary>
        Task<Result<IList<Sticker>, StickersError>> GetRecentAsync(CancellationToken ct);

        /// <summary>Favorite or unfavorite a sticker (<c>messages.faveSticker#b9ffc55b</c>).</summary>
        Task<Result<Unit, StickersError>> FavoriteAsync(StickerId id, CancellationToken ct);

        /// <summary>Search published sticker sets (<c>messages.searchStickerSets#35705b8a</c>).</summary>
        Task<Result<IList<StickerSet>, StickersError>> SearchAsync(string query, CancellationToken ct);

        /// <summary>
        /// CLR event raised whenever the library mutates. Subscribers receive a
        /// <see cref="StickerLibraryChangedEventArgs"/> describing what
        /// happened. Multicast from the implementation; thread-safe add/remove.
        /// </summary>
        event EventHandler<StickerLibraryChangedEventArgs> LibraryChanged;
    }
}
