// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// 64-bit key fingerprint derived from the SHA1 digest of the
    /// negotiated <c>auth_key</c> (the last 8 bytes of <c>SHA1(auth_key)</c>
    /// interpreted as a little-endian <see cref="long"/>). Telegram sends the
    /// fingerprint on every <c>encryptedMessage</c> and uses it during
    /// <c>messages.acceptEncryption</c>; both peers must compute the same
    /// value for a session to be considered established.
    ///
    /// The fingerprint is also surfaced to the user via the visual
    /// <see cref="EmojiKey"/> rendering for out-of-band verification.
    ///
    /// Note: this VO carries only the 8-byte fingerprint, NOT the underlying
    /// 256-byte auth_key. Per principles.md §M3 (key material isolation),
    /// raw key bytes never live in a managed value object — they stay inside
    /// <see cref="AuthKey"/>'s opaque handle / inside the native crypto
    /// vault. The fingerprint is a non-secret derivative.
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
            // Hex form, low-endian of the 8 bytes Telegram serializes — useful
            // for diagnostics. UI uses the EmojiKey rendering for users.
            return "fp:" + _value.ToString("x16", CultureInfo.InvariantCulture);
        }

        public static bool operator ==(KeyFingerprint a, KeyFingerprint b) { return a.Equals(b); }
        public static bool operator !=(KeyFingerprint a, KeyFingerprint b) { return !a.Equals(b); }
    }
}
