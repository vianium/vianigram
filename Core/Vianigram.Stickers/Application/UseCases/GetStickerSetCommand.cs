// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Application.UseCases
{
    /// <summary>
    /// Fetch the body of a sticker set via <c>messages.getStickerSet#c8a0ec74</c>.
    /// The <see cref="Hash"/> is the per-set content hash held locally; the
    /// server short-circuits with <c>messages.stickerSetNotModified</c> when
    /// the set hasn't changed.
    /// </summary>
    public sealed class GetStickerSetCommand
    {
        public StickerSetId Target { get; private set; }
        public int Hash { get; private set; }

        public GetStickerSetCommand(StickerSetId target, int hash)
        {
            Target = target;
            Hash = hash;
        }

        public GetStickerSetCommand(StickerSetId target)
            : this(target, 0)
        {
        }
    }
}
