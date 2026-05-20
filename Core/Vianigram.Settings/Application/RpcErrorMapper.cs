// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Ports.Outbound;

namespace Vianigram.Settings.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="SettingsError"/>. The handler layer owns this mapping so the
    /// outbound port stays generic.
    ///
    /// Mirrors the Notifications / Stickers translation patterns: native
    /// MTProto channel hints (<see cref="MtProtoRpcError.Kind"/>) take
    /// precedence over message-string parsing, with a fallback for FLOOD_WAIT
    /// and well-known langpack / content-settings error strings.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static SettingsError Map(MtProtoRpcError err)
        {
            if (err == null) return SettingsError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return SettingsError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return SettingsError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return SettingsError.FloodWait(seconds, message);
                }
            }

            if (string.Equals(message, "LANG_PACK_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "LANG_CODE_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "LANG_CODE_NOT_SUPPORTED", StringComparison.Ordinal))
            {
                return SettingsError.NotFound(message);
            }

            if (string.Equals(message, "SETTINGS_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "INPUT_USER_DEACTIVATED", StringComparison.Ordinal))
            {
                return SettingsError.InvalidValue(message);
            }

            return SettingsError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
