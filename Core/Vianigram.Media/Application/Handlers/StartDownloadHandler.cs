// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;
using Vianigram.Media.Application.UseCases;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.Entities;
using Vianigram.Media.Domain.Events;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Media.Infrastructure;
using Vianigram.Media.Ports.Outbound;

namespace Vianigram.Media.Application.Handlers
{
    /// <summary>
    /// Parallel chunked-download orchestrator.
    ///
    /// <para>Algorithm:</para>
    /// <list type="number">
    ///   <item><description>Build the chunk plan from the requested
    ///         <see cref="StartDownloadCommand.TotalSize"/> at the
    ///         strategy's current <see cref="ChunkSize"/> (default 64 KiB).</description></item>
    ///   <item><description>Allocate an <see cref="AdaptiveParallelism"/>
    ///         controller — a 1..8 ramp/demote controller driven by observed
    ///         RTT and FLOOD_WAIT signals; the initial concurrency is 4
    ///         to match the per-DC connection budget.</description></item>
    ///   <item><description>Fan out one task per chunk. Each task acquires
    ///         the semaphore, calls <c>upload.getFile</c>, releases on
    ///         completion (success or failure).</description></item>
    ///   <item><description>On <c>FLOOD_WAIT_X</c>: the affected chunk
    ///         <see cref="Task.Delay(int, CancellationToken)"/>s for X
    ///         seconds while still holding its slot — wait, then re-issues
    ///         the RPC. <b>Other chunks keep flowing</b>; only this slot is
    ///         blocked. The chunk-size strategy halves on flood-wait so
    ///         subsequent issues come in smaller.</description></item>
    ///   <item><description>On other errors: retry up to
    ///         <see cref="MaxRetries"/> with exponential backoff
    ///         (250 ms × 2^attempt, capped at 4 s).</description></item>
    ///   <item><description>On every completion: emit
    ///         <c>ChunkCompleted</c> + <c>TransferProgress</c>.</description></item>
    ///   <item><description>When all chunks settle, assemble bytes in
    ///         offset order, push to the cache, emit
    ///         <c>TransferCompleted</c>, and return the transfer
    ///         aggregate.</description></item>
    /// </list>
    /// </summary>
    public sealed class StartDownloadHandler
    {
        public const int ParallelismDefault = 4;
        public const int MaxRetries = 5;
        private const int AutodetectParallelism = 2;
        private const int GetFileAlignmentBytes = 4 * 1024;
        private const int GetFileMinLimitBytes = ChunkSize.Bytes64K;
        private const int GetFileMaxLimitBytes = 1024 * 1024;

        private readonly IMtProtoRpcPort _rpc;
        private readonly IMediaCache _cache;
        private readonly TransferRegistry _registry;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;
        private readonly SemaphoreSlim _autodetectGate =
            new SemaphoreSlim(AutodetectParallelism, AutodetectParallelism);

        public StartDownloadHandler(
            IMtProtoRpcPort rpc,
            IMediaCache cache,
            TransferRegistry registry,
            IEventBus bus,
            IClock clock,
            ILogger log,
            ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (cache == null) throw new ArgumentNullException("cache");
            if (registry == null) throw new ArgumentNullException("registry");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _rpc = rpc;
            _cache = cache;
            _registry = registry;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Media.StartDownload");
            _telemetry = telemetry;
        }

        public async Task<Result<MediaTransfer, MediaError>> HandleAsync(StartDownloadCommand cmd, CancellationToken ct)
        {
            if (cmd == null) return Result<MediaTransfer, MediaError>.Fail(MediaError.InvalidArgument("cmd null"));
            if (cmd.Location == null) return Result<MediaTransfer, MediaError>.Fail(MediaError.InvalidArgument("location null"));
            if (cmd.TotalSize < 0) return Result<MediaTransfer, MediaError>.Fail(MediaError.InvalidArgument("totalSize negative"));

            // Cache check first — a cache hit short-circuits the entire pipeline.
            var hit = await _cache.TryGetAsync(cmd.Location, ct).ConfigureAwait(false);
            if (hit != null)
            {
                _telemetry.Track("media.download.cache_hit", 1);
                var instant = MediaTransfer.NewDownload(MediaId.NewId(), cmd.Type, hit.SizeBytes, ChunkSize.Default, 0, _clock.UtcNow);
                instant.Start(_clock.UtcNow);
                instant.Complete(_clock.UtcNow);
                _registry.Add(instant);
                _bus.Publish(new TransferCompleted(instant.Id, hit.SizeBytes, hit.LocalPath, _clock.UtcNow));
                return Result<MediaTransfer, MediaError>.Ok(instant);
            }

            // When the caller doesn't know the file size (peer
            // photos / message thumbnails — Telegram doesn't include
            // a `size` field on those wire shapes), they pass
            // totalSize=0. A naive short-circuit would store an
            // empty byte[] in the cache and return success — every
            // avatar fetch would come back as "empty payload" and
            // the chat list would stay at initials.
            //
            // Autodetect path: drop the precomputed chunk plan and
            // fetch a single 64 KB chunk at offset 0. Then keep
            // requesting 64 KB chunks until the server returns
            // fewer bytes than asked (EOF) or we hit a 4 MB safety
            // cap. The result is the assembled file regardless of
            // its actual size — robust against future schema
            // changes that add more peer-photo shapes.
            if (cmd.TotalSize == 0)
            {
                await _autodetectGate.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    return await DownloadAutodetectAsync(cmd, ct).ConfigureAwait(false);
                }
                finally
                {
                    _autodetectGate.Release();
                }
            }

            var id = MediaId.NewId();
            var strategy = new AdaptiveChunkSize();
            var transfer = MediaTransfer.NewDownload(id, cmd.Type, cmd.TotalSize, strategy.Current, 0, _clock.UtcNow);
            _registry.Add(transfer);

            transfer.Start(_clock.UtcNow);
            _bus.Publish(new TransferStarted(id, cmd.Type, cmd.TotalSize, transfer.Chunks.Count, _clock.UtcNow));
            _telemetry.Track("media.download.started", 1);

            // Edge case: zero-chunk plan (shouldn't happen for
            // TotalSize > 0 + standard ChunkSize, but defensive).
            if (transfer.Chunks.Count == 0)
            {
                transfer.Complete(_clock.UtcNow);
                _bus.Publish(new TransferCompleted(id, 0, string.Empty, _clock.UtcNow));
                await _cache.PutAsync(cmd.Location, new byte[0], cmd.Type, ct).ConfigureAwait(false);
                return Result<MediaTransfer, MediaError>.Ok(transfer);
            }

            // Per-transfer adaptive parallelism (1..8). Initial concurrency
            // starts at 4; the controller ramps on healthy RTTs and demotes
            // on FLOOD_WAIT or sustained slow responses.
            using (var gate = new AdaptiveParallelism())
            {
                var tasks = new Task<MediaError>[transfer.Chunks.Count];
                for (int i = 0; i < transfer.Chunks.Count; i++)
                {
                    int idx = i;
                    tasks[idx] = DownloadChunkAsync(transfer, cmd.Location, transfer.Chunks[idx], gate, strategy, ct);
                }

                MediaError firstError = null;
                for (int i = 0; i < tasks.Length; i++)
                {
                    var err = await tasks[i].ConfigureAwait(false);
                    if (err != null && firstError == null) firstError = err;
                }

                if (firstError != null)
                {
                    transfer.Fail(firstError.ToString(), _clock.UtcNow);
                    _bus.Publish(new TransferFailed(id, firstError.ToString(), _clock.UtcNow));
                    _telemetry.Track("media.download.failed", 1);
                    return Result<MediaTransfer, MediaError>.Fail(firstError);
                }
            }

            // Assemble bytes in offset order.
            byte[] assembled = AssembleChunks(transfer);
            var put = await _cache.PutAsync(cmd.Location, assembled, cmd.Type, ct).ConfigureAwait(false);
            if (!put.IsOk)
            {
                transfer.Fail(put.Error.ToString(), _clock.UtcNow);
                _bus.Publish(new TransferFailed(id, put.Error.ToString(), _clock.UtcNow));
                return Result<MediaTransfer, MediaError>.Fail(put.Error);
            }

            transfer.Complete(_clock.UtcNow);
            _bus.Publish(new TransferCompleted(id, assembled.Length, put.Value.LocalPath, _clock.UtcNow));
            _telemetry.Track("media.download.completed", 1);
            _telemetry.Track("media.download.size_bytes", assembled.Length, "bytes");
            return Result<MediaTransfer, MediaError>.Ok(transfer);
        }

        /// <summary>
        /// Fetch a single upload.getFile range. Unlike HandleAsync this does
        /// not allocate a transfer aggregate, assemble chunks, or write to the
        /// media cache. It is intentionally small and predictable so the app
        /// layer can build progressive playback buffers without holding large
        /// videos in managed memory.
        /// </summary>
        public async Task<Result<byte[], MediaError>> DownloadRangeAsync(
            FileLocation location,
            long offset,
            int limit,
            CancellationToken ct)
        {
            if (location == null)
                return Result<byte[], MediaError>.Fail(MediaError.InvalidArgument("location null"));
            if (offset < 0)
                return Result<byte[], MediaError>.Fail(MediaError.InvalidArgument("offset negative"));
            if ((offset % GetFileAlignmentBytes) != 0L)
                return Result<byte[], MediaError>.Fail(MediaError.InvalidArgument("offset must be 4KB aligned"));
            if (limit <= 0)
                return Result<byte[], MediaError>.Fail(MediaError.InvalidArgument("limit must be positive"));
            if (limit > GetFileMaxLimitBytes)
                return Result<byte[], MediaError>.Fail(MediaError.ChunkTooLarge("limit exceeds 1 MiB"));

            int attempt = 0;
            while (true)
            {
                try
                {
                    int requestLimit = NormalizeGetFileLimit(limit);
                    byte[] req = TlEncoder.EncodeGetFile(location, offset, requestLimit);
                    var rpc = await _rpc.CallAsync(req, location.DcId, ct).ConfigureAwait(false);
                    if (rpc.IsOk)
                    {
                        byte[] bytes;
                        if (!TlDecoder.TryDecodeUploadFile(rpc.Value ?? new byte[0], out bytes))
                        {
                            return Result<byte[], MediaError>.Fail(
                                MediaError.ProtocolError("could not decode upload.file"));
                        }

                        return Result<byte[], MediaError>.Ok(ClampPayload(bytes, limit));
                    }

                    var err = rpc.Error;
                    if (err.Code == MediaErrorCode.FloodWait)
                    {
                        _telemetry.Track("media.range.flood_wait_seconds", err.FloodWaitSeconds, "s");
                        await Task.Delay(TimeSpan.FromSeconds(err.FloodWaitSeconds), ct).ConfigureAwait(false);
                        continue;
                    }

                    if (err.Code == MediaErrorCode.NetworkError && attempt < MaxRetries)
                    {
                        attempt += 1;
                        int backoff = 250 * (1 << Math.Min(attempt, 4));
                        if (backoff > 4000) backoff = 4000;
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                        continue;
                    }

                    return Result<byte[], MediaError>.Fail(err);
                }
                catch (OperationCanceledException)
                {
                    return Result<byte[], MediaError>.Fail(MediaError.Cancelled("range cancelled"));
                }
                catch (Exception ex)
                {
                    _log.Error("Media.DownloadRange: " + ex.Message);
                    return Result<byte[], MediaError>.Fail(MediaError.NetworkError(ex.Message));
                }
            }
        }

        private async Task<MediaError> DownloadChunkAsync(
            MediaTransfer transfer,
            FileLocation location,
            MediaChunk chunk,
            AdaptiveParallelism gate,
            AdaptiveChunkSize strategy,
            CancellationToken ct)
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                int attempt = 0;
                while (true)
                {
                    if (transfer.State == MediaTransferState.Cancelled)
                        return MediaError.Cancelled("transfer cancelled");
                    if (transfer.State == MediaTransferState.Paused)
                    {
                        // Cooperative pause loop; check every 250 ms.
                        await Task.Delay(250, ct).ConfigureAwait(false);
                        continue;
                    }

                    chunk.MarkInFlight();
                    // Stopwatch (high-res) gives the adaptive-parallelism
                    // controller an accurate elapsed value. AdaptiveChunkSize
                    // keeps using the int millisecond form — both signals are
                    // independent.
                    var sw = Stopwatch.StartNew();
                    // Zero-copy media path: build the chunk request as
                    // a byte[] (TlEncoder is byte[]-native) then wrap it once in
                    // an IBuffer for the WinRT marshal. The native side reads
                    // it via IBufferByteAccess — no Array<uint8> marshal cost.
                    // upload.getFile request bodies are ~24 bytes, but going
                    // through the buffer path also returns the chunk reply as
                    // an IBuffer so consumers can pass it to FileIO directly
                    // when the on-disk cache lands; today we still copy once
                    // to byte[] for the TL decode.
                    int requestLimit = NormalizeGetFileLimit(chunk.Size);
                    byte[] req = TlEncoder.EncodeGetFile(location, chunk.Offset, requestLimit);
                    IBuffer reqBuffer = CryptographicBuffer.CreateFromByteArray(req);
                    var rpc = await _rpc.CallBufferAsync(reqBuffer, location.DcId, ct).ConfigureAwait(false);

                    if (rpc.IsOk)
                    {
                        byte[] replyBytes;
                        // CopyToByteArray is the only managed-side copy; the
                        // native→managed marshal of the IBuffer itself is
                        // pointer-passing through ABI (one ref-count bump).
                        CryptographicBuffer.CopyToByteArray(rpc.Value, out replyBytes);
                        if (replyBytes == null) replyBytes = new byte[0];
                        byte[] bytes;
                        if (!TlDecoder.TryDecodeUploadFile(replyBytes, out bytes))
                        {
                            chunk.MarkFailed("decode failed");
                            return MediaError.ProtocolError("could not decode upload.file");
                        }

                        bytes = ClampPayload(bytes, chunk.Size);
                        chunk.MarkCompleted(bytes);
                        sw.Stop();
                        long rttMs = sw.ElapsedMilliseconds;
                        strategy.OnChunkSuccess((int)rttMs);
                        gate.OnSuccess(rttMs);

                        _bus.Publish(new ChunkCompleted(transfer.Id, chunk.Index, bytes.Length, _clock.UtcNow));
                        _bus.Publish(new TransferProgress(
                            transfer.Id,
                            new MediaProgress(transfer.BytesCompleted, transfer.TotalSize, ComputeRate(transfer)),
                            _clock.UtcNow));
                        _telemetry.Track("media.download.chunk_ms", rttMs, "ms");
                        return null;
                    }

                    var err = rpc.Error;
                    if (err.Code == MediaErrorCode.FloodWait)
                    {
                        // FLOOD_WAIT honours the seconds payload exactly.
                        // The slot is held while we wait — that is intentional:
                        // letting other parallel chunks proceed is what the
                        // semaphore is for, and freeing the slot would let a
                        // fresh chunk pile on top of the same wait.
                        strategy.OnFloodWait();
                        gate.OnFloodWait();
                        _bus.Publish(new TransferFloodWait(transfer.Id, chunk.Index, err.FloodWaitSeconds, _clock.UtcNow));
                        _telemetry.Track("media.download.flood_wait_seconds", err.FloodWaitSeconds, "s");
                        chunk.MarkPendingForRetry("FLOOD_WAIT_" + err.FloodWaitSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(err.FloodWaitSeconds), ct).ConfigureAwait(false);
                        continue; // retry the same chunk
                    }

                    if (err.Code == MediaErrorCode.NetworkError && attempt < MaxRetries)
                    {
                        attempt += 1;
                        strategy.OnTimeoutOrNetworkError();
                        chunk.MarkPendingForRetry(err.Message);
                        int backoff = 250 * (1 << Math.Min(attempt, 4)); // 250, 500, 1000, 2000, 4000
                        if (backoff > 4000) backoff = 4000;
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                        continue;
                    }

                    // Terminal failure.
                    chunk.MarkFailed(err.ToString());
                    _log.Warn("Media.DownloadChunk failed offset=" +
                        chunk.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " size=" + chunk.Size.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " limit=" + requestLimit.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        ": " + err);
                    return err;
                }
            }
            catch (OperationCanceledException)
            {
                chunk.MarkFailed("cancelled");
                return MediaError.Cancelled("chunk cancelled");
            }
            catch (Exception ex)
            {
                chunk.MarkFailed(ex.Message);
                _log.Error("Media.DownloadChunk: " + ex.Message);
                return MediaError.NetworkError(ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }

        /// <summary>
        /// Autodetect-size download for callers that don't know the file
        /// size in advance (peer photos, message thumbnails). Issues
        /// <c>upload.getFile</c> RPCs sequentially at offset 0, 64 KB,
        /// 128 KB, … until the server returns fewer bytes than asked
        /// (EOF) or we hit a 4 MB safety cap. Persists the assembled
        /// payload through <see cref="IMediaCache"/> so callers can
        /// read it back via <see cref="IMediaCache.TryGetAsync"/>.
        ///
        /// Sequential rather than parallel because we don't know how
        /// many chunks we'll need — firing 4 chunks in parallel and
        /// throwing away the past-EOF results would waste rate-limit
        /// budget. The first chunk usually carries the entire file
        /// for avatars / "m" thumbs (5-50 KB) so the latency cost is
        /// bounded by a single round-trip.
        /// </summary>
        private async Task<Result<MediaTransfer, MediaError>> DownloadAutodetectAsync(
            StartDownloadCommand cmd, CancellationToken ct)
        {
            const int ChunkBytes = 64 * 1024;       // upload.getFile rejects non-4KB-aligned limits
            const int SafetyCapBytes = 4 * 1024 * 1024;

            var id = MediaId.NewId();
            // Reserve a transfer record at chunkSize=Default. We don't
            // know the final size yet; we'll patch it via Complete()
            // once the loop terminates.
            var transfer = MediaTransfer.NewDownload(id, cmd.Type, 0, ChunkSize.Default, 0, _clock.UtcNow);
            _registry.Add(transfer);
            transfer.Start(_clock.UtcNow);
            _bus.Publish(new TransferStarted(id, cmd.Type, 0, 0, _clock.UtcNow));
            _telemetry.Track("media.download.autodetect.started", 1);

            var assembled = new System.Collections.Generic.List<byte>(ChunkBytes);
            long offset = 0;

            while (true)
            {
                if (transfer.State == MediaTransferState.Cancelled)
                    return Result<MediaTransfer, MediaError>.Fail(MediaError.Cancelled("transfer cancelled"));

                // Use the bytes-path RPC instead of the IBuffer zero-copy
                // path. The buffer path has been observed returning
                // INPUT_REQUEST_TOO_LONG
                // for tiny (~56 byte) requests — diagnostic suggests an
                // upstream framing issue specific to the IBuffer marshal
                // for short messages. The bytes path is the same one used
                // by every other bounded context and is known-good.
                byte[] req = TlEncoder.EncodeGetFile(cmd.Location, offset, ChunkBytes);
                if (_log != null && offset == 0)
                {
                    _log.Info("autodetect.req hex=" + ToHex(req, Math.Min(req.Length, 64)) +
                        " len=" + req.Length);
                }
                var rpc = await _rpc.CallAsync(req, cmd.Location.DcId, ct).ConfigureAwait(false);

                if (rpc.IsFail)
                {
                    var err = rpc.Error;
                    if (err.Code == MediaErrorCode.FloodWait)
                    {
                        // Honour FLOOD_WAIT_X then retry the same offset.
                        _bus.Publish(new TransferFloodWait(id, 0, err.FloodWaitSeconds, _clock.UtcNow));
                        await Task.Delay(TimeSpan.FromSeconds(err.FloodWaitSeconds), ct).ConfigureAwait(false);
                        continue;
                    }
                    transfer.Fail(err.ToString(), _clock.UtcNow);
                    _bus.Publish(new TransferFailed(id, err.ToString(), _clock.UtcNow));
                    _telemetry.Track("media.download.autodetect.failed", 1);
                    return Result<MediaTransfer, MediaError>.Fail(err);
                }

                byte[] replyBytes = rpc.Value ?? new byte[0];
                if (_log != null && offset == 0)
                {
                    _log.Info("autodetect.resp hex=" + ToHex(replyBytes, Math.Min(replyBytes.Length, 32)) +
                        " len=" + replyBytes.Length);
                }
                byte[] chunkBytes;
                if (!TlDecoder.TryDecodeUploadFile(replyBytes, out chunkBytes))
                {
                    var err = MediaError.ProtocolError("could not decode upload.file");
                    transfer.Fail(err.ToString(), _clock.UtcNow);
                    _bus.Publish(new TransferFailed(id, err.ToString(), _clock.UtcNow));
                    return Result<MediaTransfer, MediaError>.Fail(err);
                }

                if (chunkBytes != null && chunkBytes.Length > 0)
                {
                    assembled.AddRange(chunkBytes);
                    offset += chunkBytes.Length;
                }

                bool eof = chunkBytes == null
                    || chunkBytes.Length == 0
                    || chunkBytes.Length < ChunkBytes;
                if (eof) break;

                if (assembled.Count >= SafetyCapBytes)
                {
                    _log.Warn("Media.DownloadAutodetect hit 4 MB safety cap — truncating.");
                    break;
                }
            }

            byte[] finalBytes = assembled.ToArray();
            var put = await _cache.PutAsync(cmd.Location, finalBytes, cmd.Type, ct).ConfigureAwait(false);
            if (!put.IsOk)
            {
                transfer.Fail(put.Error.ToString(), _clock.UtcNow);
                _bus.Publish(new TransferFailed(id, put.Error.ToString(), _clock.UtcNow));
                return Result<MediaTransfer, MediaError>.Fail(put.Error);
            }

            transfer.Complete(_clock.UtcNow);
            _bus.Publish(new TransferCompleted(id, finalBytes.Length, put.Value.LocalPath, _clock.UtcNow));
            _telemetry.Track("media.download.autodetect.completed", 1);
            _telemetry.Track("media.download.autodetect.size_bytes", finalBytes.Length, "bytes");
            return Result<MediaTransfer, MediaError>.Ok(transfer);
        }

        // Diagnostic helper for hex-dumping the first N bytes of a
        // request / response — gives us visibility into the wire bytes
        // when debugging "INPUT_REQUEST_TOO_LONG" / decode failures.
        private static string ToHex(byte[] bytes, int length)
        {
            if (bytes == null || length <= 0) return string.Empty;
            int n = Math.Min(length, bytes.Length);
            var sb = new System.Text.StringBuilder(n * 2);
            for (int i = 0; i < n; i++)
            {
                sb.Append(bytes[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static int NormalizeGetFileLimit(int requested)
        {
            if (requested <= 0) return GetFileMinLimitBytes;
            if (requested <= GetFileMinLimitBytes) return GetFileMinLimitBytes;
            if (requested >= GetFileMaxLimitBytes) return GetFileMaxLimitBytes;

            int normalized = GetFileMinLimitBytes;
            while (normalized < requested && normalized < GetFileMaxLimitBytes)
                normalized *= 2;
            return normalized > GetFileMaxLimitBytes ? GetFileMaxLimitBytes : normalized;
        }

        private static byte[] ClampPayload(byte[] bytes, int maxBytes)
        {
            if (bytes == null || bytes.Length == 0) return new byte[0];
            if (maxBytes <= 0 || bytes.Length <= maxBytes) return bytes;

            var clipped = new byte[maxBytes];
            System.Buffer.BlockCopy(bytes, 0, clipped, 0, maxBytes);
            return clipped;
        }

        private static byte[] AssembleChunks(MediaTransfer transfer)
        {
            long total = 0;
            for (int i = 0; i < transfer.Chunks.Count; i++)
            {
                var p = transfer.Chunks[i].Payload;
                total += p == null ? 0 : p.Length;
            }
            var result = new byte[total];
            long write = 0;
            for (int i = 0; i < transfer.Chunks.Count; i++)
            {
                var p = transfer.Chunks[i].Payload;
                if (p == null || p.Length == 0) continue;
                System.Buffer.BlockCopy(p, 0, result, (int)write, p.Length);
                write += p.Length;
            }
            return result;
        }

        private long ComputeRate(MediaTransfer transfer)
        {
            if (!transfer.StartedUtc.HasValue) return 0;
            var elapsed = (_clock.UtcNow - transfer.StartedUtc.Value).TotalSeconds;
            if (elapsed <= 0.001) return 0;
            return (long)(transfer.BytesCompleted / elapsed);
        }
    }
}
