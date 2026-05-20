// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Domain.ValueObjects
{
    public enum ChatStubKind
    {
        Empty = 0,
        BasicGroup = 1,
        Channel = 2,
        Supergroup = 3,
        Forbidden = 4
    }

    /// <summary>
    /// Cross-context projection of a Telegram Chat constructor (chat / channel /
    /// chatForbidden / channelForbidden / chatEmpty). Hydrates downstream consumers
    /// (Chats, Messages) with name+access_hash without an extra getFullChat call.
    /// </summary>
    public sealed class ChatStub
    {
        public ChatStub(ChatStubKind kind, long chatId, long accessHash, string title, int participantsCount)
        {
            Kind = kind;
            ChatId = chatId;
            AccessHash = accessHash;
            Title = title ?? string.Empty;
            ParticipantsCount = participantsCount;
        }

        public ChatStubKind Kind { get; private set; }
        public long ChatId { get; private set; }
        public long AccessHash { get; private set; }
        public string Title { get; private set; }
        public int ParticipantsCount { get; private set; }
    }
}
