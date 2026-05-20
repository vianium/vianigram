// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Local-only history page for ISecretChatsApi.LoadHistoryAsync.

using System.Collections.Generic;
using Vianigram.SecretChats.Domain.Entities;

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// Snapshot of a secret-chat history page.
    ///
    /// <para><b>Why a local shape (instead of <c>Vianigram.Messages.Domain.ValueObjects.MessagePage</c>):</b>
    /// secret-chat history lives entirely on the device — Telegram never
    /// stores E2E ciphertext server-side. The page item type is the
    /// SecretChats-owned <see cref="SecretMessage"/> aggregate, not the
    /// regular <c>Vianigram.Messages</c> Message entity. Defining the shape
    /// here keeps <c>Vianigram.SecretChats</c> from depending on
    /// <c>Vianigram.Messages</c>, preserving the bounded-context
    /// independence rule documented in
    /// <c>docs/managed-architecture/principles.md</c>.</para>
    ///
    /// <para>Messages are ordered <b>oldest-first</b> by send time so the
    /// natural list rendering reads top-to-bottom from the start of
    /// conversation. <see cref="HasMoreOlder"/> is true when the page begins
    /// at message 0 and the underlying session has additional rows older
    /// than <see cref="OldestRandomId"/>; today the in-memory store keeps
    /// the entire conversation in RAM so this flag is wired but typically
    /// false — a future encrypted-SQLite repository will use it.</para>
    /// </summary>
    public sealed class SecretMessagePage
    {
        private static readonly SecretMessage[] EmptyMessages = new SecretMessage[0];

        public SecretMessagePage(IList<SecretMessage> messages, bool hasMoreOlder, long? oldestRandomId)
        {
            Messages = messages != null ? CopyToReadOnly(messages) : EmptyMessages;
            HasMoreOlder = hasMoreOlder;
            OldestRandomId = oldestRandomId;
        }

        public IList<SecretMessage> Messages { get; private set; }
        public bool HasMoreOlder { get; private set; }
        public long? OldestRandomId { get; private set; }

        private static SecretMessage[] CopyToReadOnly(IList<SecretMessage> source)
        {
            var arr = new SecretMessage[source.Count];
            for (int i = 0; i < source.Count; i++) arr[i] = source[i];
            return arr;
        }
    }
}
