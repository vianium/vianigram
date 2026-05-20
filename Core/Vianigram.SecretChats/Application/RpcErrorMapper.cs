// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Ports.Outbound;

namespace Vianigram.SecretChats.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="SecretChatError"/>. The handler layer owns this mapping so
    /// the outbound port stays generic.
    ///
    /// Mirrors the AuthErrorMapper / Contacts translation patterns: the
    /// native MTProto channel hints (Kind) take precedence over message-
    /// string parsing, with a fallback for FLOOD_WAIT / well-known
    /// secret-chat error strings.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static SecretChatError Map(MtProtoRpcError err)
        {
            if (err == null) return SecretChatError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return SecretChatError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return SecretChatError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return SecretChatError.FloodWait(seconds, message);
                }
            }

            if (string.Equals(message, "ENCRYPTION_DECLINED", StringComparison.Ordinal) ||
                string.Equals(message, "ENCRYPTED_CHAT_NOT_FOUND", StringComparison.Ordinal) ||
                string.Equals(message, "CHAT_ID_INVALID", StringComparison.Ordinal))
            {
                return SecretChatError.ChatNotFound(message);
            }

            if (string.Equals(message, "DH_G_A_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "DH_G_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "DH_PRIME_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "ENCRYPTION_ALREADY_ACCEPTED", StringComparison.Ordinal))
            {
                return SecretChatError.InvalidKey(message);
            }

            if (string.Equals(message, "MSG_WAIT_FAILED", StringComparison.Ordinal) ||
                string.Equals(message, "RANDOM_ID_DUPLICATE", StringComparison.Ordinal))
            {
                return SecretChatError.ProtocolError(message);
            }

            return SecretChatError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
