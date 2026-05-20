// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="IStickersApi.LibraryChanged"/>
    /// whenever the sticker library mutates. Mirrors the kernel bus events
    /// (<c>StickerSetInstalled</c>, <c>StickerSetUninstalled</c>,
    /// <c>StickerSetReordered</c>, <c>StickerUsedRecently</c>,
    /// <c>StickerFavorited</c>, <c>StickersSynced</c>) in a CLR-event shape so
    /// XAML/UI layers that don't take an <c>IEventBus</c> dependency can still
    /// subscribe.
    /// </summary>
    public sealed class StickerLibraryChangedEventArgs : EventArgs
    {
        public enum ChangeReason
        {
            SetInstalled = 0,
            SetUninstalled = 1,
            SetReordered = 2,
            StickerUsed = 3,
            StickerFavorited = 4,
            StickerUnfavorited = 5,
            LibrarySynced = 6
        }

        public ChangeReason Reason { get; private set; }

        /// <summary>Affected set. Null for sticker-level events and for <see cref="ChangeReason.LibrarySynced"/> / <see cref="ChangeReason.SetReordered"/>.</summary>
        public StickerSetId? SetId { get; private set; }

        /// <summary>Affected sticker. Null for set-level events.</summary>
        public StickerId? StickerId { get; private set; }

        public DateTime At { get; private set; }

        public StickerLibraryChangedEventArgs(ChangeReason reason, StickerSetId? setId, StickerId? stickerId, DateTime at)
        {
            Reason = reason;
            SetId = setId;
            StickerId = stickerId;
            At = at;
        }
    }
}
