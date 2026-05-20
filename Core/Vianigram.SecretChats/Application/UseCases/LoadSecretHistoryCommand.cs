// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LoadSecretHistoryCommand.cs - Vianigram.SecretChats.Application.UseCases
// Command DTO for ISecretChatsApi.LoadHistoryAsync.

using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Application.UseCases
{
    /// <summary>
    /// Load a page of locally-stored secret-chat history. Pure local read;
    /// secret ciphertext is never server-stored.
    /// </summary>
    internal sealed class LoadSecretHistoryCommand
    {
        public LoadSecretHistoryCommand(SecretChatId chatId, long? offsetMsgId, int limit)
        {
            ChatId = chatId;
            OffsetMsgId = offsetMsgId;
            Limit = limit;
        }

        public SecretChatId ChatId { get; private set; }
        public long? OffsetMsgId { get; private set; }
        public int Limit { get; private set; }
    }
}
