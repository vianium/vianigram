// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Notifications.Domain.ValueObjects
{
    /// <summary>
    /// Lockscreen / start-tile badge count. Carries the unread total clamped
    /// into the WP8.1 BadgeNumeric range [0, 99] plus a flag indicating whether
    /// any of the unread messages mentioned the active account (the UI may
    /// surface mentions distinctly even when the chat is muted).
    ///
    /// Immutable struct so the value can flow through events and the public
    /// API without allocations.
    /// </summary>
    public struct BadgeCount : IEquatable<BadgeCount>
    {
        /// <summary>WP8.1 BadgeNumeric clamp ceiling (values above are shown as "99+").</summary>
        public const int MaxBadge = 99;

        private readonly int _count;
        private readonly bool _hasMentions;

        public BadgeCount(int count, bool hasMentions)
        {
            if (count < 0) count = 0;
            if (count > MaxBadge) count = MaxBadge;
            _count = count;
            _hasMentions = hasMentions;
        }

        public int Count { get { return _count; } }
        public bool HasMentions { get { return _hasMentions; } }

        public static BadgeCount Empty
        {
            get { return new BadgeCount(0, false); }
        }

        public bool IsEmpty
        {
            get { return _count == 0 && !_hasMentions; }
        }

        public bool Equals(BadgeCount other)
        {
            return _count == other._count && _hasMentions == other._hasMentions;
        }

        public override bool Equals(object obj)
        {
            return obj is BadgeCount && Equals((BadgeCount)obj);
        }

        public override int GetHashCode()
        {
            return _count.GetHashCode() ^ (_hasMentions ? 1 : 0);
        }

        public override string ToString()
        {
            return "badge(" + _count + (_hasMentions ? ", mentions" : "") + ")";
        }

        public static bool operator ==(BadgeCount a, BadgeCount b) { return a.Equals(b); }
        public static bool operator !=(BadgeCount a, BadgeCount b) { return !a.Equals(b); }
    }
}
