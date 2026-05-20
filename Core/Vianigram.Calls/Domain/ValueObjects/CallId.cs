// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// Server-assigned identifier for one phone call. Telegram allocates
    /// this value when responding to <c>phone.requestCall</c> and surfaces
    /// it on every subsequent <c>phoneCall*</c> constructor and on
    /// <c>updatePhoneCall</c>. Persists for the lifetime of the call (until
    /// <c>phone.discardCall</c>).
    ///
    /// Carried as <see cref="long"/>: Telegram's TL schema declares
    /// <c>phoneCall.id:long</c>. Identity-only — pair with the access hash
    /// that sits on the <see cref="CallSession"/> aggregate when issuing
    /// outbound RPCs.
    /// </summary>
    public struct CallId : IEquatable<CallId>
    {
        private readonly long _value;

        public CallId(long value)
        {
            _value = value;
        }

        public long Value { get { return _value; } }

        public bool Equals(CallId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is CallId && Equals((CallId)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "call:" + _value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(CallId a, CallId b) { return a.Equals(b); }
        public static bool operator !=(CallId a, CallId b) { return !a.Equals(b); }
    }
}
