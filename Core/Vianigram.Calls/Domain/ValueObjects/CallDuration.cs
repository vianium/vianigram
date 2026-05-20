// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// Wall-clock duration of a call from <see cref="CallSessionState.Active"/>
    /// to <see cref="CallSessionState.Discarded"/>, expressed in seconds
    /// (Telegram's wire format for <c>phone.discardCall.duration:int</c>).
    ///
    /// Created via <see cref="FromSeconds"/> when discarding a call so the
    /// value handed to the server is identical to the one persisted on the
    /// aggregate; created via <see cref="FromInterval"/> when the
    /// application layer derives it from <c>StartedAt → DiscardedAt</c>.
    ///
    /// Negative durations are clamped to zero to keep the wire payload
    /// well-formed if a clock skew briefly produces a negative interval.
    /// </summary>
    public struct CallDuration : IEquatable<CallDuration>
    {
        private readonly int _seconds;

        public CallDuration(int seconds)
        {
            _seconds = seconds < 0 ? 0 : seconds;
        }

        public int Seconds { get { return _seconds; } }
        public TimeSpan ToTimeSpan() { return TimeSpan.FromSeconds(_seconds); }

        public static CallDuration Zero { get { return new CallDuration(0); } }

        public static CallDuration FromSeconds(int seconds)
        {
            return new CallDuration(seconds);
        }

        public static CallDuration FromInterval(DateTime startUtc, DateTime endUtc)
        {
            TimeSpan delta = endUtc - startUtc;
            double total = delta.TotalSeconds;
            if (total <= 0) return Zero;
            if (total > int.MaxValue) return new CallDuration(int.MaxValue);
            return new CallDuration((int)total);
        }

        public bool Equals(CallDuration other)
        {
            return _seconds == other._seconds;
        }

        public override bool Equals(object obj)
        {
            return obj is CallDuration && Equals((CallDuration)obj);
        }

        public override int GetHashCode()
        {
            return _seconds.GetHashCode();
        }

        public override string ToString()
        {
            return _seconds.ToString(CultureInfo.InvariantCulture) + "s";
        }

        public static bool operator ==(CallDuration a, CallDuration b) { return a.Equals(b); }
        public static bool operator !=(CallDuration a, CallDuration b) { return !a.Equals(b); }
    }
}
