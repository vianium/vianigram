// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Cross-context projection of a Telegram User constructor. Carried by
    /// updates#74ae4240 and updates.difference responses so downstream contexts
    /// (Contacts, Chats) can hydrate without an extra round-trip.
    ///
    /// Stub — only the fields a sync downstream consumer needs. Fully detailed
    /// user profile is owned by Contacts.
    /// </summary>
    public sealed class UserStub
    {
        public UserStub(long userId, long accessHash, string firstName, string lastName, string username, string phone, bool isBot, bool isContact, bool isSelf)
        {
            UserId = userId;
            AccessHash = accessHash;
            FirstName = firstName ?? string.Empty;
            LastName = lastName ?? string.Empty;
            Username = username ?? string.Empty;
            Phone = phone ?? string.Empty;
            IsBot = isBot;
            IsContact = isContact;
            IsSelf = isSelf;
        }

        public long UserId { get; private set; }
        public long AccessHash { get; private set; }
        public string FirstName { get; private set; }
        public string LastName { get; private set; }
        public string Username { get; private set; }
        public string Phone { get; private set; }
        public bool IsBot { get; private set; }
        public bool IsContact { get; private set; }
        public bool IsSelf { get; private set; }
    }
}
