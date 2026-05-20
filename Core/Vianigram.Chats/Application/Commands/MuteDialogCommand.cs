// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Commands
{
    /// <summary>
    /// Mute a dialog for an optional window. Null <see cref="Until"/> means
    /// "muted forever" (Telegram convention: mute_until = INT32_MAX).
    /// </summary>
    public sealed class MuteDialogCommand
    {
        public PeerId Peer { get; private set; }
        public TimeSpan? Until { get; private set; }

        public MuteDialogCommand(PeerId peer, TimeSpan? until)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            if (until.HasValue && until.Value <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException("until", "must be positive when present");
            Peer = peer;
            Until = until;
        }
    }
}
