// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Message body font scale. Carries an integer point size and exposes
    /// well-known defaults. Range matches Telegram-Android's
    /// <c>SharedConfig.fontSize</c> band [12, 30].
    ///
    /// Immutable struct so the value flows through events and the public API
    /// without allocations.
    /// </summary>
    public struct MessageTextSize : IEquatable<MessageTextSize>
    {
        public const int MinPoints = 12;
        public const int MaxPoints = 30;
        public const int DefaultPoints = 16;

        private readonly int _points;

        public MessageTextSize(int points)
        {
            if (points < MinPoints) points = MinPoints;
            if (points > MaxPoints) points = MaxPoints;
            _points = points;
        }

        public int Points { get { return _points; } }

        public static MessageTextSize Default
        {
            get { return new MessageTextSize(DefaultPoints); }
        }

        public bool Equals(MessageTextSize other)
        {
            return _points == other._points;
        }

        public override bool Equals(object obj)
        {
            return obj is MessageTextSize && Equals((MessageTextSize)obj);
        }

        public override int GetHashCode()
        {
            return _points.GetHashCode();
        }

        public override string ToString()
        {
            return _points + "pt";
        }

        public static bool operator ==(MessageTextSize a, MessageTextSize b) { return a.Equals(b); }
        public static bool operator !=(MessageTextSize a, MessageTextSize b) { return !a.Equals(b); }
    }
}
