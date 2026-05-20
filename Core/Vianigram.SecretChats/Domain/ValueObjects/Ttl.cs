// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// Self-destruct lifetime for a secret chat. Two flavors share the same
    /// VO: <em>session</em> TTL applied per-message via
    /// <c>decryptedMessageActionSetMessageTTL</c>, and <em>per-message</em>
    /// TTL on individual <c>decryptedMessage</c> envelopes.
    ///
    /// <para>
    /// Wire format: TL <c>int</c> seconds. <c>0</c> means "no TTL" — the
    /// message is retained until manually deleted. The maximum is whatever
    /// fits in a positive int32, but UI typically caps at one week
    /// (604800 s).
    /// </para>
    ///
    /// <para>
    /// We model TTL as an integer-seconds VO instead of <see cref="TimeSpan"/>
    /// so the wire encoding is unambiguous (no fractional seconds, no DST
    /// surprises).
    /// </para>
    /// </summary>
    public struct Ttl : IEquatable<Ttl>
    {
        public static readonly Ttl None = new Ttl(0);

        private readonly int _seconds;

        public Ttl(int seconds)
        {
            if (seconds < 0) throw new ArgumentOutOfRangeException("seconds", "TTL seconds must be >= 0");
            _seconds = seconds;
        }

        public int Seconds { get { return _seconds; } }
        public bool HasValue { get { return _seconds > 0; } }

        public TimeSpan ToTimeSpan()
        {
            return TimeSpan.FromSeconds(_seconds);
        }

        public static Ttl FromTimeSpan(TimeSpan ts)
        {
            if (ts.TotalSeconds < 0) return None;
            long s = (long)ts.TotalSeconds;
            if (s > int.MaxValue) s = int.MaxValue;
            return new Ttl((int)s);
        }

        public bool Equals(Ttl other) { return _seconds == other._seconds; }
        public override bool Equals(object obj) { return obj is Ttl && Equals((Ttl)obj); }
        public override int GetHashCode() { return _seconds.GetHashCode(); }
        public override string ToString()
        {
            return _seconds == 0 ? "ttl:none" : ("ttl:" + _seconds.ToString(CultureInfo.InvariantCulture) + "s");
        }
        public static bool operator ==(Ttl a, Ttl b) { return a.Equals(b); }
        public static bool operator !=(Ttl a, Ttl b) { return !a.Equals(b); }
    }
}
