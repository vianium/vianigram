// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// The pts/qts/seq/date state — the heart of Telegram's update protocol.
    ///
    /// Per principle M6, this state is the EXCLUSIVE invariant of the Sync bounded
    /// context. No other context may read or write these counters; Sync exposes
    /// only typed domain events as its public surface.
    ///
    /// Field semantics (Telegram MTProto layer 214):
    /// - Pts: Persistent Timestamp — advances by ptsCount per "main box" update
    ///   (new messages, reads, deletes). Gaps require updates.getDifference.
    /// - Qts: Queue Timestamp — advances by 1 per "secret box" update
    ///   (encryptedMessage, encryptedChatRequested, etc.). Independent counter.
    /// - Seq: Date Sequence — advances by 1 per Updates/UpdatesCombined container
    ///   (NOT per individual Update inside). Used to order multi-update batches.
    /// - Date: Unix epoch seconds for the server's notion of "now". Monotonic
    ///   over a session; used as a hint for getDifference and to compute drift.
    ///
    /// Immutable: every transition creates a new instance via With* methods.
    /// </summary>
    public sealed class SyncCursor : IEquatable<SyncCursor>
    {
        private readonly int _pts;
        private readonly int _qts;
        private readonly int _seq;
        private readonly int _date;

        public SyncCursor(int pts, int qts, int seq, int date)
        {
            if (pts < 0) throw new ArgumentOutOfRangeException("pts", "pts must be non-negative");
            if (qts < 0) throw new ArgumentOutOfRangeException("qts", "qts must be non-negative");
            if (seq < 0) throw new ArgumentOutOfRangeException("seq", "seq must be non-negative");
            if (date < 0) throw new ArgumentOutOfRangeException("date", "date must be non-negative");
            _pts = pts;
            _qts = qts;
            _seq = seq;
            _date = date;
        }

        public int Pts { get { return _pts; } }
        public int Qts { get { return _qts; } }
        public int Seq { get { return _seq; } }
        public int Date { get { return _date; } }

        /// <summary>
        /// All-zero cursor — represents a cold-start state where the client has
        /// never observed any update. Valid input for updates.getState (which
        /// returns the full server cursor in response).
        /// </summary>
        public static SyncCursor Initial()
        {
            return new SyncCursor(0, 0, 0, 0);
        }

        public bool IsInitial
        {
            get { return _pts == 0 && _qts == 0 && _seq == 0 && _date == 0; }
        }

        public SyncCursor WithPts(int newPts)
        {
            if (newPts < 0) throw new ArgumentOutOfRangeException("newPts");
            return new SyncCursor(newPts, _qts, _seq, _date);
        }

        public SyncCursor WithQts(int newQts)
        {
            if (newQts < 0) throw new ArgumentOutOfRangeException("newQts");
            return new SyncCursor(_pts, newQts, _seq, _date);
        }

        public SyncCursor WithSeqAndDate(int newSeq, int newDate)
        {
            if (newSeq < 0) throw new ArgumentOutOfRangeException("newSeq");
            if (newDate < 0) throw new ArgumentOutOfRangeException("newDate");
            return new SyncCursor(_pts, _qts, newSeq, newDate);
        }

        public SyncCursor WithDate(int newDate)
        {
            if (newDate < 0) throw new ArgumentOutOfRangeException("newDate");
            return new SyncCursor(_pts, _qts, _seq, newDate);
        }

        public SyncCursor With(int pts, int qts, int seq, int date)
        {
            return new SyncCursor(pts, qts, seq, date);
        }

        public bool Equals(SyncCursor other)
        {
            if (ReferenceEquals(other, null)) return false;
            return _pts == other._pts && _qts == other._qts && _seq == other._seq && _date == other._date;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as SyncCursor);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = (h * 31) + _pts;
                h = (h * 31) + _qts;
                h = (h * 31) + _seq;
                h = (h * 31) + _date;
                return h;
            }
        }

        public override string ToString()
        {
            return "SyncCursor(pts=" + _pts + " qts=" + _qts + " seq=" + _seq + " date=" + _date + ")";
        }
    }
}
