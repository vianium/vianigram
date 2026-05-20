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
    /// Issues <c>messages.installStickerSet#c78fe460</c> against the configured
    /// RPC port and mirrors the install in the local
    /// <see cref="StickerLibrary"/> aggregate.
    ///
    /// V1 short-circuit: if the set is already in the library and the server
    /// returns STICKERSET_ALREADY_INSTALLED, the handler still considers the
    /// operation successful — the user-visible state matches their intent.
    /// </summary>
    internal sealed class InstallStickerSetHandler
    {
        private readonly IStickerRepository _repo;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly IClock _clock;

        public InstallStickerSetHandler(IStickerRepository repo, IMtProtoRpcPort rpc, IEventBus bus, ILogger log, IClock clock)
        {
            if (repo == null) throw new ArgumentNullException("repo");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            if (clock == null) throw new ArgumentNullException("clock");
            _repo = repo;
            _rpc = rpc;
            _bus = bus;
            _log = new TimestampedLogger(log, "Stickers.InstallStickerSet");
            _clock = clock;
        }

        public async Task<Result<Unit, StickersError>> HandleAsync(InstallStickerSetCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<Unit, StickersError>.Fail(StickersError.Unknown("null command"));

            StickerLibrary library = await _repo.LoadAsync(ct).ConfigureAwait(false);

            byte[] request = TlEncoder.EncodeInstallStickerSet(cmd.Target, cmd.Archived);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                if (mapped.Kind == StickersErrorKind.AlreadyInstalled)
                {
                    // Server confirms the desired state already exists. Treat as success.
                    _log.Info("messages.installStickerSet: already installed " + cmd.Target);
                    return Result<Unit, StickersError>.Ok(Unit.Value);
                }
                _log.Warn("messages.installStickerSet failed: " + mapped);
                return Result<Unit, StickersError>.Fail(mapped);
            }

            // V1: synthesize a placeholder set. The next SyncStickerSetsAsync
            // (or GetStickerSetAsync) will populate the metadata. We still
            // stage the install event eagerly so UI updates immediately.
            var placeholder = new StickerSet(
                cmd.Target,
                title: string.Empty, shortName: string.Empty,
                count: 0, hash: 0L,
                isOfficial: false, isAnimated: false, isMasks: false, isVideos: false);
            library.AddOrUpdate(placeholder, _clock.UtcNow);
            await _repo.SaveAsync(library, ct).ConfigureAwait(false);
            HandlerEventBridge.Drain(library, _bus);
            return Result<Unit, StickersError>.Ok(Unit.Value);
        }
    }
}
