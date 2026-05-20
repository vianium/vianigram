// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;

namespace Vianigram.Sync.Ports.Outbound
{
    /// <summary>
    /// MTProto RPC port for the Sync context. Handlers compose a TL-encoded
    /// request body via <see cref="Vianigram.Sync.Infrastructure.TlEncoder"/>
    /// and invoke the channel; the response body is opaque bytes that the
    /// handler routes back through <see cref="Vianigram.Sync.Infrastructure.TlDecoder"/>.
    ///
    /// Errors surface as <see cref="MtProtoRpcError"/>; the application layer
    /// maps those to <see cref="Vianigram.Sync.Domain.Errors.SyncError"/>. Sync
    /// handlers never see raw exceptions across this boundary.
    ///
    /// The same physical adapter (one per host process) implements every bounded
    /// context's IMtProtoRpcPort. It does not import any Sync types beyond this
    /// interface and <see cref="MtProtoRpcError"/>.
    /// </summary>
    public interface IMtProtoRpcPort
    {
        Task<Result<byte[], MtProtoRpcError>> InvokeAsync(byte[] requestBody, string methodName, CancellationToken ct);
    }
}
