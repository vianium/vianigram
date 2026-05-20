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
    /// Issues <c>messages.uninstallStickerSet#f96e55de</c> and removes the set
    /// from the local <see cref="StickerLibrary"/>. After a successful
    /// acknowledgement the handler also evicts every cached blob owned by the
    /// uninstalled set via <see cref="IStickerCachePort.EvictPackAsync"/> —
    /// this matches the production-storage adapter's fast-path
    /// (folder-level rmdir).
    /// </summary>
    internal sealed class UninstallStickerSetHandler
    {
        private readonly IStickerRepository _repo;
        private readonly IStickerCachePort _cache;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public UninstallStickerSetHandler(
            IStickerRepository repo,
            IStickerCachePort cache,
            IMtProtoRpcPort rpc,
            IEventBus bus,
            ILogger log,
            IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (cache == null) throw new ArgumentNullException("cache");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _cache = cache;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Stickers.UninstallStickerSet");
            _clock = clock;
        }

        public async Task<Result<Unit, StickersError>> HandleAsync(UninstallStickerSetCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, StickersError>.Fail(StickersError.Unknown("null command"));

            StickerLibrary library = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeUninstallStickerSet(cmd.Target);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.uninstallStickerSet failed: " + mapped);
                return Result<Unit, StickersError>.Fail(mapped);
            }

            library.Uninstall(cmd.Target, _clock.UtcNow);
            await _repo.SaveAsync(library, ct).ConfigureAwait(false);
            // Best-effort blob eviction; ignore the result so a cache miss
            // never blocks the user-visible state transition.
            await _cache.EvictPackAsync(cmd.Target, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(library, _bus);
            return Result<Unit, StickersError>.Ok(Unit.Value);
        }
    }
}
