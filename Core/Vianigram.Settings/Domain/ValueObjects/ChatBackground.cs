// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Identifies the active chat-list / chat-page background. V1 carries:
    ///   * <see cref="Id"/>          — stable id (e.g. <c>"default"</c>,
    ///                                 a Telegram <c>wallPaper</c> id, or a
    ///                                 LocalFolder asset path).
    ///   * <see cref="IsCustomImage"/> — true when the id points at a
    ///                                   user-imported asset rather than a
    ///                                   server-blessed wallpaper.
    ///   * <see cref="ColorHex"/>    — fallback solid color in <c>#RRGGBB</c>
    ///                                 when the asset cannot resolve.
    ///
    /// Immutable.
    /// </summary>
    public sealed class ChatBackground : IEquatable<ChatBackground>
    {
        public const string DefaultId = "default";
        public const string DefaultColorHex = "#0E1621";

        private readonly string _id;
        private readonly bool _isCustomImage;
        private readonly string _colorHex;

        public ChatBackground(string id, bool isCustomImage, string colorHex)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id required", "id");
            _id = id;
            _isCustomImage = isCustomImage;
            _colorHex = string.IsNullOrEmpty(colorHex) ? DefaultColorHex : colorHex;
        }

        public string Id { get { return _id; } }
        public bool IsCustomImage { get { return _isCustomImage; } }
        public string ColorHex { get { return _colorHex; } }

        public static ChatBackground Default
        {
            get { return new ChatBackground(DefaultId, false, DefaultColorHex); }
        }

        public bool Equals(ChatBackground other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return string.Equals(_id, other._id, StringComparison.Ordinal)
                && _isCustomImage == other._isCustomImage
                && string.Equals(_colorHex, other._colorHex, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ChatBackground);
        }

        public override int GetHashCode()
        {
            int h = _id.GetHashCode();
            h = (h * 397) ^ (_isCustomImage ? 1 : 0);
            h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_colorHex);
            return h;
        }

        public override string ToString()
        {
            return "bg(" + _id + (_isCustomImage ? "*" : "") + ", " + _colorHex + ")";
        }
    }
}
