// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AvatarPlaceholder.cs — Vianigram.App.Helpers
//
// Static helpers used while a peer's real avatar JPEG is still in
// flight (or before RequestAvatar fires at all). Picks a stable
// background color per-peer plus 1-2 ASCII initials derived from
// the display name. The runtime AvatarCircle UserControl already
// owns its own ~10-entry palette and seed logic; this helper exposes
// the same primitives at the type system level so other surfaces
// (search results, forward picker, contacts list, peek-of-row tests)
// can render the same look without dragging in the WP control.
//
// All choices are deterministic in (peerId, displayName) so a peer
// keeps the same colour across launches and across UI surfaces —
// matches Telegram's behaviour.

using System;
using Windows.UI;
using Windows.UI.Xaml.Media;

namespace Vianigram.App.Helpers
{
    public static class AvatarPlaceholder
    {
        // Telegram-style palette: ten saturated tones distributed across
        // the hue wheel so adjacent peer IDs land on visually distinct
        // colours. Kept in lockstep with the AvatarCircle UserControl;
        // the slight difference is that AvatarCircle currently ships
        // eight colours — this helper exposes the full ten so the palette
        // can match a future widening without touching call sites.
        private static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(255, 230, 92, 92),    // red
            Color.FromArgb(255, 240, 150, 9),    // orange
            Color.FromArgb(255, 230, 196, 56),   // yellow
            Color.FromArgb(255, 164, 196, 0),    // lime
            Color.FromArgb(255, 96, 169, 23),    // green
            Color.FromArgb(255, 0, 171, 169),    // teal
            Color.FromArgb(255, 27, 161, 226),   // blue
            Color.FromArgb(255, 126, 56, 120),   // purple
            Color.FromArgb(255, 170, 0, 255),    // violet
            Color.FromArgb(255, 218, 83, 159)    // pink
        };

        /// <summary>
        /// Picks one of the palette colours by hashing <paramref name="peerId"/>
        /// modulo the palette size. Stable across launches and across
        /// UI surfaces so the same peer always wears the same colour.
        /// Falls back to the first palette slot when peerId is zero so
        /// the "unknown peer" placeholder is at least consistent.
        /// </summary>
        public static Brush BackgroundForPeer(long peerId)
        {
            int len = Palette.Length;
            int idx = peerId == 0L
                ? 0
                : (int)(((peerId % len) + len) % len);
            return new SolidColorBrush(Palette[idx]);
        }

        /// <summary>
        /// Returns 1-2 uppercase ASCII letters derived from the display
        /// name. Multi-word names take first + last initials ("John Doe" ->
        /// "JD"); single-word names take the first two characters
        /// ("ChannelName" -> "CH"); single-character or empty names
        /// degrade to a single "?". Non-ASCII letters and surrogate
        /// pairs (cyrillic, emoji) are preserved as-is — Telegram's own
        /// clients display the source-character anyway and stripping
        /// would just produce blank circles for Russian / Arabic peers.
        /// </summary>
        public static string InitialsForName(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return "?";
            string trimmed = displayName.Trim();
            if (trimmed.Length == 0) return "?";

            string[] parts = trimmed.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";

            if (parts.Length == 1)
            {
                string single = parts[0];
                if (single.Length <= 1)
                {
                    return single.ToUpperInvariant();
                }
                return single
                    .Substring(0, 2)
                    .ToUpperInvariant();
            }

            string first = parts[0].Substring(0, 1);
            string last = parts[parts.Length - 1].Substring(0, 1);
            return (first + last).ToUpperInvariant();
        }
    }
}
