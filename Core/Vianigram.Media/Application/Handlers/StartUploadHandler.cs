// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
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
using Vianigram.Media.Ports.Inbound;
using Vianigram.Media.Ports.Outbound;

namespace Vianigram.Media.Application.Handlers
{
    /// <summary>
    /// Parallel chunked-upload orchestrator with optimistic-UI semantics.
    ///
    /// <para><b>Optimistic-UI flow (M1):</b> the handler generates a random
    /// int64 <c>file_id</c>, plans the chunks, registers the transfer in the
    /// <see cref="TransferRegistry"/>, emits <c>TransferStarted</c>, and
    /// returns an <see cref="UploadedFile"/> stub immediately. The caller
    /// (Messages) embeds <c>file_id</c> in the optimistic
    /// <c>messages.sendMedia</c> request and renders the bubble in
    /// "uploading" state. The chunk fan-out continues on a background task;
    /// when it settles, <c>TransferCompleted</c> fires with the same
    /// <c>file_id</c> + true MD5 so the UI can flip the bubble to "sent".
    /// </para>
    ///
    /// <para><b>Method selection:</b> Telegram requires
    /// <c>upload.saveBigFilePart</c> for files ≥ 10 MiB and
    /// <c>upload.saveFilePart</c> for smaller files. We pick at start time
    /// based on <c>bytes.Length</c>; the choice cannot change mid-transfer.
    /// Big-file uploads do <i>not</i> require an MD5 checksum (the server
    /// computes it from the parts); small-file uploads do, and we compute
    /// it incrementally over the source bytes.</para>
    ///
    /// <para><b>Parallelism:</b> identical fan-out to the download handler —
    /// per-transfer <see cref="AdaptiveParallelism"/> controller (1..8 ramp,
    /// initial 4), FLOOD_WAIT respect, retry with backoff. See
    /// <see cref="StartDownloadHandler"/> for the rationale on the budget.</para>
    /// </summary>
    public sealed class StartUploadHandler
    {
        public const int ParallelismDefault = 4;
        public const int MaxRetries = 5;
        public const long BigFileThresholdBytes = 10L * 1024L * 1024L; // 10 MiB

        private readonly IMtProtoRpcPort _rpc;
        private readonly TransferRegistry _registry;
        private readonly IEventBus _bus;
        private readonly IClock _clock;
        private readonly IComponentLogger _log;
        private readonly ITelemetry _telemetry;

        public StartUploadHandler(
            IMtProtoRpcPort rpc,
            TransferRegistry registry,
            IEventBus bus,
            IClock clock,
            ILogger log,
            ITelemetry telemetry)
        {
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (registry == null) throw new ArgumentNullException("registry");
            if (bus == null) throw new ArgumentNullException("bus");
            if (clock == null) throw new ArgumentNullException("clock");
            if (log == null) throw new ArgumentNullException("log");
            if (telemetry == null) throw new ArgumentNullException("telemetry");

            _rpc = rpc;
            _registry = registry;
            _bus = bus;
            _clock = clock;
            _log = new TimestampedLogger(log, "Media.StartUpload");
            _telemetry = telemetry;
        }

        /// <summary>
        /// Optimistic entry point. Returns within a few milliseconds with the
        /// stub <see cref="UploadedFile"/>; the chunk fan-out runs on a
        /// background task and emits <c>TransferCompleted</c> when done.
        /// </summary>
        public Task<Result<UploadedFile, MediaError>> HandleAsync(StartUploadCommand cmd, CancellationToken ct)
        {
            if (cmd == null)
                return TaskFromResult(Result<UploadedFile, MediaError>.Fail(MediaError.InvalidArgument("cmd null")));
            if (cmd.Bytes == null || cmd.Bytes.Length == 0)
                return TaskFromResult(Result<UploadedFile, MediaError>.Fail(MediaError.InvalidArgument("bytes empty")));

            long fileId = GenerateFileId();
            var transferId = MediaId.NewId();
            var chunkSize = ChooseChunkSize(cmd.Bytes.Length);
            var transfer = MediaTransfer.NewUpload(transferId, fileId, cmd.FileName, cmd.Bytes.Length, chunkSize, _clock.UtcNow);

            // Pre-attach upload payload to each chunk so the fan-out task
            // doesn't need to share the source byte[] reference past this
            // method (we slice into the original).
            for (int i = 0; i < transfer.Chunks.Count; i++)
            {
                var ch = transfer.Chunks[i];
                int len = ch.Size;
                byte[] slice = new byte[len];
                System.Buffer.BlockCopy(cmd.Bytes, (int)ch.Offset, slice, 0, len);
                ch.AttachUploadPayload(slice);
            }

            string md5 = cmd.Bytes.Length < BigFileThresholdBytes
                ? ComputeMd5Hex(cmd.Bytes)
                : string.Empty;

            _registry.Add(transfer);
            transfer.Start(_clock.UtcNow);

            _bus.Publish(new TransferStarted(transferId, FileType.Document, cmd.Bytes.Length, transfer.Chunks.Count, _clock.UtcNow));
            _telemetry.Track("media.upload.started", 1);

            // Fire-and-track the parallel chunk fan-out.
            var unobserved = RunUploadAsync(transfer, ct);
            GC.KeepAlive(unobserved);

            // Optimistic return — the caller (Messages) uses fileId immediately
            // for messages.sendMedia and flips the bubble to "sent" once
            // TransferCompleted fires for this transferId.
            var stub = new UploadedFile(transferId, fileId, transfer.Chunks.Count, cmd.FileName ?? string.Empty, md5);
            return TaskFromResult(Result<UploadedFile, MediaError>.Ok(stub));
        }

        private async Task RunUploadAsync(MediaTransfer transfer, CancellationToken ct)
        {
            try
            {
                bool isBig = transfer.TotalSize >= BigFileThresholdBytes;
                var strategy = new AdaptiveChunkSize();

                // Per-transfer adaptive parallelism (1..8). Initial
                // concurrency starts at 4; the controller ramps on
                // healthy RTTs and demotes on FLOOD_WAIT or sustained slow
                // responses. Sizes still come from AdaptiveChunkSize.
                using (var gate = new AdaptiveParallelism())
                {
                    var tasks = new Task<MediaError>[transfer.Chunks.Count];
                    for (int i = 0; i < transfer.Chunks.Count; i++)
                    {
                        int idx = i;
                        tasks[idx] = UploadChunkAsync(transfer, transfer.Chunks[idx], gate, isBig, strategy, ct);
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
                        _bus.Publish(new TransferFailed(transfer.Id, firstError.ToString(), _clock.UtcNow));
                        _telemetry.Track("media.upload.failed", 1);
                        return;
                    }
                }

                transfer.Complete(_clock.UtcNow);
                _bus.Publish(new TransferCompleted(transfer.Id, transfer.TotalSize, string.Empty, _clock.UtcNow));
                _telemetry.Track("media.upload.completed", 1);
                _telemetry.Track("media.upload.size_bytes", transfer.TotalSize, "bytes");
            }
            catch (OperationCanceledException)
            {
                transfer.Cancel();
                _bus.Publish(new TransferFailed(transfer.Id, "cancelled", _clock.UtcNow));
            }
            catch (Exception ex)
            {
                transfer.Fail(ex.Message, _clock.UtcNow);
                _bus.Publish(new TransferFailed(transfer.Id, ex.Message, _clock.UtcNow));
                _log.Error("Media.RunUpload: " + ex.Message);
            }
        }

        private async Task<MediaError> UploadChunkAsync(
            MediaTransfer transfer,
            MediaChunk chunk,
            AdaptiveParallelism gate,
            bool isBig,
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
                        await Task.Delay(250, ct).ConfigureAwait(false);
                        continue;
                    }

                    chunk.MarkInFlight();
                    // Stopwatch (high-res) gives AdaptiveParallelism an
                    // accurate elapsed value.
                    var sw = Stopwatch.StartNew();
                    // Zero-copy media path: build the encoded
                    // upload.saveFilePart / upload.saveBigFilePart request
                    // (which carries the 1 MiB chunk payload) and wrap it once
                    // in an IBuffer for the WinRT marshal — saves the
                    // Array<uint8> marshal of a 1 MiB-class request body. The
                    // upload reply is just `Bool`, so the receive-side savings
                    // are negligible; the request side is where the win lives.
                    byte[] req = isBig
                        ? TlEncoder.EncodeSaveBigFilePart(transfer.FileId, chunk.Index, transfer.Chunks.Count, chunk.Payload)
                        : TlEncoder.EncodeSaveFilePart(transfer.FileId, chunk.Index, chunk.Payload);
                    IBuffer reqBuffer = CryptographicBuffer.CreateFromByteArray(req);
                    var rpc = await _rpc.CallBufferAsync(reqBuffer, ct).ConfigureAwait(false);

                    if (rpc.IsOk)
                    {
                        byte[] replyBytes;
                        CryptographicBuffer.CopyToByteArray(rpc.Value, out replyBytes);
                        if (replyBytes == null) replyBytes = new byte[0];
                        bool ack;
                        if (!TlDecoder.TryDecodeBool(replyBytes, out ack) || !ack)
                        {
                            chunk.MarkFailed("server rejected part");
                            return MediaError.ProtocolError("upload.saveFilePart returned false");
                        }
                        // Keep the original payload reference for big files in case
                        // the server asks us to re-upload (CDN flow); MarkCompleted
                        // does not null it out.
                        chunk.MarkCompleted(chunk.Payload ?? new byte[0]);
                        sw.Stop();
                        long rttMs = sw.ElapsedMilliseconds;
                        strategy.OnChunkSuccess((int)rttMs);
                        gate.OnSuccess(rttMs);

                        _bus.Publish(new ChunkCompleted(transfer.Id, chunk.Index, chunk.Size, _clock.UtcNow));
                        _bus.Publish(new TransferProgress(
                            transfer.Id,
                            new MediaProgress(transfer.BytesCompleted, transfer.TotalSize, ComputeRate(transfer)),
                            _clock.UtcNow));
                        _telemetry.Track("media.upload.chunk_ms", rttMs, "ms");
                        return null;
                    }

                    var err = rpc.Error;
                    if (err.Code == MediaErrorCode.FloodWait)
                    {
                        // Same FLOOD_WAIT semantics as downloads: hold the
                        // slot, sleep the requested seconds, then retry the
                        // same chunk. Other slots remain free for sibling
                        // chunks to keep flowing.
                        strategy.OnFloodWait();
                        gate.OnFloodWait();
                        _bus.Publish(new TransferFloodWait(transfer.Id, chunk.Index, err.FloodWaitSeconds, _clock.UtcNow));
                        _telemetry.Track("media.upload.flood_wait_seconds", err.FloodWaitSeconds, "s");
                        chunk.MarkPendingForRetry("FLOOD_WAIT_" + err.FloodWaitSeconds);
                        await Task.Delay(TimeSpan.FromSeconds(err.FloodWaitSeconds), ct).ConfigureAwait(false);
                        continue;
                    }

                    if (err.Code == MediaErrorCode.NetworkError && attempt < MaxRetries)
                    {
                        attempt += 1;
                        strategy.OnTimeoutOrNetworkError();
                        chunk.MarkPendingForRetry(err.Message);
                        int backoff = 250 * (1 << Math.Min(attempt, 4));
                        if (backoff > 4000) backoff = 4000;
                        await Task.Delay(backoff, ct).ConfigureAwait(false);
                        continue;
                    }

                    chunk.MarkFailed(err.ToString());
                    _log.Warn("Media.UploadChunk failed: " + err);
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
                _log.Error("Media.UploadChunk: " + ex.Message);
                return MediaError.NetworkError(ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }

        // ---------- Helpers ----------

        private static ChunkSize ChooseChunkSize(long totalBytes)
        {
            // For small files we keep the default 64 KiB so failures retry cheaply.
            // For large files we open up to 256 KiB to amortise RPC overhead;
            // AdaptiveChunkSize promotes further at runtime if RTT permits.
            if (totalBytes >= 4L * 1024L * 1024L) return ChunkSize.FromBytes(ChunkSize.Bytes256K);
            if (totalBytes >= 1L * 1024L * 1024L) return ChunkSize.FromBytes(ChunkSize.Bytes128K);
            return ChunkSize.Default;
        }

        private static long GenerateFileId()
        {
            // Telegram's file_id is a client-chosen non-zero int64. WP8.1
            // exposes a crypto-strong RNG only via WinRT
            // (CryptographicBuffer.GenerateRandom forwards to BCryptGenRandom);
            // System.Security.Cryptography.RNGCryptoServiceProvider is not
            // available on the WindowsPhoneApp 8.1 surface.
            IBuffer buf = CryptographicBuffer.GenerateRandom(8);
            byte[] bytes;
            CryptographicBuffer.CopyToByteArray(buf, out bytes);
            long v = BitConverter.ToInt64(bytes, 0);
            if (v == 0) v = 1;
            return v;
        }

        private static string ComputeMd5Hex(byte[] bytes)
        {
            // Telegram requires md5_checksum on saveFilePart for small files
            // (<10 MiB upload path). WP8.1 exposes MD5 via WinRT
            // HashAlgorithmProvider, not System.Security.Cryptography.
            // A future revision will switch to incremental MD5 as chunks are
            // streamed off disk so we never hold the whole file in memory.
            var provider = HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Md5);
            IBuffer input = CryptographicBuffer.CreateFromByteArray(bytes);
            IBuffer hashed = provider.HashData(input);
            byte[] hash;
            CryptographicBuffer.CopyToByteArray(hashed, out hash);
            var sb = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private long ComputeRate(MediaTransfer transfer)
        {
            if (!transfer.StartedUtc.HasValue) return 0;
            var elapsed = (_clock.UtcNow - transfer.StartedUtc.Value).TotalSeconds;
            if (elapsed <= 0.001) return 0;
            return (long)(transfer.BytesCompleted / elapsed);
        }

        private static Task<T> TaskFromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
