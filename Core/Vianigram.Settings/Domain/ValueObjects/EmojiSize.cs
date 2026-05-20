// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Emoji rendering size. Mirrors the bands surfaced by Telegram-Android
    /// <c>SharedConfig.EMOJI_SIZE_*</c> — three discrete steps used to scale
    /// the in-message emoji glyph.
    /// </summary>
    public enum EmojiSize
    {
        Small = 0,
        Default = 1,
        Large = 2
    }

    /// <summary>
    /// Helpers for clamping ints into the <see cref="EmojiSize"/> band.
    /// </summary>
    public static class EmojiSizeExtensions
    {
        public static EmojiSize Clamp(int raw)
        {
            if (raw < (int)EmojiSize.Small) return EmojiSize.Small;
            if (raw > (int)EmojiSize.Large) return EmojiSize.Large;
            return (EmojiSize)raw;
        }
    }
}
