// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Media.Domain.ValueObjects
{
    /// <summary>
    /// Identity of a media transfer (upload or download). Backed by a Guid so
    /// transfer ids never collide with Telegram file_ids (which are int64).
    /// Opaque to callers — only the Media context produces them.
    /// </summary>
    public struct MediaId : IEquatable<MediaId>
    {
        private readonly Guid _value;

        private MediaId(Guid value)
        {
            _value = value;
        }

        public Guid Value { get { return _value; } }

        public static MediaId NewId()
        {
            return new MediaId(Guid.NewGuid());
        }

        public static MediaId From(Guid value)
        {
            if (value == Guid.Empty) throw new ArgumentException("MediaId cannot be empty", "value");
            return new MediaId(value);
        }

        public bool Equals(MediaId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is MediaId && Equals((MediaId)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "MediaId(" + _value.ToString("N").Substring(0, 8) + ")";
        }

        public static bool operator ==(MediaId a, MediaId b) { return a.Equals(b); }
        public static bool operator !=(MediaId a, MediaId b) { return !a.Equals(b); }
    }
}
