// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Vianigram.Kernel.Result;
using Vianigram.Media.Domain;

namespace Vianigram.Media.Ports.Outbound
{
    /// <summary>
    /// Outbound port to the MTProto data plane. Defined locally inside the
    /// Media bounded context (anti-corruption boundary; we do not reference
    /// other bounded contexts' MTProto port types directly). The composition
    /// root provides an adapter that bridges to <c>Vianigram.Core.MTProto</c>.
    ///
    /// All payloads are TL-serialized opaque buffers — encoding/decoding
    /// lives in <c>Infrastructure/TlEncoder.cs</c> /
    /// <c>Infrastructure/TlDecoder.cs</c>.
    ///
    /// <para>Adapters MUST translate raw RPC errors into typed
    /// <see cref="MediaError"/> values via <c>RpcErrorMapper</c> — handlers
    /// never see raw error strings (rule M2). FLOOD_WAIT in particular must
    /// preserve the seconds payload exactly so the chunk-retry timer can
    /// honor it.</para>
    /// </summary>
    public interface IMtProtoRpcPort
    {
        Task<Result<byte[], MediaError>> CallAsync(byte[] tlRequest, CancellationToken ct);

        /// <summary>
        /// Zero-copy media-chunk overload. The request payload is
        /// passed in as an <see cref="IBuffer"/> (callers typically wrap a
        /// <c>byte[]</c> via <c>CryptographicBuffer.CreateFromByteArray</c>),
        /// and the success result is delivered as an <see cref="IBuffer"/> so
        /// it can be handed straight to <c>FileIO.WriteBufferAsync</c> /
        /// <c>HttpBufferContent</c> without re-marshalling.
        ///
        /// <para>For 1 MiB media chunks at 4 parallel slots this saves ~8 MiB
        /// of byte[] churn per round-trip vs. the byte[] overload above. Small
        /// RPC calls (auth/messages/sync) keep <see cref="CallAsync"/> — the
        /// marshal cost there is amortized by the TL parse anyway.</para>
        ///
        /// <para>Error semantics are identical: FLOOD_WAIT seconds preserved
        /// exactly, every error mapped to a typed <see cref="MediaError"/> by
        /// the adapter. Handlers never see raw error strings (rule M2).</para>
        /// </summary>
        Task<Result<IBuffer, MediaError>> CallBufferAsync(IBuffer requestBuffer, CancellationToken ct);
    }
}
