// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Stickers.Application.UseCases;
using Vianigram.Stickers.Domain;
using Vianigram.Stickers.Domain.Entities;
using Vianigram.Stickers.Infrastructure;
using Vianigram.Stickers.Ports.Outbound;

namespace Vianigram.Stickers.Application.Handlers
{
    /// <summary>
    /// Issues <c>messages.searchStickerSets#35705b8a</c>. Discovery results
    /// are NOT folded into the local installed library — they are merely
    /// candidate packs the user can choose to install. Hits that happen to
    /// already be installed are returned as-is; the UI layer cross-references
    /// them with the installed list when it needs to disable an "Install"
    /// button.
    /// </summary>
    internal sealed class SearchStickersHandler
    {
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;

        public SearchStickersHandler(IMtProtoRpcPort rpc, ILogger log)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (log == null) throw new ArgumentNullException("log");
            _rpc = rpc;
            _log = new TimestampedLogger(log, "Stickers.SearchStickers");
        }

        public async Task<Result<IList<StickerSet>, StickersError>> HandleAsync(SearchStickersCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<IList<StickerSet>, StickersError>.Fail(StickersError.Unknown("null command"));
            if (string.IsNullOrEmpty(cmd.Query))
                return Result<IList<StickerSet>, StickersError>.Ok(new StickerSet[0]);

            byte[] request = TlEncoder.EncodeSearchStickerSets(cmd.Query, cmd.ExcludeFeatured, cmd.Hash);
            var rpcResult = await _rpc.CallAsync(request, ct).ConfigureAwait(false);
            if (rpcResult.IsFail)
            {
                var mapped = RpcErrorMapper.Map(rpcResult.Error);
                _log.Warn("messages.searchStickerSets failed: " + mapped);
                return Result<IList<StickerSet>, StickersError>.Fail(mapped);
            }

            try
            {
                var decoded = TlDecoder.DecodeFoundStickerSets(rpcResult.Value);
                if (decoded.NotModified)
                    return Result<IList<StickerSet>, StickersError>.Ok(new StickerSet[0]);
                return Result<IList<StickerSet>, StickersError>.Ok(decoded.Sets);
            }
            catch (Exception ex)
            {
                return Result<IList<StickerSet>, StickersError>.Fail(StickersError.Unknown("searchStickerSets decode failed", ex));
            }
        }
    }
}
