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
using Vianigram.Stickers.Infrastructure;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Application.Handlers
{
    /// <summary>
    /// Issues <c>messages.getAllStickers#b8a0a1a8</c>, decodes the response,
    /// and reconciles the resulting set list with the local
    /// <see cref="StickerLibrary"/> aggregate. Persists, then drains and
    /// publishes the staged domain events.
    ///
    /// Errors:
    ///   - Network / cancellation -&gt; <see cref="StickersError.NetworkError"/>.
    ///   - TL decode errors        -&gt; <see cref="StickersError.Unknown"/> with cause.
    ///   - Server <c>allStickersNotModified</c> -&gt; success with the current snapshot.
    /// </summary>
    internal sealed class SyncStickersHandler
    {
        private readonly IStickerRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public SyncStickersHandler(IStickerRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Stickers.SyncStickers");
            _clock = clock;
        }

        public async Task<Result<IList<StickerSet>, StickersError>> HandleAsync(SyncStickersCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<StickerSet>, StickersError>.Fail(StickersError.Unknown("null command"));

            StickerLibrary library = await _repo.LoadAsync(ct).ConfigureAwait(false);

            long hash = cmd.Hash != 0L ? cmd.Hash : library.LastHash;
            byte[] request = TlEncoder.EncodeGetAllStickers(hash);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.getAllStickers failed: " + mapped);
                return Result<IList<StickerSet>, StickersError>.Fail(mapped);
            }

            TlDecoder.DecodedAllStickers decoded;
            try
            {
                decoded = TlDecoder.DecodeAllStickers(rpcResult.Value);
            }
            catch (Exception ex)
            {
                return Result<IList<StickerSet>, StickersError>.Fail(StickersError.Unknown("getAllStickers decode failed", ex));
            }

            DateTime now = _clock.UtcNow;
            if (decoded.NotModified)
            {
                return Result<IList<StickerSet>, StickersError>.Ok(library.InstalledSnapshot());
            }

            library.ApplyServerSync(decoded.Sets, decoded.Hash, now);
            await _repo.SaveAsync(library, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(library, _bus);

            return Result<IList<StickerSet>, StickersError>.Ok(library.InstalledSnapshot());
        }
    }
}
