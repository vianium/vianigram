// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="ISecretChatsApi.MessageReceived"/>
    /// whenever an inbound <c>encryptedMessage</c> has been successfully
    /// decrypted, fingerprint-verified, and committed to history.
    /// </summary>
    public sealed class SecretMessageReceivedEventArgs : EventArgs
    {
        public SecretChatId ChatId { get; private set; }
        public long RandomId { get; private set; }
        public DateTime At { get; private set; }

        public SecretMessageReceivedEventArgs(SecretChatId chatId, long randomId, DateTime at)
        {
            ChatId = chatId;
            RandomId = randomId;
            At = at;
        }
    }
}
