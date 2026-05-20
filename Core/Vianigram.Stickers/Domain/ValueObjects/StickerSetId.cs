// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Stickers.Domain.ValueObjects
{
    /// <summary>
    /// Telegram-issued identifier for a sticker pack (set). Carries both the
    /// stable numeric id and the access hash required by the TL InputStickerSet
    /// constructors. We bundle them here because callers (handlers, repository)
    /// almost always need both — and the access hash is meaningless without the
    /// id.
    ///
    /// Defined locally per context (Stickers does not share value objects with
    /// other bounded contexts).
    /// </summary>
    public struct StickerSetId : IEquatable<StickerSetId>
    {
        private readonly long _value;
        private readonly long _accessHash;

        public StickerSetId(long value, long accessHash)
        {
            if (value <= 0) throw new ArgumentException("sticker set id must be positive", "value");
            _value = value;
            _accessHash = accessHash;
        }

        public long Value { get { return _value; } }
        public long AccessHash { get { return _accessHash; } }

        public bool Equals(StickerSetId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is StickerSetId && Equals((StickerSetId)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "set:" + _value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(StickerSetId a, StickerSetId b) { return a.Equals(b); }
        public static bool operator !=(StickerSetId a, StickerSetId b) { return !a.Equals(b); }
    }
}
