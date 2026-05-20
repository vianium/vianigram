// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Media.Domain.ValueObjects
{
    /// <summary>
    /// Coarse classification of a media payload. Drives codec selection on the
    /// native side (Vianigram.Core.Media) and chunk-size strategy on the
    /// managed side (voices/stickers stay small; videos go big).
    /// </summary>
    public enum FileType
    {
        Unknown = 0,
        Photo = 1,
        Document = 2,
        Video = 3,
        Voice = 4,
        Sticker = 5
    }
}
