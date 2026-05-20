// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Application.Commands
{
    /// <summary>
    /// Discard the current cursor and bootstrap from scratch. Used on:
    /// - auth-key rotation (the prior session validity is gone),
    /// - server returning updatesTooLong,
    /// - manual user-triggered "force resync" diagnostic.
    /// </summary>
    public sealed class ResyncCommand
    {
        public static readonly ResyncCommand Instance = new ResyncCommand();
        private ResyncCommand() { }
    }
}
