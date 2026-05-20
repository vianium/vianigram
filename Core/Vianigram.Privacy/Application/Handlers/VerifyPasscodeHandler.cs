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
    /// Handles <see cref="VerifyPasscodeCommand"/>: recomputes the hash for
    /// the candidate PIN over the stored salt and constant-time-compares
    /// against the stored hash. Returns <c>Ok(true)</c> on match,
    /// <c>Ok(false)</c> on mismatch, and a fail-result only for storage /
    /// system errors. Raises <see cref="Domain.Events.PasscodeUnlocked"/> on
    /// match and <see cref="Domain.Events.PasscodeFailedAttempt"/> on
    /// mismatch.
    /// </summary>
    internal sealed class VerifyPasscodeHandler
    {
        private readonly IPasscodeStore _store;
        private readonly IPasscodeHasher _hasher;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public VerifyPasscodeHandler(
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
            _log = new TimestampedLogger(log, "Privacy.VerifyPasscode");
            _clock = clock;
        }

        public async Task<Result<bool, PrivacyError>> HandleAsync(VerifyPasscodeCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<bool, PrivacyError>.Fail(PrivacyError.Unknown("null command"));

            // An empty PIN never matches; pre-empt to avoid burning a hash
            // computation. Treated as "wrong" rather than "invalid" so the
            // caller's UX collapses both to the same retry path.
            if (string.IsNullOrEmpty(cmd.Pin))
            {
                _profile.RecordPasscodeFailedAttempt(_clock.UtcNow);
                HandlerEventBridge.Drain(_profile, _bus);
                return Result<bool, PrivacyError>.Ok(false);
            }

            try
            {
                var loadResult = await _store.LoadAsync(ct).ConfigureAwait(false);
                if (loadResult.IsFail)
                {
                    _log.Warn("VerifyPasscode store.LoadAsync failed: " + loadResult.Error);
                    return Result<bool, PrivacyError>.Fail(loadResult.Error);
                }

                PasscodeState state = loadResult.Value;
                if (!state.Enabled)
                {
                    // No passcode configured — verify trivially succeeds.
                    _profile.RecordPasscodeUnlocked(_clock.UtcNow);
                    HandlerEventBridge.Drain(_profile, _bus);
                    return Result<bool, PrivacyError>.Ok(true);
                }

                byte[] packed = state.GetHashedSaltSnapshot();
                byte[] salt;
                byte[] storedHash;
                if (!PasscodeMaterial.TryUnpack(packed, out salt, out storedHash))
                {
                    _log.Warn("VerifyPasscode unpack failed; treating as miss");
                    _profile.RecordPasscodeFailedAttempt(_clock.UtcNow);
                    HandlerEventBridge.Drain(_profile, _bus);
                    return Result<bool, PrivacyError>.Ok(false);
                }

                byte[] candidate = _hasher.ComputeHash(cmd.Pin, salt);
                bool match = PasscodeMaterial.ConstantTimeEquals(storedHash, candidate);

                // Zero the volatile arrays.
                Array.Clear(candidate, 0, candidate.Length);
                Array.Clear(storedHash, 0, storedHash.Length);
                Array.Clear(salt, 0, salt.Length);
                Array.Clear(packed, 0, packed.Length);

                if (match)
                {
                    _profile.RecordPasscodeUnlocked(_clock.UtcNow);
                }
                else
                {
                    _profile.RecordPasscodeFailedAttempt(_clock.UtcNow);
                }
                HandlerEventBridge.Drain(_profile, _bus);
                return Result<bool, PrivacyError>.Ok(match);
            }
            catch (Exception ex)
            {
                _log.Warn("VerifyPasscode threw: " + ex.Message);
                return Result<bool, PrivacyError>.Fail(PrivacyError.Unknown("VerifyPasscode failed", ex));
            }
        }
    }
}
