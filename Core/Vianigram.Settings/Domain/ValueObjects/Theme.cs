// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// User-selected theme. <see cref="System"/> defers to OS theme via
    /// <c>UISettings.GetColorValue(UIColorType.Background)</c>; <see cref="Custom"/>
    /// indicates the active theme is a user-imported pack (the actual asset
    /// reference is stored under a separate preference key).
    ///
    /// Mirrors the <c>themeSettings</c> palette in TDLib and the
    /// <c>Theme</c> enum used by Telegram-Android settings, plus a WP-specific
    /// <see cref="AmoledDark"/> entry for OLED-pixel-off backgrounds (true
    /// black) which Telegram-Android exposes as a separate option.
    /// </summary>
    public enum Theme
    {
        /// <summary>Follow the OS theme (default for V1).</summary>
        System = 0,
        Light = 1,
        Dark = 2,
        /// <summary>Pure-black dark theme — preserves OLED pixel-off energy savings.</summary>
        AmoledDark = 3,
        /// <summary>Theme is a user-imported pack; resolution is delegated to the App layer.</summary>
        Custom = 4
    }
}
