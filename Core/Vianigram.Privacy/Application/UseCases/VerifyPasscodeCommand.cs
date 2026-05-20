// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Verify a candidate PIN. Returns a boolean wrapped in a Result — true on
    /// match, false on mismatch (a mismatch is NOT a fail-result; verify is a
    /// query). Raises domain events both ways:
    /// <c>PasscodeUnlocked</c> on match, <c>PasscodeFailedAttempt</c> on
    /// mismatch.
    /// </summary>
    public sealed class VerifyPasscodeCommand
    {
        public string Pin { get; private set; }

        public VerifyPasscodeCommand(string pin)
        {
            Pin = pin ?? string.Empty;
        }
    }
}
