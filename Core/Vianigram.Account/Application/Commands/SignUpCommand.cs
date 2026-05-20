// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Application.Commands
{
    /// <summary>Complete a new-user phone auth flow via auth.signUp.</summary>
    public sealed class SignUpCommand
    {
        public string FirstName { get; private set; }
        public string LastName { get; private set; }

        public SignUpCommand(string firstName, string lastName)
        {
            FirstName = firstName;
            LastName = lastName;
        }
    }
}
