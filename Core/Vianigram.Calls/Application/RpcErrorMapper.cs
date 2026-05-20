// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Ports.Outbound;

namespace Vianigram.Calls.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="CallError"/>. The handler layer owns this mapping so the
    /// outbound port stays generic.
    ///
    /// Mirrors the pattern used by Account, Contacts, Chats, and
    /// SecretChats: the native MTProto channel hints (Kind) take precedence
    /// over message-string parsing, with a fallback for FLOOD_WAIT and the
    /// well-known phone.* error strings.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static CallError Map(MtProtoRpcError err)
        {
            if (err == null) return CallError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return CallError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return CallError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return CallError.FloodWait(seconds, message);
                }
            }

            // Well-known phone.* error strings — see TDLib PhoneCallManager.cpp.
            if (string.Equals(message, "PARTICIPANT_VERSION_OUTDATED", StringComparison.Ordinal) ||
                string.Equals(message, "CALL_PROTOCOL_FLAGS_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "CALL_PROTOCOL_LAYER_INVALID", StringComparison.Ordinal))
            {
                return CallError.ProtocolMismatch(message);
            }

            if (string.Equals(message, "USER_PRIVACY_RESTRICTED", StringComparison.Ordinal) ||
                string.Equals(message, "USER_DELETED", StringComparison.Ordinal) ||
                string.Equals(message, "USER_BLOCKED", StringComparison.Ordinal) ||
                string.Equals(message, "USER_IS_BOT", StringComparison.Ordinal) ||
                string.Equals(message, "USER_BANNED_IN_CHANNEL", StringComparison.Ordinal))
            {
                return CallError.ParticipantUnavailable(message);
            }

            if (string.Equals(message, "CALL_OCCUPY_FAILED", StringComparison.Ordinal) ||
                string.Equals(message, "CALL_ALREADY_DECLINED", StringComparison.Ordinal))
            {
                return CallError.Busy(message);
            }

            if (string.Equals(message, "CALL_ALREADY_ACCEPTED", StringComparison.Ordinal))
            {
                return CallError.AlreadyInCall(message);
            }

            if (string.Equals(message, "CALL_PEER_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "INPUT_USER_DEACTIVATED", StringComparison.Ordinal) ||
                string.Equals(message, "PEER_ID_INVALID", StringComparison.Ordinal))
            {
                return CallError.CallNotFound(message);
            }

            if (string.Equals(message, "CONNECTION_NOT_INITED", StringComparison.Ordinal) ||
                string.Equals(message, "CONNECTION_LAYER_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "DH_G_A_HASH_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "DH_G_A_INVALID", StringComparison.Ordinal))
            {
                return CallError.ProtocolError(message);
            }

            return CallError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
