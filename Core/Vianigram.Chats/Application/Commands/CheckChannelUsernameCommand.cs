// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CheckChannelUsernameCommand.cs — Vianigram.Chats.Application.Commands
// Carries the inputs for CheckChannelUsernameHandler.

using System;

namespace Vianigram.Chats.Application.Commands
{
    public sealed class CheckChannelUsernameCommand
    {
        public string Username { get; private set; }

        public CheckChannelUsernameCommand(string username)
        {
            if (string.IsNullOrEmpty(username)) throw new ArgumentException("username required", "username");
            Username = username;
        }
    }
}
