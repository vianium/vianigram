// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Application.UseCases
{
    /// <summary>
    /// Apply a single inbound <c>encryptedMessage</c> already routed to this
    /// context by <c>UpdateSecretChatHandler</c> (or the SmokeTests harness).
    /// The handler decrypts via AES-256-IGE, validates the key fingerprint,
    /// parses the inner <c>decryptedMessage</c>, and commits to history.
    /// </summary>
    public sealed class ReceiveSecretMessageCommand
    {
        public SecretChatId ChatId { get; private set; }
        public long RandomId { get; private set; }
        public DateTime ServerDate { get; private set; }
        public byte[] EncryptedPayload { get; private set; }

        public ReceiveSecretMessageCommand(SecretChatId chatId, long randomId, DateTime serverDate, byte[] encryptedPayload)
        {
            ChatId = chatId;
            RandomId = randomId;
            ServerDate = serverDate;
            EncryptedPayload = encryptedPayload ?? new byte[0];
        }
    }
}
