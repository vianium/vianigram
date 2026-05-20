// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Commands
{
    public sealed class UnpinDialogCommand
    {
        public PeerId Peer { get; private set; }

        public UnpinDialogCommand(PeerId peer)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
        }
    }
}
