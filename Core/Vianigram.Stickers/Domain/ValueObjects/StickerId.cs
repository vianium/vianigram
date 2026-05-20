// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Stickers.Domain.ValueObjects
{
    /// <summary>
    /// Telegram-issued identifier for an individual sticker (document id).
    /// Carries the access hash because the TL InputDocument constructor needs
    /// both id and access_hash to address the document on the server.
    ///
    /// Defined locally per context (Stickers does not share value objects with
    /// other bounded contexts).
    /// </summary>
    public struct StickerId : IEquatable<StickerId>
    {
        private readonly long _value;
        private readonly long _accessHash;

        public StickerId(long value, long accessHash)
        {
            if (value <= 0) throw new ArgumentException("sticker id must be positive", "value");
            _value = value;
            _accessHash = accessHash;
        }

        public long Value { get { return _value; } }
        public long AccessHash { get { return _accessHash; } }

        public bool Equals(StickerId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is StickerId && Equals((StickerId)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "sticker:" + _value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(StickerId a, StickerId b) { return a.Equals(b); }
        public static bool operator !=(StickerId a, StickerId b) { return !a.Equals(b); }
    }
}
