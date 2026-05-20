// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Application.UseCases
{
    /// <summary>
    /// Accept an incoming <c>encryptedChatRequested</c> already persisted to
    /// the local repository. Generates <c>b</c>, computes <c>g^b</c> and the
    /// shared <c>auth_key</c>, and issues
    /// <c>messages.acceptEncryption</c> with the freshly-derived key
    /// fingerprint.
    /// </summary>
    public sealed class AcceptSecretChatCommand
    {
        public SecretChatId ChatId { get; private set; }

        public AcceptSecretChatCommand(SecretChatId chatId)
        {
            ChatId = chatId;
        }
    }
}
