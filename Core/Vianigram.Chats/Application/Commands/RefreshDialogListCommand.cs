// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Chats.Application.Commands
{
    /// <summary>
    /// Re-fetch the dialog list from the start (empty cursor). Used on cold start,
    /// account switch, or explicit user pull-to-refresh.
    ///
    /// Empty payload by design — refresh has no parameters at the application layer;
    /// the limit is decided by the handler from a config constant.
    /// </summary>
    public sealed class RefreshDialogListCommand
    {
        public static readonly RefreshDialogListCommand Instance = new RefreshDialogListCommand();
        private RefreshDialogListCommand() { }
    }
}
