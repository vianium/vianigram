// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Privacy.Application.UseCases;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.Entities;
using Vianigram.Privacy.Domain.ValueObjects;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Application.Handlers
{
    /// <summary>
    /// Handles <see cref="EnablePasscodeCommand"/>: validates the PIN,
    /// generates a fresh salt via <see cref="IPasscodeHasher"/>, computes the
    /// hash, persists the resulting <see cref="PasscodeState"/> through
    /// <see cref="IPasscodeStore"/>, and records the new state on the
    /// aggregate.
    ///
    /// <para>Replaces any existing passcode without prior verification —
    /// callers that need a confirm-old-PIN step use
    /// <see cref="ChangePasscodeHandler"/>.</para>
    /// </summary>
    internal sealed class EnablePasscodeHandler
    {
        private readonly IPasscodeStore _store;
        private readonly IPasscodeHasher _hasher;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public EnablePasscodeHandler(
            IPasscodeStore store,
            IPasscodeHasher hasher,
            PrivacyProfile profile,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (hasher == null) throw new ArgumentNullException("hasher");
            if (profile == null) throw new ArgumentNullException("profile");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _store = store;
            _hasher = hasher;
            _profile = profile;
            _bus = bus;
            _log = new TimestampedLogger(log, "Privacy.EnablePasscode");
            _clock = clock;
        }

        public async Task<Result<Unit, PrivacyError>> HandleAsync(EnablePasscodeCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("null command"));

            var validationError = PasscodeValidator.ValidateOrNull(cmd.Pin);
            if (validationError != null) return Result<Unit, PrivacyError>.Fail(validationError);

            try
            {
                byte[] salt = _hasher.GenerateSalt();
                byte[] hash = _hasher.ComputeHash(cmd.Pin, salt);

                // Pack salt + hash into a single blob — the stored state holds
                // the combined material so verify can recompute over the same
                // (salt, candidate) pair. The stub hasher prepends the salt;
                // the production PBKDF2 adapter follows the same convention.
                byte[] packed = PackSaltAndHash(salt, hash);

                var newState = new PasscodeState(
                    enabled: true,
                    kind: PasscodeKind.Pin,
                    hashedSalt: packed,
                    lastUnlocked: _clock.UtcNow,
                    autoLockMinutes: 0);

                var saveResult = await _store.SaveAsync(newState, ct).ConfigureAwait(false);
                if (saveResult.IsFail)
                {
                    _log.Warn("EnablePasscode store.SaveAsync failed: " + saveResult.Error);
                    return Result<Unit, PrivacyError>.Fail(saveResult.Error);
                }

                _profile.RecordPasscode(newState, _clock.UtcNow);
                HandlerEventBridge.Drain(_profile, _bus);

                // Best-effort: zero the volatile arrays we created. Stack /
                // heap pressure on Phone 8.1 is non-trivial and the GC is
                // generational; explicit clear is cheap insurance against
                // dump-style attacks.
                Array.Clear(hash, 0, hash.Length);
                Array.Clear(salt, 0, salt.Length);

                return Result<Unit, PrivacyError>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                _log.Warn("EnablePasscode threw: " + ex.Message);
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("EnablePasscode failed", ex));
            }
        }

        internal static byte[] PackSaltAndHash(byte[] salt, byte[] hash)
        {
            int sLen = salt == null ? 0 : salt.Length;
            int hLen = hash == null ? 0 : hash.Length;
            byte[] packed = new byte[4 + sLen + hLen];
            // First 4 bytes = salt length (big-endian) so we can recover the
            // boundary on verify. Big-endian by convention.
            packed[0] = (byte)((sLen >> 24) & 0xFF);
            packed[1] = (byte)((sLen >> 16) & 0xFF);
            packed[2] = (byte)((sLen >> 8) & 0xFF);
            packed[3] = (byte)(sLen & 0xFF);
            if (sLen > 0) Array.Copy(salt, 0, packed, 4, sLen);
            if (hLen > 0) Array.Copy(hash, 0, packed, 4 + sLen, hLen);
            return packed;
        }
    }
}
