// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Stickers.Domain.ValueObjects
{
    /// <summary>
    /// Composite key used by <c>IStickerCachePort</c> to address a single
    /// decoded sticker blob. Includes both the owning <see cref="StickerSetId"/>
    /// (to enable fast pack-wide eviction) and the <see cref="StickerId"/>.
    ///
    /// Identity is by the (setId, stickerId) pair. The access hashes are NOT
    /// part of the identity — they may change while the underlying document
    /// stays the same.
    /// </summary>
    public struct StickerCacheKey : IEquatable<StickerCacheKey>
    {
        private readonly long _setId;
        private readonly long _stickerId;

        public StickerCacheKey(long setId, long stickerId)
        {
            if (setId <= 0) throw new ArgumentException("setId must be positive", "setId");
            if (stickerId <= 0) throw new ArgumentException("stickerId must be positive", "stickerId");
            _setId = setId;
            _stickerId = stickerId;
        }

        public StickerCacheKey(StickerSetId setId, StickerId stickerId)
            : this(setId.Value, stickerId.Value)
        {
        }

        public long SetId { get { return _setId; } }
        public long StickerId { get { return _stickerId; } }

        public bool Equals(StickerCacheKey other)
        {
            return _setId == other._setId && _stickerId == other._stickerId;
        }

        public override bool Equals(object obj)
        {
            return obj is StickerCacheKey && Equals((StickerCacheKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + _setId.GetHashCode();
                h = h * 31 + _stickerId.GetHashCode();
                return h;
            }
        }

        public override string ToString()
        {
            return "cache:" +
                _setId.ToString(CultureInfo.InvariantCulture) + "/" +
                _stickerId.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(StickerCacheKey a, StickerCacheKey b) { return a.Equals(b); }
        public static bool operator !=(StickerCacheKey a, StickerCacheKey b) { return !a.Equals(b); }
    }
}
