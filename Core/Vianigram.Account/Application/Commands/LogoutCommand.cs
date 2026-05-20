// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Application.Commands
{
    /// <summary>Drop the local auth_key and call auth.logOut server-side.</summary>
    public sealed class LogoutCommand
    {
        public static readonly LogoutCommand Instance = new LogoutCommand();

        private LogoutCommand()
        {
        }
    }
}
