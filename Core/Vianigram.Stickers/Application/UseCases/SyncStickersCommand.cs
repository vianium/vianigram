// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Stickers.Application.UseCases
{
    /// <summary>
    /// Full sync of installed sets via <c>messages.getAllStickers#b8a0a1a8</c>.
    /// The hash is the library hash from the previous sync; the server uses it
    /// to short-circuit with <c>messages.allStickersNotModified</c> when nothing
    /// has changed.
    /// </summary>
    public sealed class SyncStickersCommand
    {
        public long Hash { get; private set; }
        public static readonly SyncStickersCommand Initial = new SyncStickersCommand(0L);

        public SyncStickersCommand(long hash)
        {
            Hash = hash;
        }
    }
}
