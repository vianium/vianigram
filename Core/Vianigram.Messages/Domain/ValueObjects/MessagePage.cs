// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Messages.Domain.Entities;

namespace Vianigram.Messages.Domain.ValueObjects
{
    /// <summary>
    /// Snapshot of a history page returned by <c>messages.getHistory</c>.
    /// Messages are ordered newest-first (descending by server id), matching
    /// Telegram's wire convention.
    /// </summary>
    public sealed class MessagePage
    {
        private static readonly Message[] EmptyMessages = new Message[0];

        public MessagePage(IList<Message> messages, bool hasMoreOlder, long? oldestKnownMessageId)
        {
            Messages = messages != null ? CopyToReadOnly(messages) : EmptyMessages;
            HasMoreOlder = hasMoreOlder;
            OldestKnownMessageId = oldestKnownMessageId;
        }

        public IList<Message> Messages { get; private set; }
        public bool HasMoreOlder { get; private set; }
        public long? OldestKnownMessageId { get; private set; }

        private static Message[] CopyToReadOnly(IList<Message> source)
        {
            var arr = new Message[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }
}
