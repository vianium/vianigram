// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Application.Commands
{
    /// <summary>Submit the 2FA password (SRP-2048) via auth.checkPassword.</summary>
    public sealed class SubmitTwoFaPasswordCommand
    {
        public string Password { get; private set; }

        public SubmitTwoFaPasswordCommand(string password)
        {
            Password = password;
        }
    }
}
