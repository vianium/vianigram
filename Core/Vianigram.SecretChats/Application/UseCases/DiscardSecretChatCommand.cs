// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Application.UseCases
{
    /// <summary>
    /// Discard a secret chat — calls <c>messages.discardEncryption</c>, wipes
    /// the local <c>auth_key</c>, and stages
    /// <see cref="Domain.Events.SecretChatDiscarded"/>.
    /// </summary>
    public sealed class DiscardSecretChatCommand
    {
        public SecretChatId ChatId { get; private set; }
        public DiscardReason Reason { get; private set; }
        public bool DeleteHistory { get; private set; }

        public DiscardSecretChatCommand(SecretChatId chatId, DiscardReason reason, bool deleteHistory)
        {
            ChatId = chatId;
            Reason = reason;
            DeleteHistory = deleteHistory;
        }

        public DiscardSecretChatCommand(SecretChatId chatId)
            : this(chatId, DiscardReason.LocalUserDiscarded, /*deleteHistory*/ true)
        {
        }
    }
}
