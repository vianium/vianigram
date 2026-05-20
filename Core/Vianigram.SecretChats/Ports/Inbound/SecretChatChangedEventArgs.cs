// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.SecretChats.Domain.ValueObjects;

namespace Vianigram.SecretChats.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="ISecretChatsApi.SessionChanged"/>
    /// whenever a secret session transitions through its lifecycle. Mirrors
    /// the kernel-bus events (<c>SecretChatRequested</c>,
    /// <c>SecretChatAccepted</c>, <c>SecretChatEstablished</c>,
    /// <c>SecretChatDiscarded</c>, <c>KeyFingerprintMismatch</c>,
    /// <c>KeyRekeyed</c>) in a CLR-event shape so XAML/UI layers that don't
    /// take an <c>IEventBus</c> dependency can still subscribe.
    /// </summary>
    public sealed class SecretChatChangedEventArgs : EventArgs
    {
        public enum ChangeReason
        {
            Requested = 0,
            Accepted = 1,
            Established = 2,
            Discarded = 3,
            FingerprintMismatch = 4,
            Rekeyed = 5
        }

        public ChangeReason Reason { get; private set; }
        public SecretChatId ChatId { get; private set; }
        public DateTime At { get; private set; }

        public SecretChatChangedEventArgs(ChangeReason reason, SecretChatId chatId, DateTime at)
        {
            Reason = reason;
            ChatId = chatId;
            At = at;
        }
    }
}
