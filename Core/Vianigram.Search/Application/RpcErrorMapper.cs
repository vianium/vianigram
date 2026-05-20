// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Search.Domain;
using Vianigram.Search.Ports.Outbound;

namespace Vianigram.Search.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="SearchError"/>. The handler layer owns this mapping so the
    /// outbound port stays generic.
    ///
    /// Mirrors the Settings / Notifications / Stickers translation patterns:
    /// native MTProto channel hints (<see cref="MtProtoRpcError.Kind"/>) take
    /// precedence over message-string parsing, with a fallback for
    /// FLOOD_WAIT and well-known search error strings emitted by
    /// <c>messages.search</c> / <c>messages.searchGlobal</c> /
    /// <c>contacts.search</c>.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static SearchError Map(MtProtoRpcError err)
        {
            if (err == null) return SearchError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return SearchError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return SearchError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return SearchError.FloodWait(seconds, message);
                }
            }

            // Per-RPC failure strings observed in tdlib's MessagesManager and
            // ContactsManager. Map each to the closest typed kind so callers
            // can react without parsing strings.
            if (string.Equals(message, "PEER_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "CHAT_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "CHANNEL_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "CHANNEL_PRIVATE", StringComparison.Ordinal) ||
                string.Equals(message, "USER_ID_INVALID", StringComparison.Ordinal))
            {
                return SearchError.PeerNotFound(message);
            }

            if (string.Equals(message, "QUERY_TOO_SHORT", StringComparison.Ordinal) ||
                string.Equals(message, "SEARCH_QUERY_EMPTY", StringComparison.Ordinal))
            {
                return SearchError.QueryTooShort(message);
            }

            if (string.Equals(message, "INPUT_FILTER_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "MSG_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "OFFSET_PEER_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "FROM_PEER_INVALID", StringComparison.Ordinal))
            {
                return SearchError.InvalidValue(message);
            }

            return SearchError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
