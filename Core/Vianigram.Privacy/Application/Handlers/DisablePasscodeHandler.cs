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
    /// Handles <see cref="DisablePasscodeCommand"/>: verifies the supplied
    /// PIN, then clears the local store and replaces the aggregate state with
    /// <see cref="PasscodeState.Disabled"/>.
    ///
    /// <para>Verify is delegated to <see cref="VerifyPasscodeHandler"/> for a
    /// single timing-safe code path; the staged
    /// <see cref="Domain.Events.PasscodeUnlocked"/> /
    /// <see cref="Domain.Events.PasscodeFailedAttempt"/> events from that
    /// path bubble naturally — handlers compose by call.</para>
    /// </summary>
    internal sealed class DisablePasscodeHandler
    {
        private readonly IPasscodeStore _store;
        private readonly VerifyPasscodeHandler _verify;
        private readonly PrivacyProfile _profile;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public DisablePasscodeHandler(
            IPasscodeStore store,
            VerifyPasscodeHandler verify,
            PrivacyProfile profile,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (verify == null) throw new ArgumentNullException("verify");
            if (profile == null) throw new ArgumentNullException("profile");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _store = store;
            _verify = verify;
            _profile = profile;
            _bus = bus;
            _log = new TimestampedLogger(log, "Privacy.DisablePasscode");
            _clock = clock;
        }

        public async Task<Result<Unit, PrivacyError>> HandleAsync(DisablePasscodeCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("null command"));

            // Already disabled? Trivially success.
            if (!_profile.IsPasscodeEnabled)
            {
                return Result<Unit, PrivacyError>.Ok(Unit.Value);
            }

            var verifyResult = await _verify.HandleAsync(new VerifyPasscodeCommand(cmd.Pin), ct).ConfigureAwait(false);
            if (verifyResult.IsFail)
            {
                return Result<Unit, PrivacyError>.Fail(verifyResult.Error);
            }
            if (!verifyResult.Value)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.PasscodeWrong());
            }

            try
            {
                var clearResult = await _store.ClearAsync(ct).ConfigureAwait(false);
                if (clearResult.IsFail)
                {
                    _log.Warn("DisablePasscode store.ClearAsync failed: " + clearResult.Error);
                    return Result<Unit, PrivacyError>.Fail(clearResult.Error);
                }

                _profile.RecordPasscode(PasscodeState.Disabled, _clock.UtcNow);
                HandlerEventBridge.Drain(_profile, _bus);
                return Result<Unit, PrivacyError>.Ok(Unit.Value);
            }
            catch (Exception ex)
            {
                _log.Warn("DisablePasscode threw: " + ex.Message);
                return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("DisablePasscode failed", ex));
            }
        }
    }
}
