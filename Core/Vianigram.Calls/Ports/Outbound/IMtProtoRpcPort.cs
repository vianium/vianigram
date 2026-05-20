// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;

namespace Vianigram.Calls.Ports.Outbound
{
    /// <summary>
    /// Outbound port for issuing one MTProto RPC. Defined per-context to
    /// keep bounded contexts decoupled (Calls does not reference
    /// SecretChats.Ports nor Account.Ports). The same concrete adapter
    /// (<c>MtProtoRpcAdapter</c>) in the host composition layer implements
    /// every per-context interface — the ACL pattern documented in
    /// <c>docs/managed-architecture/principles.md</c>.
    ///
    /// Contract:
    ///   - <paramref name="requestBytes"/> is a fully-serialized TL request body.
    ///   - On success the result carries the raw TL response payload
    ///     (constructor + body), suitable for <see cref="Infrastructure.TlDecoder"/>.
    ///   - On failure the result carries a structured <see cref="MtProtoRpcError"/>.
    ///     Network / cancellation errors must NOT throw across the port —
    ///     they are surfaced as <see cref="MtProtoRpcError"/> with
    ///     <c>Kind="Network"</c>.
    /// </summary>
    public interface IMtProtoRpcPort
    {
        Task<Result<byte[], MtProtoRpcError>> CallAsync(byte[] requestBytes, CancellationToken ct);
    }
}
