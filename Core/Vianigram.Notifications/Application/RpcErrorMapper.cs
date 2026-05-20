// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Ports.Outbound;

namespace Vianigram.Notifications.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="NotificationsError"/>. The handler layer owns this mapping
    /// so the outbound port stays generic.
    ///
    /// Mirrors the Stickers / Contacts / Account translation patterns: native
    /// MTProto channel hints (Kind) take precedence over message-string
    /// parsing, with a fallback for FLOOD_WAIT / well-known account.* error
    /// strings observed for the notify-settings RPCs.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static NotificationsError Map(MtProtoRpcError err)
        {
            if (err == null) return NotificationsError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return NotificationsError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return NotificationsError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return NotificationsError.FloodWait(seconds, message);
                }
            }

            if (string.Equals(message, "PEER_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "MSG_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "USER_ID_INVALID", StringComparison.Ordinal))
            {
                return NotificationsError.NotFound(message);
            }

            if (string.Equals(message, "SETTINGS_INVALID", StringComparison.Ordinal))
            {
                return NotificationsError.NotInExpectedState(message);
            }

            return NotificationsError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
