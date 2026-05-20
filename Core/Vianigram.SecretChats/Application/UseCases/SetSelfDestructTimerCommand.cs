// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SetSelfDestructTimerCommand.cs - Vianigram.SecretChats.Application.UseCases
// Command DTO for ISecretChatsApi.SetSelfDestructTimerAsync.

using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Application.UseCases
{
    /// <summary>
    /// Update the self-destruct (TTL) timer for an established secret chat.
    /// <see cref="Seconds"/> = 0 disables auto-destruct.
    /// </summary>
    internal sealed class SetSelfDestructTimerCommand
    {
        public SetSelfDestructTimerCommand(SecretChatId chatId, int seconds)
        {
            ChatId = chatId;
            Seconds = seconds;
        }

        public SecretChatId ChatId { get; private set; }
        public int Seconds { get; private set; }
    }
}
