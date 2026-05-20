// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CreateGroupCommand.cs — Vianigram.Chats.Application.Commands
// Carries the inputs for CreateGroupHandler.

using System;
using System.Collections.Generic;

namespace Vianigram.Chats.Application.Commands
{
    public sealed class CreateGroupCommand
    {
        public string Title { get; private set; }
        public IList<long> UserIds { get; private set; }

        public CreateGroupCommand(string title, IList<long> userIds)
        {
            if (string.IsNullOrEmpty(title)) throw new ArgumentException("title required", "title");
            if (userIds == null) throw new ArgumentNullException("userIds");
            Title = title;
            UserIds = userIds;
        }
    }
}
