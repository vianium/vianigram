// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Issue <c>account.getAuthorizations#e320c158</c> and refresh the
    /// aggregate's session cache. The command carries no payload — there is a
    /// single global authorizations list per account.
    /// </summary>
    public sealed class GetActiveSessionsCommand
    {
        public static readonly GetActiveSessionsCommand Instance = new GetActiveSessionsCommand();
        private GetActiveSessionsCommand() { }
    }
}
