// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.SecretChats.Domain.ValueObjects
{
    /// <summary>
    /// Telegram-server-assigned identifier for an encrypted chat. The server
    /// allocates this when responding to <c>messages.requestEncryption</c> and
    /// surfaces it on every <c>encryptedChat*</c> / <c>encryptedMessage</c>
    /// constructor.
    ///
    /// Defined locally per context to keep the Secret Chats ubiquitous
    /// language independent from other contexts (Account/Contacts/Messages
    /// each carry their own typed identifiers).
    ///
    /// Note: in TL the chat_id is <c>int</c>, so we hold an <see cref="int"/>
    /// here even though Telegram is migrating other id surfaces to
    /// <c>long</c>; the secret-chat schema keeps <c>int</c>.
    /// </summary>
    public struct SecretChatId : IEquatable<SecretChatId>
    {
        private readonly int _value;

        public SecretChatId(int value)
        {
            // 0 is reserved for "unset"; negative values can flow from the
            // server during DH for placeholder rows but we accept them so the
            // caller can persist them.
            _value = value;
        }

        public int Value { get { return _value; } }

        public bool Equals(SecretChatId other)
        {
            return _value == other._value;
        }

        public override bool Equals(object obj)
        {
            return obj is SecretChatId && Equals((SecretChatId)obj);
        }

        public override int GetHashCode()
        {
            return _value.GetHashCode();
        }

        public override string ToString()
        {
            return "secret_chat:" + _value.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(SecretChatId a, SecretChatId b) { return a.Equals(b); }
        public static bool operator !=(SecretChatId a, SecretChatId b) { return !a.Equals(b); }
    }
}
