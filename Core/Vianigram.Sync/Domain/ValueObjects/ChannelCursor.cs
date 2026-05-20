// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Per-channel pts cursor. Channels (broadcasts and supergroups) maintain
    /// an independent pts stream from the common box; gaps must be filled with
    /// updates.getChannelDifference, not updates.getDifference.
    ///
    /// Immutable; replaced on each update via <see cref="WithPts"/> /
    /// <see cref="WithLastSyncedAt"/>.
    /// </summary>
    public sealed class ChannelCursor : IEquatable<ChannelCursor>
    {
        private readonly long _channelId;
        private readonly int _pts;
        private readonly DateTime _lastSyncedAt;

        public ChannelCursor(long channelId, int pts, DateTime lastSyncedAt)
        {
            if (channelId <= 0) throw new ArgumentOutOfRangeException("channelId", "channelId must be positive");
            if (pts < 0) throw new ArgumentOutOfRangeException("pts", "pts must be non-negative");
            _channelId = channelId;
            _pts = pts;
            _lastSyncedAt = lastSyncedAt;
        }

        public long ChannelId { get { return _channelId; } }
        public int Pts { get { return _pts; } }
        public DateTime LastSyncedAt { get { return _lastSyncedAt; } }

        public ChannelCursor WithPts(int newPts, DateTime nowUtc)
        {
            if (newPts < 0) throw new ArgumentOutOfRangeException("newPts");
            return new ChannelCursor(_channelId, newPts, nowUtc);
        }

        public ChannelCursor WithLastSyncedAt(DateTime nowUtc)
        {
            return new ChannelCursor(_channelId, _pts, nowUtc);
        }

        public bool Equals(ChannelCursor other)
        {
            if (ReferenceEquals(other, null)) return false;
            return _channelId == other._channelId && _pts == other._pts && _lastSyncedAt == other._lastSyncedAt;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ChannelCursor);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) + _channelId.GetHashCode();
                h = (h * 31) + _pts;
                return h;
            }
        }

        public override string ToString()
        {
            return "ChannelCursor(id=" + _channelId + " pts=" + _pts + ")";
        }
    }
}
