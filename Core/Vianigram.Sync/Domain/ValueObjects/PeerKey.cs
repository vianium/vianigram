// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Sync.Domain.ValueObjects
{
    /// <summary>
    /// Stringified peer identifier used as a ubiquitous-language coordinate
    /// across context boundaries.
    ///
    /// Format: "user:{id}" | "chat:{id}" | "channel:{id}".
    ///
    /// Sync emits PeerKey rather than a typed PeerId because the Chats context
    /// owns the typed PeerId aggregate and Sync must not depend on it (rule 3:
    /// no using Vianigram.&lt;OtherContext&gt; in domain code). PeerKey is a
    /// neutral inter-context coordinate that downstream contexts can resolve
    /// to their own typed PeerId on subscribe.
    /// </summary>
    public static class PeerKey
    {
        public const string PrefixUser = "user:";
        public const string PrefixChat = "chat:";
        public const string PrefixChannel = "channel:";

        public static string ForUser(long userId)
        {
            return PrefixUser + userId.ToString(CultureInfo.InvariantCulture);
        }

        public static string ForChat(long chatId)
        {
            return PrefixChat + chatId.ToString(CultureInfo.InvariantCulture);
        }

        public static string ForChannel(long channelId)
        {
            return PrefixChannel + channelId.ToString(CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string key, out string kind, out long id)
        {
            kind = null;
            id = 0;
            if (string.IsNullOrEmpty(key)) return false;
            int colon = key.IndexOf(':');
            if (colon <= 0 || colon == key.Length - 1) return false;
            kind = key.Substring(0, colon);
            return long.TryParse(key.Substring(colon + 1), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out id);
        }
    }
}
