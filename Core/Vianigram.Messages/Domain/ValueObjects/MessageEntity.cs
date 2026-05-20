// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Messages.Domain.ValueObjects
{
    /// <summary>
    /// Markup ranges over a message body — bold/italic/url/mention/etc.
    /// Stored as offsets/lengths into the UTF-16 codepoint stream of the
    /// rendered Body.
    /// </summary>
    public enum EntityKind
    {
        Bold = 0,
        Italic = 1,
        Underline = 2,
        Strike = 3,
        Code = 4,
        Pre = 5,
        Url = 6,
        TextUrl = 7,
        Mention = 8,
        Hashtag = 9,
        BotCommand = 10,
        Email = 11,
        PhoneNumber = 12,
        Spoiler = 13,
        CustomEmoji = 14,
        BlockQuote = 15
    }

    public sealed class MessageEntity
    {
        public MessageEntity(EntityKind kind, int offset, int length, string url = null)
            : this(kind, offset, length, url, null, null)
        {
        }

        public MessageEntity(EntityKind kind, int offset, int length, string url,
            string language, string customEmojiId)
        {
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (length < 0) throw new ArgumentOutOfRangeException("length");
            Kind = kind;
            Offset = offset;
            Length = length;
            Url = url;
            Language = language ?? string.Empty;
            CustomEmojiId = customEmojiId ?? string.Empty;
        }

        public EntityKind Kind { get; private set; }
        public int Offset { get; private set; }
        public int Length { get; private set; }
        public string Url { get; private set; }
        public string Language { get; private set; }
        public string CustomEmojiId { get; private set; }
    }
}
