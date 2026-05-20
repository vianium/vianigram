// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Application.Commands
{
    /// <summary>
    /// Cold-start the sync engine: load persisted cursor (if any), call
    /// updates.getState to seed if cursor was empty, then run getDifference to
    /// catch up on anything missed while the app was offline.
    ///
    /// Idempotent — replays after success are no-ops.
    /// </summary>
    public sealed class BootstrapSyncCommand
    {
        public static readonly BootstrapSyncCommand Instance = new BootstrapSyncCommand();
        private BootstrapSyncCommand() { }
    }
}
