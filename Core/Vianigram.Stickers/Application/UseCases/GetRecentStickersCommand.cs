// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Stickers.Application.UseCases
{
    /// <summary>
    /// Fetch the recently-used sticker list from the server
    /// (<c>messages.getRecentStickers#9da9403b</c>). Carries the per-collection
    /// hash for not_modified support and an attached/regular flag — V1 only
    /// fetches the regular collection.
    /// </summary>
    public sealed class GetRecentStickersCommand
    {
        public long Hash { get; private set; }
        public bool Attached { get; private set; }

        public static readonly GetRecentStickersCommand Default = new GetRecentStickersCommand(0L, false);

        public GetRecentStickersCommand(long hash, bool attached)
        {
            Hash = hash;
            Attached = attached;
        }
    }
}
