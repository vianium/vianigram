// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Application
{
    /// <summary>
    /// Translates an outbound <see cref="MtProtoRpcError"/> into a typed
    /// <see cref="StickersError"/>. The handler layer owns this mapping so the
    /// outbound port stays generic.
    ///
    /// Mirrors the Contacts / Chats / Account translation patterns: the native
    /// MTProto channel hints (Kind) take precedence over message-string
    /// parsing, with a fallback for FLOOD_WAIT / well-known stickers.* error
    /// strings.
    /// </summary>
    internal static class RpcErrorMapper
    {
        public static StickersError Map(MtProtoRpcError err)
        {
            if (err == null) return StickersError.Unknown("rpc error is null");

            string kind = err.Kind ?? string.Empty;
            string message = err.Message ?? string.Empty;

            if (string.Equals(kind, "FloodWait", StringComparison.OrdinalIgnoreCase))
            {
                return StickersError.FloodWait(err.Parameter, message);
            }

            if (string.Equals(kind, "Network", StringComparison.OrdinalIgnoreCase))
            {
                return StickersError.NetworkError(message);
            }

            if (message.StartsWith("FLOOD_WAIT_", StringComparison.Ordinal))
            {
                int seconds;
                if (int.TryParse(message.Substring("FLOOD_WAIT_".Length), out seconds))
                {
                    return StickersError.FloodWait(seconds, message);
                }
            }

            if (string.Equals(message, "STICKERSET_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "STICKER_INVALID", StringComparison.Ordinal) ||
                string.Equals(message, "STICKERSET_NOT_FOUND", StringComparison.Ordinal) ||
                string.Equals(message, "STICKER_DOCUMENT_INVALID", StringComparison.Ordinal))
            {
                return StickersError.NotFound(message);
            }

            if (string.Equals(message, "STICKERSET_ALREADY_INSTALLED", StringComparison.Ordinal))
            {
                return StickersError.AlreadyInstalled(message);
            }

            if (string.Equals(message, "STICKERSETS_TOO_MUCH", StringComparison.Ordinal) ||
                string.Equals(message, "STICKERS_TOO_MUCH", StringComparison.Ordinal))
            {
                return StickersError.MaxSetsReached(message);
            }

            return StickersError.Unknown("rpc " + kind + " code=" + err.Code + " msg=" + message);
        }
    }
}
