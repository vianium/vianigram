// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Issue <c>account.resetAuthorization#df77f3bc</c> for a single session
    /// hash. The handler refuses to terminate the session marked
    /// <c>IsCurrent</c> in the cache (server would reject anyway with
    /// FRESH_RESET_AUTHORISATION_FORBIDDEN — pre-empted here for a faster /
    /// typed error).
    /// </summary>
    public sealed class TerminateSessionCommand
    {
        public long Hash { get; private set; }

        public TerminateSessionCommand(long hash)
        {
            Hash = hash;
        }
    }
}
