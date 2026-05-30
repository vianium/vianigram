// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProtoChannelAdapter.AdditionalContexts.cs
// Adds the per-context byte-level CallAsync surfaces for the nine bounded
// contexts (Contacts / Notifications / Settings / Privacy / Search / Media /
// Stickers / SecretChats / Calls).
// All nine share the same Result<byte[], <Context>.MtProtoRpcError> shape
// (Media uses MediaError instead) and delegate to the same CallInternalAsync
// core defined on the partial-class root file.

using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Vianigram.Kernel.Result;

namespace Vianigram.Composition.Infrastructure
{
    public sealed partial class MtProtoChannelAdapter
    {
        // ---------- Contacts port ----------

        async Task<Result<byte[], Vianigram.Contacts.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Contacts.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Contacts.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Contacts.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Contacts.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Notifications port ----------

        async Task<Result<byte[], Vianigram.Notifications.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Notifications.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Notifications.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Notifications.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Notifications.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Settings port ----------

        async Task<Result<byte[], Vianigram.Settings.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Settings.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Settings.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Settings.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Settings.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Privacy port ----------

        async Task<Result<byte[], Vianigram.Privacy.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Privacy.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Privacy.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Privacy.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Privacy.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Search port ----------

        async Task<Result<byte[], Vianigram.Search.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Search.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Search.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Search.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Search.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Media port (typed MediaError) ----------

        async Task<Result<byte[], Vianigram.Media.Domain.MediaError>>
            Vianigram.Media.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] tlRequest, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(tlRequest, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Media.Domain.MediaError>.Ok(outcome.Bytes);
            }
            return Result<byte[], Vianigram.Media.Domain.MediaError>.Fail(MapToMediaError(outcome));
        }

        async Task<Result<byte[], Vianigram.Media.Domain.MediaError>>
            Vianigram.Media.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] tlRequest, int dcId, CancellationToken ct)
        {
            CallOutcome outcome = await CallMediaInternalAsync(tlRequest, dcId, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Media.Domain.MediaError>.Ok(outcome.Bytes);
            }
            return Result<byte[], Vianigram.Media.Domain.MediaError>.Fail(MapToMediaError(outcome));
        }

        // Zero-copy media-chunk overload. Routes through the native
        // CallBufferAsync(IBuffer) so 1 MiB chunks no longer pay an
        // 8 MiB-per-round-trip Array<uint8>↔byte[] marshal. Error mapping
        // is identical to the byte[] face (FLOOD_WAIT seconds preserved
        // exactly, every other error landed via MapToMediaError).
        async Task<Result<IBuffer, Vianigram.Media.Domain.MediaError>>
            Vianigram.Media.Ports.Outbound.IMtProtoRpcPort.CallBufferAsync(
                IBuffer requestBuffer, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalBufferAsync(requestBuffer, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<IBuffer, Vianigram.Media.Domain.MediaError>.Ok(outcome.ResultBufferOpt);
            }
            return Result<IBuffer, Vianigram.Media.Domain.MediaError>.Fail(MapToMediaError(outcome));
        }

        async Task<Result<IBuffer, Vianigram.Media.Domain.MediaError>>
            Vianigram.Media.Ports.Outbound.IMtProtoRpcPort.CallBufferAsync(
                IBuffer requestBuffer, int dcId, CancellationToken ct)
        {
            CallOutcome outcome = await CallMediaInternalBufferAsync(requestBuffer, dcId, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<IBuffer, Vianigram.Media.Domain.MediaError>.Ok(outcome.ResultBufferOpt);
            }
            return Result<IBuffer, Vianigram.Media.Domain.MediaError>.Fail(MapToMediaError(outcome));
        }

        private static Vianigram.Media.Domain.MediaError MapToMediaError(CallOutcome outcome)
        {
            // Best-effort mapping from MTProto rpc_error kind to MediaError.
            // FloodWait carries the seconds payload exactly as parsed (rule M2).
            string kind = outcome.Kind ?? "Unknown";
            if (string.Equals(kind, "FloodWait", System.StringComparison.OrdinalIgnoreCase) && outcome.Parameter > 0)
            {
                return Vianigram.Media.Domain.MediaError.FloodWait(outcome.Parameter);
            }
            if (string.Equals(kind, "Network", System.StringComparison.OrdinalIgnoreCase))
            {
                return Vianigram.Media.Domain.MediaError.NetworkError(
                    outcome.Message ?? ("RPC error " + outcome.Code));
            }
            return Vianigram.Media.Domain.MediaError.ProtocolError(
                outcome.Message ?? ("RPC error " + outcome.Code));
        }

        // ---------- Stickers port ----------

        async Task<Result<byte[], Vianigram.Stickers.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Stickers.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Stickers.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Stickers.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Stickers.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- SecretChats port ----------

        async Task<Result<byte[], Vianigram.SecretChats.Ports.Outbound.MtProtoRpcError>>
            Vianigram.SecretChats.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.SecretChats.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.SecretChats.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.SecretChats.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }

        // ---------- Calls port ----------

        async Task<Result<byte[], Vianigram.Calls.Ports.Outbound.MtProtoRpcError>>
            Vianigram.Calls.Ports.Outbound.IMtProtoRpcPort.CallAsync(
                byte[] requestBytes, CancellationToken ct)
        {
            CallOutcome outcome = await CallInternalAsync(requestBytes, ct).ConfigureAwait(false);
            if (outcome.Ok)
            {
                return Result<byte[], Vianigram.Calls.Ports.Outbound.MtProtoRpcError>.Ok(outcome.Bytes);
            }
            var err = new Vianigram.Calls.Ports.Outbound.MtProtoRpcError
            {
                Kind = outcome.Kind,
                Code = outcome.Code,
                Message = outcome.Message,
                Parameter = outcome.Parameter
            };
            return Result<byte[], Vianigram.Calls.Ports.Outbound.MtProtoRpcError>.Fail(err);
        }
    }
}
