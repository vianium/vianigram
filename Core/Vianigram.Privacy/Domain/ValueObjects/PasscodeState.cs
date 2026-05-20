// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Privacy.Domain.ValueObjects
{
    /// <summary>
    /// Immutable snapshot of the on-device passcode configuration.
    ///
    /// <para><b>M3 isolation</b>: the actual hash + salt blob is held inside
    /// this VO, but is NEVER exposed via a public property. The application
    /// layer interacts with the hash exclusively through
    /// <see cref="VerifyAgainst"/>, which delegates to an
    /// <c>IPasscodeHasher</c> and never returns the raw bytes.</para>
    ///
    /// <para><b>Disabled state</b> — produced by <see cref="Disabled"/>: no
    /// hash, <see cref="Enabled"/> is false, and <see cref="VerifyAgainst"/>
    /// always rejects.</para>
    /// </summary>
    public sealed class PasscodeState
    {
        // Sentinel for the disabled state.
        public static readonly PasscodeState Disabled = new PasscodeState(
            enabled: false,
            kind: PasscodeKind.None,
            hashedSalt: new byte[0],
            lastUnlocked: DateTime.MinValue,
            autoLockMinutes: 0);

        private readonly bool _enabled;
        private readonly PasscodeKind _kind;
        // M3 isolation: this byte[] is private, never surfaced through a
        // property, never serialized in ToString. Only IPasscodeHasher touches
        // it via VerifyAgainst.
        private readonly byte[] _hashedSalt;
        private readonly DateTime _lastUnlocked;
        private readonly int _autoLockMinutes;

        public PasscodeState(
            bool enabled,
            PasscodeKind kind,
            byte[] hashedSalt,
            DateTime lastUnlocked,
            int autoLockMinutes)
        {
            _enabled = enabled;
            _kind = kind;
            _hashedSalt = hashedSalt == null ? new byte[0] : (byte[])hashedSalt.Clone();
            _lastUnlocked = lastUnlocked;
            _autoLockMinutes = autoLockMinutes < 0 ? 0 : autoLockMinutes;
        }

        public bool Enabled { get { return _enabled; } }
        public PasscodeKind Kind { get { return _kind; } }
        public DateTime LastUnlocked { get { return _lastUnlocked; } }
        public int AutoLockMinutes { get { return _autoLockMinutes; } }

        /// <summary>
        /// Returns the hashed-salt blob length only — exposed for telemetry
        /// without leaking material. Use this in logs / diagnostics.
        /// </summary>
        public int HashedSaltLength { get { return _hashedSalt.Length; } }

        /// <summary>
        /// Verify a candidate hash (computed by the active
        /// <c>IPasscodeHasher</c> over the user-typed PIN) against the stored
        /// hash, in constant time so a timing attack cannot distinguish
        /// "first byte correct" from "totally wrong". Internal — only the
        /// application handlers / hasher touch this.
        /// </summary>
        internal bool VerifyAgainst(byte[] candidateHash)
        {
            if (!_enabled) return false;
            if (candidateHash == null) return false;
            if (candidateHash.Length != _hashedSalt.Length) return false;

            int diff = 0;
            for (int i = 0; i < _hashedSalt.Length; i++)
            {
                diff |= _hashedSalt[i] ^ candidateHash[i];
            }
            return diff == 0;
        }

        /// <summary>
        /// Internal — return the stored hash as a defensive copy. The only
        /// supported caller is an <c>IPasscodeHasher</c> invoked from inside
        /// the Application layer; this is package-internal and never crosses
        /// the inbound boundary.
        /// </summary>
        internal byte[] GetHashedSaltSnapshot()
        {
            byte[] copy = new byte[_hashedSalt.Length];
            Array.Copy(_hashedSalt, copy, _hashedSalt.Length);
            return copy;
        }

        public PasscodeState WithLastUnlocked(DateTime when)
        {
            return new PasscodeState(_enabled, _kind, _hashedSalt, when, _autoLockMinutes);
        }

        public PasscodeState WithAutoLockMinutes(int minutes)
        {
            return new PasscodeState(_enabled, _kind, _hashedSalt, _lastUnlocked, minutes);
        }

        public override string ToString()
        {
            // Deliberately omit hashedSalt — never log secret material.
            return "PasscodeState(enabled=" + _enabled + " kind=" + _kind +
                   " autoLockMin=" + _autoLockMinutes + " hashLen=" + _hashedSalt.Length + ")";
        }
    }
}
