// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GetGroupInfoCommand.cs — Vianigram.Chats.Application.Commands
// Carries the inputs for GetGroupInfoHandler.

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Commands
{
    public sealed class GetGroupInfoCommand
    {
        public PeerId Peer { get; private set; }

        public GetGroupInfoCommand(PeerId peer)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            Peer = peer;
        }
    }
}
