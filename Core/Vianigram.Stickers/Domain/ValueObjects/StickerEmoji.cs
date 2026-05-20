// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Stickers.Domain.ValueObjects
{
    /// <summary>
    /// Emoji shortcode associated with a sticker. Telegram tags every sticker
    /// with one or more emoji that the user typed when adding it; the emoji
    /// makes typed-emoji-to-sticker suggestions possible.
    ///
    /// Stored as a normalized string with leading/trailing whitespace trimmed.
    /// May be empty (some packs ship stickers without emoji metadata).
    ///
    /// Immutable. Equality is ordinal (emoji codepoints are identity).
    /// </summary>
    public sealed class StickerEmoji : IEquatable<StickerEmoji>
    {
        public static readonly StickerEmoji Empty = new StickerEmoji(string.Empty);

        private readonly string _value;

        private StickerEmoji(string value)
        {
            _value = value;
        }

        public string Value { get { return _value; } }
        public bool IsEmpty { get { return _value.Length == 0; } }

        public static StickerEmoji From(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return Empty;
            string trimmed = raw.Trim();
            if (trimmed.Length == 0) return Empty;
            return new StickerEmoji(trimmed);
        }

        public bool Equals(StickerEmoji other)
        {
            if (ReferenceEquals(other, null)) return false;
            return string.Equals(_value, other._value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as StickerEmoji);
        }

        public override int GetHashCode()
        {
            return _value == null ? 0 : _value.GetHashCode();
        }

        public override string ToString() { return _value ?? string.Empty; }
    }
}
