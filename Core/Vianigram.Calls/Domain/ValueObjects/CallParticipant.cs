// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// The remote peer of a 1:1 phone call. Telegram models this as
    /// <c>InputPhoneCall</c>'s admin/participant ids — one user is the
    /// initiator (admin), the other is the participant. From our local
    /// vantage point we always carry the OTHER user, plus the access hash
    /// the server expects on outbound RPCs.
    ///
    /// Group calls (voice chats) are explicitly out of scope per
    /// <c>docs/managed-architecture/07-calls.md §1</c>; that flow has its
    /// own context and aggregate.
    /// </summary>
    public struct CallParticipant : IEquatable<CallParticipant>
    {
        private readonly long _userId;
        private readonly long _accessHash;

        public CallParticipant(long userId, long accessHash)
        {
            if (userId <= 0) throw new ArgumentException("userId must be positive", "userId");
            _userId = userId;
            _accessHash = accessHash;
        }

        public long UserId { get { return _userId; } }
        public long AccessHash { get { return _accessHash; } }

        public bool Equals(CallParticipant other)
        {
            return _userId == other._userId && _accessHash == other._accessHash;
        }

        public override bool Equals(object obj)
        {
            return obj is CallParticipant && Equals((CallParticipant)obj);
        }

        public override int GetHashCode()
        {
            int h = _userId.GetHashCode();
            h = (h * 397) ^ _accessHash.GetHashCode();
            return h;
        }

        public override string ToString()
        {
            return "participant:user=" + _userId.ToString(CultureInfo.InvariantCulture)
                   + " hash=" + _accessHash.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static bool operator ==(CallParticipant a, CallParticipant b) { return a.Equals(b); }
        public static bool operator !=(CallParticipant a, CallParticipant b) { return !a.Equals(b); }
    }
}
