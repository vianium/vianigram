// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Contacts.Ports.Outbound
{
    /// <summary>
    /// Error returned by <see cref="IMtProtoRpcPort"/>. Mirrors the structured
    /// rpc_error model from the native MTProto channel and the per-context
    /// shape used elsewhere (Account, etc).
    /// </summary>
    public sealed class MtProtoRpcError
    {
        /// <summary>Error kind classifier — e.g. "FloodWait", "Network", "Unknown".</summary>
        public string Kind { get; set; }

        /// <summary>Numeric error code (303 for migrate, 420 for flood, 401 for unauthorized, etc.).</summary>
        public int Code { get; set; }

        /// <summary>Server-supplied raw error string (FLOOD_WAIT_30, USER_ALREADY_PARTICIPANT, ...).</summary>
        public string Message { get; set; }

        /// <summary>Numeric parameter — flood-wait seconds for FloodWait, target DC for *_MIGRATE_X.</summary>
        public int Parameter { get; set; }

        public override string ToString()
        {
            return (Kind ?? "?") + "[" + Code + "] " + (Message ?? "") + " param=" + Parameter;
        }
    }
}
