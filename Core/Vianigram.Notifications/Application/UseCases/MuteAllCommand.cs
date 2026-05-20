// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Notifications.Application.UseCases
{
    /// <summary>
    /// Mute every peer (and global) until the supplied UTC instant. When
    /// <see cref="MuteUntilUtc"/> is null the rules are muted forever
    /// (<see cref="DateTime.MaxValue"/>).
    ///
    /// Each affected peer triggers an
    /// <c>account.updateNotifySettings#84be5b93</c> call so the server stays
    /// in sync.
    /// </summary>
    public sealed class MuteAllCommand
    {
        public DateTime? MuteUntilUtc { get; private set; }

        public MuteAllCommand(DateTime? muteUntilUtc)
        {
            MuteUntilUtc = muteUntilUtc;
        }

        public static MuteAllCommand Forever
        {
            get { return new MuteAllCommand(null); }
        }
    }
}
