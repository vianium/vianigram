// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Application.UseCases
{
    /// <summary>
    /// Uninstall a sticker set via <c>messages.uninstallStickerSet#f96e55de</c>.
    /// The handler also evicts cached blobs for the set after a successful
    /// server acknowledgement.
    /// </summary>
    public sealed class UninstallStickerSetCommand
    {
        public StickerSetId Target { get; private set; }

        public UninstallStickerSetCommand(StickerSetId target)
        {
            Target = target;
        }
    }
}
