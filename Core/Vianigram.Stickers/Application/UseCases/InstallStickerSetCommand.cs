// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Application.UseCases
{
    /// <summary>
    /// Install a sticker set via <c>messages.installStickerSet#c78fe460</c>.
    /// The <see cref="Archived"/> flag controls whether the install lands in
    /// the active panel (false) or in the archived bucket (true).
    /// </summary>
    public sealed class InstallStickerSetCommand
    {
        public StickerSetId Target { get; private set; }
        public bool Archived { get; private set; }

        public InstallStickerSetCommand(StickerSetId target, bool archived)
        {
            Target = target;
            Archived = archived;
        }
    }
}
