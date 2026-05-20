// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Issue <c>auth.resetAuthorizations#9fab0d1a</c> — terminates every
    /// non-current session associated with the account. There is no payload.
    /// </summary>
    public sealed class TerminateAllOtherSessionsCommand
    {
        public static readonly TerminateAllOtherSessionsCommand Instance = new TerminateAllOtherSessionsCommand();
        private TerminateAllOtherSessionsCommand() { }
    }
}
