// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Sync.Ports.Outbound
{
    /// <summary>
    /// Structured error returned by <see cref="IMtProtoRpcPort"/>. Mirrors the
    /// rpc_error model from the native MTProto channel; one instance per failure.
    ///
    /// Defined per-context per the kernel rule that bounded contexts do not share
    /// outbound port types. The same concrete adapter may surface multiple
    /// per-context error types — they're structurally identical but namespaced.
    /// </summary>
    public sealed class MtProtoRpcError
    {
        /// <summary>Error kind classifier — e.g. "FloodWait", "AuthRestart", "Network", "Unknown".</summary>
        public string Kind { get; set; }

        /// <summary>Numeric error code (303 for migrate, 420 for flood, 401 for unauthorized, etc.).</summary>
        public int Code { get; set; }

        /// <summary>Server-supplied raw error string.</summary>
        public string Message { get; set; }

        /// <summary>Numeric parameter — flood-wait seconds for FloodWait, target DC for *_MIGRATE_X.</summary>
        public int Parameter { get; set; }

        public override string ToString()
        {
            return (Kind ?? "?") + "[" + Code + "] " + (Message ?? "") + " param=" + Parameter;
        }
    }
}
