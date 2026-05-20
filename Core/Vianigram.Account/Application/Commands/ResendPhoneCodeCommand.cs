// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Application.Commands
{
    /// <summary>Request the next available code delivery method for the current phone auth flow.</summary>
    public sealed class ResendPhoneCodeCommand
    {
        public static readonly ResendPhoneCodeCommand Instance = new ResendPhoneCodeCommand();

        private ResendPhoneCodeCommand()
        {
        }
    }
}
