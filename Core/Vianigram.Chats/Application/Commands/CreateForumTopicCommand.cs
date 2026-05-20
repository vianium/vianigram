// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CreateForumTopicCommand.cs — Vianigram.Chats.Application.Commands
// Carries the inputs for CreateForumTopicHandler.

using System;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Application.Commands
{
    public sealed class CreateForumTopicCommand
    {
        public PeerId Channel { get; private set; }
        public string Title { get; private set; }
        public string IconEmoji { get; private set; }

        public CreateForumTopicCommand(PeerId channel, string title, string iconEmoji)
        {
            if (channel == null) throw new ArgumentNullException("channel");
            if (channel.Kind != PeerKind.Channel)
                throw new ArgumentException("forum topics live under a channel peer", "channel");
            if (string.IsNullOrEmpty(title)) throw new ArgumentException("title required", "title");
            Channel = channel;
            Title = title;
            IconEmoji = iconEmoji ?? string.Empty;
        }
    }
}
