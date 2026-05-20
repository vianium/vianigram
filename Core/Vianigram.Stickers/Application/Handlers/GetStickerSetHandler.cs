// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Stickers.Application.UseCases;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Infrastructure;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Application.Handlers
{
    /// <summary>
    /// Issues <c>messages.getStickerSet#c8a0ec74</c>, decodes the response,
    /// and updates the corresponding entry in the local
    /// <see cref="StickerLibrary"/>. If the set is not yet installed the
    /// handler still returns the populated entity to the caller (e.g. to
    /// preview a set before installing).
    /// </summary>
    internal sealed class GetStickerSetHandler
    {
        private readonly IStickerRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public GetStickerSetHandler(IStickerRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Stickers.GetStickerSet");
            _clock = clock;
        }

        public async Task<Result<StickerSet, StickersError>> HandleAsync(GetStickerSetCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<StickerSet, StickersError>.Fail(StickersError.Unknown("null command"));

            StickerLibrary library = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeGetStickerSet(cmd.Target, cmd.Hash);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.getStickerSet failed: " + mapped);
                return Result<StickerSet, StickersError>.Fail(mapped);
            }

            TlDecoder.DecodedStickerSet decoded;
            try
            {
                decoded = TlDecoder.DecodeStickerSet(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<StickerSet, StickersError>.Fail(StickersError.Unknown("getStickerSet decode failed", ex));
            }

            DateTime now = _clock.UtcNow;
            if (decoded.NotModified)
            {
                StickerSet cached = library.Find(cmd.Target);
                if (cached == null)
                    return Result<StickerSet, StickersError>.Fail(StickersError.NotFound("set not in library"));
                return Result<StickerSet, StickersError>.Ok(cached);
            }

            if (decoded.Set == null)
                return Result<StickerSet, StickersError>.Fail(StickersError.Unknown("decoded set is null"));

            // Fold the loaded body onto the parsed metadata.
            var fresh = decoded.Set;
            // Reattach loaded content via the aggregate so domain events get staged consistently.
            library.AddOrUpdate(fresh, now);
            if (decoded.Stickers != null && decoded.Stickers.Count > 0)
            {
                library.UpdateSetContent(fresh.Id, decoded.Stickers, now);
            }
            await _repo.SaveAsync(library, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(library, _bus);

            StickerSet stored = library.Find(fresh.Id);
            if (stored == null) stored = fresh;
            return Result<StickerSet, StickersError>.Ok(stored);
        }
    }
}
