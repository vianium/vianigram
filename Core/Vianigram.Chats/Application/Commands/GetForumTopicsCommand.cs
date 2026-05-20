// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GetForumTopicsCommand.cs — Vianigram.Chats.Application.Commands
// Carries the inputs for GetForumTopicsHandler.

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Commands
{
    public sealed class GetForumTopicsCommand
    {
        public PeerId Channel { get; private set; }

        public GetForumTopicsCommand(PeerId channel)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (channel.Kind != PeerKind.Channel)
                throw new ArgumentException("forum topics live under a channel peer", "channel");
            Channel = channel;
        }
    }
}
