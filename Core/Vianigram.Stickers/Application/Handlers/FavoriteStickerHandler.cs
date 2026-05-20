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
using Vianigram.Stickers.Domain.ValueObjects;
using Vianigram.Stickers.Infrastructure;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Application.Handlers
{
    /// <summary>
    /// Issues <c>messages.faveSticker#b9ffc55b</c> and toggles the favorite
    /// state on the local <see cref="StickerLibrary"/>. The aggregate caps the
    /// favorites list at <see cref="StickerLibrary.MaxFavorites"/>; when the
    /// cap is hit the handler surfaces a <see cref="StickersErrorKind.MaxSetsReached"/>
    /// to mirror Telegram's STICKERS_TOO_MUCH semantics.
    /// </summary>
    internal sealed class FavoriteStickerHandler
    {
        private readonly IStickerRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public FavoriteStickerHandler(IStickerRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Stickers.FavoriteSticker");
            _clock = clock;
        }

        public async Task<Result<Unit, StickersError>> HandleAsync(FavoriteStickerCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, StickersError>.Fail(StickersError.Unknown("null command"));

            StickerLibrary library = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeFaveSticker(cmd.Target, cmd.Unfave);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.faveSticker failed: " + mapped);
                return Result<Unit, StickersError>.Fail(mapped);
            }

            DateTime now = _clock.UtcNow;
            bool desired = !cmd.Unfave;
            bool transitioned = library.SetFavorite(cmd.Target, desired, now);
            if (!transitioned && desired && library.FavoriteCount >= StickerLibrary.MaxFavorites)
            {
                // The aggregate refused the insert because the cap is full.
                return Result<Unit, StickersError>.Fail(StickersError.MaxSetsReached("favorites cap reached"));
            }

            await _repo.SaveAsync(library, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(library, _bus);
            return Result<Unit, StickersError>.Ok(Unit.Value);
        }
    }
}
