// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Notifications.Application.UseCases
{
    /// <summary>
    /// Sync per-scope notification settings from the server
    /// (<c>account.getNotifySettings#12b3ad31</c>) into the local
    /// <see cref="Vianigram.Notifications.Domain.Entities.NotificationProfile"/>
    /// aggregate.
    /// </summary>
    public sealed class SyncSettingsCommand
    {
        /// <summary>Singleton — there are no parameters today.</summary>
        public static readonly SyncSettingsCommand Default = new SyncSettingsCommand();

        private SyncSettingsCommand() { }
    }
}
