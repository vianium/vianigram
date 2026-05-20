// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CreateChannelCommand.cs — Vianigram.Chats.Application.Commands
// Carries the inputs for CreateChannelHandler.

using System;

namespace Vianigram.Chats.Application.Commands
{
    public sealed class CreateChannelCommand
    {
        public string Title { get; private set; }
        public string Description { get; private set; }
        public bool IsPublic { get; private set; }
        public string Username { get; private set; }

        public CreateChannelCommand(string title, string description, bool isPublic, string username)
        {
            if (string.IsNullOrEmpty(title)) throw new ArgumentException("title required", "title");
            Title = title;
            Description = description ?? string.Empty;
            IsPublic = isPublic;
            Username = username ?? string.Empty;
        }
    }
}
