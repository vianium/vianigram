// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Ports.Outbound;

namespace Vianigram.Privacy.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="PrivacyError"/>. The handler layer owns this mapping so the
    /// outbound port stays generic.
    ///
    /// Mirrors the Settings / Notifications / Stickers / Search translation
    /// patterns: native MTProto channel hints (<see cref="MtProtoRpcError.Kind"/>)
    /// take precedence over message-string parsing, with a fallback for
    /// FLOOD_WAIT and well-known privacy / authorization error strings.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static PrivacyError Map(MtProtoRpcError err)
        {
            if (err == null) return PrivacyError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return PrivacyError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return PrivacyError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return PrivacyError.FloodWait(seconds, message);
                }
            }

            // Well-known PrivacyManager / authorizations error strings observed
            // in tdlib's account.cpp / authorizations handlers.
            if (string.Equals(message, "FRESH_RESET_AUTHORISATION_FORBIDDEN", StringComparison.Ordinal))
            {
                return PrivacyError.ResetForbidden(message);
            }

            if (string.Equals(message, "HASH_INVALID", StringComparison.Ordinal))
            {
                return PrivacyError.NotFound(message);
            }

            if (string.Equals(message, "PRIVACY_KEY_INVALID", StringComparison.Ordinal))
            {
                return PrivacyError.InvalidValue(message);
            }

            if (string.Equals(message, "PRIVACY_TOO_LONG", StringComparison.Ordinal))
            {
                return PrivacyError.InvalidValue(message);
            }

            if (string.Equals(message, "USER_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "USER_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "CHAT_ID_INVALID", StringComparison.Ordinal))
            {
                return PrivacyError.NotFound(message);
            }

            if (string.Equals(message, "AUTH_KEY_PERM_EMPTY", StringComparison.Ordinal) ||
                string.Equals(message, "AUTH_KEY_INVALID", StringComparison.Ordinal) ||
                err.Code == 401)
            {
                return PrivacyError.NotAuthenticated(message);
            }

            return PrivacyError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
