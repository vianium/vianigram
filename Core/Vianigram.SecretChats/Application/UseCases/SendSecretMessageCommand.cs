// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Application.UseCases
{
    /// <summary>
    /// Encrypt a UTF-8 text body with the session's <c>auth_key</c> via
    /// AES-256-IGE and send it as <c>messages.sendEncrypted</c>.
    /// </summary>
    public sealed class SendSecretMessageCommand
    {
        public SecretChatId ChatId { get; private set; }
        public string Text { get; private set; }
        public Ttl Ttl { get; private set; }

        public SendSecretMessageCommand(SecretChatId chatId, string text, Ttl ttl)
        {
            ChatId = chatId;
            Text = text ?? string.Empty;
            Ttl = ttl;
        }

        public SendSecretMessageCommand(SecretChatId chatId, string text)
            : this(chatId, text, Vianigram.SecretChats.Domain.ValueObjects.Ttl.None)
        {
        }
    }
}
