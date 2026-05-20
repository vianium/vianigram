// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Application.UseCases
{
    /// <summary>
    /// Favorite or unfavorite a sticker via
    /// <c>messages.faveSticker#b9ffc55b</c>. <see cref="Unfave"/> = true tells
    /// the server to remove the sticker from the favorites collection.
    /// </summary>
    public sealed class FavoriteStickerCommand
    {
        public StickerId Target { get; private set; }
        public bool Unfave { get; private set; }

        public FavoriteStickerCommand(StickerId target, bool unfave)
        {
            Target = target;
            Unfave = unfave;
        }
    }
}
