// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Domain.Events
{
    /// <summary>
    /// Emitted when a sticker pack is added to the user's installed library
    /// (either via <c>messages.installStickerSet</c> or surfaced by a sync
    /// that revealed a new remote-side install).
    /// </summary>
    public sealed class StickerSetInstalled : IDomainEvent
    {
        public StickerSetId SetId { get; private set; }
        public DateTime At { get; private set; }

        public StickerSetInstalled(StickerSetId setId, DateTime at)
        {
            SetId = setId;
            At = at;
        }
    }

    /// <summary>Emitted when a pack leaves the installed set.</summary>
    public sealed class StickerSetUninstalled : IDomainEvent
    {
        public StickerSetId SetId { get; private set; }
        public DateTime At { get; private set; }

        public StickerSetUninstalled(StickerSetId setId, DateTime at)
        {
            SetId = setId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when the user reorders their installed packs. V1 carries a
    /// snapshot of the new order; subscribers are expected to read the
    /// aggregate via the repository for the canonical state.
    /// </summary>
    public sealed class StickerSetReordered : IDomainEvent
    {
        public DateTime At { get; private set; }

        public StickerSetReordered(DateTime at)
        {
            At = at;
        }
    }

    /// <summary>
    /// Emitted whenever a sticker is bumped to the top of the recently-used
    /// list (typically after the user sends one).
    /// </summary>
    public sealed class StickerUsedRecently : IDomainEvent
    {
        public StickerId StickerId { get; private set; }
        public DateTime At { get; private set; }

        public StickerUsedRecently(StickerId stickerId, DateTime at)
        {
            StickerId = stickerId;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a sticker is added to (or removed from) the favorites
    /// list. <see cref="Favored"/> distinguishes the two cases.
    /// </summary>
    public sealed class StickerFavorited : IDomainEvent
    {
        public StickerId StickerId { get; private set; }
        public bool Favored { get; private set; }
        public DateTime At { get; private set; }

        public StickerFavorited(StickerId stickerId, bool favored, DateTime at)
        {
            StickerId = stickerId;
            Favored = favored;
            At = at;
        }
    }

    /// <summary>
    /// Emitted at the end of a successful <c>messages.getAllStickers</c> sync.
    /// Carries summary counts so subscribers can validate expectations
    /// (UI badges, telemetry) without re-querying the repo.
    /// </summary>
    public sealed class StickersSynced : IDomainEvent
    {
        public int InstalledCount { get; private set; }
        public DateTime At { get; private set; }

        public StickersSynced(int installedCount, DateTime at)
        {
            if (installedCount < 0) throw new ArgumentOutOfRangeException("installedCount", "must be >= 0");
            InstalledCount = installedCount;
            At = at;
        }
    }
}
