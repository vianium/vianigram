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
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Application.Handlers
{
    /// <summary>
    /// Handles <see cref="ChangePasscodeCommand"/>: verifies the old PIN,
    /// then issues a fresh enable with the new PIN. Composed from
    /// <see cref="VerifyPasscodeHandler"/> + <see cref="EnablePasscodeHandler"/>
    /// so the change path runs through the exact same hash / store
    /// mechanics as a fresh enable.
    /// </summary>
    internal sealed class ChangePasscodeHandler
    {
        private readonly VerifyPasscodeHandler _verify;
        private readonly EnablePasscodeHandler _enable;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public ChangePasscodeHandler(
            VerifyPasscodeHandler verify,
            EnablePasscodeHandler enable,
            ILogger log,
            IClock clock)
        {
            if (verify == null) throw new ArgumentNullException("verify");
            if (enable == null) throw new ArgumentNullException("enable");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _verify = verify;
            _enable = enable;
            _log = new TimestampedLogger(log, "Privacy.ChangePasscode");
            _clock = clock;
        }

        public async Task<Result<Unit, PrivacyError>> HandleAsync(ChangePasscodeCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, PrivacyError>.Fail(PrivacyError.Unknown("null command"));

            // Reject "change to the same PIN" early so we don't burn a hash
            // round on a no-op. Compares strings directly — they never leave
            // the application layer.
            if (string.Equals(cmd.OldPin, cmd.NewPin, StringComparison.Ordinal))
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.InvalidValue("new pin must differ from old pin"));
            }

            var newPinValidation = PasscodeValidator.ValidateOrNull(cmd.NewPin);
            if (newPinValidation != null) return Result<Unit, PrivacyError>.Fail(newPinValidation);

            var verifyResult = await _verify.HandleAsync(new VerifyPasscodeCommand(cmd.OldPin), ct).ConfigureAwait(false);
            if (verifyResult.IsFail) return Result<Unit, PrivacyError>.Fail(verifyResult.Error);
            if (!verifyResult.Value)
            {
                return Result<Unit, PrivacyError>.Fail(PrivacyError.PasscodeWrong());
            }

            // Verify-success staged a PasscodeUnlocked; the next enable will
            // stage a PasscodeChanged. Both events are drained inside the
            // delegated handlers, so the bus sees the natural sequence
            // (Unlocked → Changed) without us re-publishing here.
            return await _enable.HandleAsync(new EnablePasscodeCommand(cmd.NewPin), ct).ConfigureAwait(false);
        }
    }
}
