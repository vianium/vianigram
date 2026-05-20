// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// 64-bit key fingerprint derived from the SHA1 digest of the
    /// negotiated <c>auth_key</c> (the last 8 bytes of <c>SHA1(auth_key)</c>
    /// interpreted as a little-endian <see cref="long"/>). Telegram echoes
    /// the fingerprint on the established <c>phoneCall</c>; both peers must
    /// compute the same value or the call MUST be aborted with
    /// <see cref="DiscardReason.ProtocolError"/> per the security guidance
    /// in TDLib's PhoneCallManager.
    ///
    /// Surfaced to the user via 4-emoji or QR rendering for out-of-band
    /// verification. The actual rendering lives in <c>VianiumVoIP</c>
    /// (WinMD); this VO carries only the non-secret 8-byte digest.
    ///
    /// Note: per principles.md §M3 (key material isolation), raw key bytes
    /// never live in a managed value object; they stay inside the native
    /// crypto vault. The fingerprint is a non-secret derivative.
    /// </summary>
    public struct KeyFingerprint : IEquatable<KeyFingerprint>
    {
        private readonly long _value;

        public KeyFingerprint(long value)
        {
            _value = value;
        }

        public long Value { get { return _value; } }

        public bool Equals(KeyFingerprint other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is KeyFingerprint && Equals((KeyFingerprint)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "fp:" + _value.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static bool operator ==(KeyFingerprint a, KeyFingerprint b) { return a.Equals(b); }
        public static bool operator !=(KeyFingerprint a, KeyFingerprint b) { return !a.Equals(b); }
    }
}
