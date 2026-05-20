// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Ports.Outbound;

namespace Vianigram.Contacts.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="ContactsError"/>. The handler layer owns this mapping so the
    /// outbound port stays generic.
    ///
    /// Mirrors the AuthErrorMapper / Chats translation patterns: the native
    /// MTProto channel hints (Kind) take precedence over message-string
    /// parsing, with a fallback for FLOOD_WAIT / well-known contacts.* error
    /// strings.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static ContactsError Map(MtProtoRpcError err)
        {
            if (err == null) return ContactsError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return ContactsError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return ContactsError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return ContactsError.FloodWait(seconds, message);
                }
            }

            if (string.Equals(message, "CONTACT_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "USER_ID_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "PEER_ID_INVALID", StringComparison.Ordinal))
            {
                return ContactsError.NotFound(message);
            }

            if (string.Equals(message, "USER_ALREADY_PARTICIPANT", StringComparison.Ordinal) ||
                string.Equals(message, "CONTACT_NAME_EMPTY", StringComparison.Ordinal))
            {
                return ContactsError.AlreadyImported(message);
            }

            if (string.Equals(message, "CONTACTS_TOO_MUCH", StringComparison.Ordinal) ||
                string.Equals(message, "PEER_FLOOD", StringComparison.Ordinal))
            {
                return ContactsError.PermissionDenied(message);
            }

            return ContactsError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
