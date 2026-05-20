// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Verify the old PIN, then atomically replace it with a new one. Fails
    /// with <see cref="Domain.PrivacyErrorKind.PasscodeWrong"/> when the old
    /// PIN does not match.
    /// </summary>
    public sealed class ChangePasscodeCommand
    {
        public string OldPin { get; private set; }
        public string NewPin { get; private set; }

        public ChangePasscodeCommand(string oldPin, string newPin)
        {
            OldPin = oldPin ?? string.Empty;
            NewPin = newPin ?? string.Empty;
        }
    }
}
