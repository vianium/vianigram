// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;
using Vianigram.Stickers.Application.UseCases;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Domain.ValueObjects;
using Vianigram.Stickers.Infrastructure;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Application.Handlers
{
    /// <summary>
    /// Issues <c>messages.getRecentStickers#9da9403b</c>, decodes the
    /// response, and replaces the local <see cref="StickerLibrary"/> recent
    /// list with the server-supplied order. The aggregate caps the result at
    /// <see cref="StickerLibrary.MaxRecent"/> automatically.
    /// </summary>
    internal sealed class GetRecentStickersHandler
    {
        private readonly IStickerRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public GetRecentStickersHandler(IStickerRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Stickers.GetRecentStickers");
            _clock = clock;
        }

        public async Task<Result<IList<Sticker>, StickersError>> HandleAsync(GetRecentStickersCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<Sticker>, StickersError>.Fail(StickersError.Unknown("null command"));

            StickerLibrary library = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeGetRecentStickers(cmd.Attached, cmd.Hash);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.getRecentStickers failed: " + mapped);
                return Result<IList<Sticker>, StickersError>.Fail(mapped);
            }

            TlDecoder.DecodedRecentStickers decoded;
            try
            {
                decoded = TlDecoder.DecodeRecentStickers(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<IList<Sticker>, StickersError>.Fail(StickersError.Unknown("getRecentStickers decode failed", ex));
            }

            if (decoded.NotModified)
            {
                // Return the cached recents — best effort: the aggregate
                // stores StickerId only, so we can't synthesize Sticker
                // entities here. Caller treats an empty list as cache-hit
                // and re-uses local UI state.
                return Result<IList<Sticker>, StickersError>.Ok(new Sticker[0]);
            }

            DateTime now = _clock.UtcNow;
            var ids = new List<StickerId>(decoded.Stickers.Count);
            for (int i = 0; i < decoded.Stickers.Count; i++)
            {
                Sticker s = decoded.Stickers[i];
                if (s != null) ids.Add(s.Id);
            }
            library.ReplaceRecent(ids);
            await _repo.SaveAsync(library, ct).ConfigureAwait(false);
            // No domain event for bulk recent refresh — the caller treats this
            // as a cache hydration, not a per-sticker action.
            HandlerEventBridge.Drain(library, _bus);

            // 'now' captured for telemetry parity with other handlers; not used
            // to stage events here because the recent-list refresh is bulk.
            DateTime unusedNow = now;
            if (unusedNow == DateTime.MinValue) { /* placeholder */ }
            return Result<IList<Sticker>, StickersError>.Ok(decoded.Stickers);
        }
    }
}
