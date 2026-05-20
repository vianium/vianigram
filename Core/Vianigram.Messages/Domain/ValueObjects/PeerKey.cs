// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Messages.Domain.ValueObjects
{
    /// <summary>
    /// Peer kinds the message stream understands. Mirrors Telegram's user/chat/channel
    /// trichotomy without coupling the Messages context to the Chats domain types.
    /// </summary>
    public enum PeerKind
    {
        User = 0,
        Chat = 1,
        Channel = 2
    }

    /// <summary>
    /// String-encoded peer reference: "user:&lt;id&gt;", "chat:&lt;id&gt;", "channel:&lt;id&gt;".
    /// We keep the wire form as a plain string to deliberately avoid sharing a typed
    /// PeerId across bounded contexts. The helpers here parse and emit that form.
    /// </summary>
    public static class PeerKey
    {
        public const string PrefixUser = "user:";
        public const string PrefixChat = "chat:";
        public const string PrefixChannel = "channel:";

        public static string ForUser(long userId)
        {
            return PrefixUser + userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string ForChat(long chatId)
        {
            return PrefixChat + chatId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static string ForChannel(long channelId)
        {
            return PrefixChannel + channelId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        public static bool TryParse(string peerKey, out PeerKind kind, out long id)
        {
            kind = PeerKind.User;
            id = 0;
            if (string.IsNullOrEmpty(peerKey)) return false;

            string body;
            if (peerKey.StartsWith(PrefixUser, StringComparison.Ordinal))
            {
                kind = PeerKind.User;
                body = peerKey.Substring(PrefixUser.Length);
            }
            else if (peerKey.StartsWith(PrefixChat, StringComparison.Ordinal))
            {
                kind = PeerKind.Chat;
                body = peerKey.Substring(PrefixChat.Length);
            }
            else if (peerKey.StartsWith(PrefixChannel, StringComparison.Ordinal))
            {
                kind = PeerKind.Channel;
                body = peerKey.Substring(PrefixChannel.Length);
            }
            else
            {
                return false;
            }

            return long.TryParse(body, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out id);
        }

        public static bool IsChannel(string peerKey)
        {
            return !string.IsNullOrEmpty(peerKey)
                && peerKey.StartsWith(PrefixChannel, StringComparison.Ordinal);
        }

        public static bool IsValid(string peerKey)
        {
            PeerKind kind;
            long id;
            return TryParse(peerKey, out kind, out id);
        }
    }
}
