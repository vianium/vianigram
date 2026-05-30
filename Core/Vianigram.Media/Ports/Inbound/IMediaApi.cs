// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.Entities;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Media bounded context (V1). All operations
    /// return <see cref="Result{T,TError}"/> — exceptions never cross this
    /// boundary.
    ///
    /// <para><b>Optimistic upload (M1):</b> <see cref="UploadAsync"/> returns
    /// fast with a stub <see cref="UploadedFile"/> carrying the
    /// client-generated <c>file_id</c>; the actual chunk fan-out runs on a
    /// background task. Callers (typically Messages) bind to the <c>file_id</c>
    /// and listen on <see cref="ProgressChanged"/> +
    /// <c>TransferCompleted</c> for the final ACK.</para>
    ///
    /// <para><b>Parallel chunks:</b> Both directions fan out across at most
    /// four concurrent chunk RPCs — the same default our connection pool
    /// sustains per-DC. <c>SemaphoreSlim(4)</c> gates the fan-out
    /// inside the handlers.</para>
    ///
    /// <para><b>FLOOD_WAIT:</b> the affected chunk waits the requested
    /// seconds and re-enters the pending queue; other chunks may proceed in
    /// parallel. The transfer fails only if a chunk exhausts its retry
    /// budget.</para>
    /// </summary>
    public interface IMediaApi
    {
        /// <summary>
        /// Begin a parallel chunked download for a server-resolved
        /// <see cref="FileLocation"/>. Returns a <see cref="MediaTransfer"/>
        /// snapshot in <c>InProgress</c> once the first chunk dispatches; the
        /// task completes when all chunks settle (success, fail, or cancel).
        /// </summary>
        Task<Result<MediaTransfer, MediaError>> DownloadAsync(FileLocation location, FileType type, long totalSize, CancellationToken ct);

        /// <summary>
        /// Read one Telegram media range without assembling or caching the
        /// whole file. This is the low-level primitive used by progressive
        /// audio/video buffering: callers write the returned bytes to a local
        /// temp file, start playback after an initial buffer, then keep asking
        /// for later ranges in the background.
        /// </summary>
        Task<Result<byte[], MediaError>> DownloadRangeAsync(FileLocation location, long offset, int limit, CancellationToken ct);

        /// <summary>
        /// Optimistic upload. Returns within a few milliseconds with a stub
        /// <see cref="UploadedFile"/> whose <c>FileId</c> is the
        /// client-generated random int64; the chunk fan-out runs on a
        /// background task and emits <c>TransferCompleted</c> when done.
        /// </summary>
        Task<Result<UploadedFile, MediaError>> UploadAsync(byte[] bytes, string fileName, CancellationToken ct);

        Task<Result<Domain.ValueObjects.Unit, MediaError>> PauseAsync(MediaId id, CancellationToken ct);
        Task<Result<Domain.ValueObjects.Unit, MediaError>> ResumeAsync(MediaId id, CancellationToken ct);
        Task<Result<Domain.ValueObjects.Unit, MediaError>> CancelAsync(MediaId id, CancellationToken ct);

        /// <summary>
        /// Synchronous read of the current transfer aggregate. Returns
        /// <c>null</c> if the id is unknown (e.g., already evicted from the
        /// in-memory registry). Safe to call from the UI thread.
        /// </summary>
        MediaTransfer GetTransfer(MediaId id);

        /// <summary>
        /// Coalesced UI-side progress stream. Listeners receive one event per
        /// chunk completion; throughput is computed against transfer-start.
        /// Subscribers that want chunk-level granularity should bind to the
        /// <c>ChunkCompleted</c> domain event on the bus directly.
        /// </summary>
        event EventHandler<MediaProgressEventArgs> ProgressChanged;
    }

    /// <summary>
    /// Result handle returned by <see cref="IMediaApi.UploadAsync"/>. The
    /// <c>FileId</c> is suitable for embedding in an <c>inputFile</c> /
    /// <c>inputFileBig</c> wrapper used by <c>messages.sendMedia</c>; the
    /// upload has not necessarily completed by the time this object is
    /// returned (M1 optimistic-UI). Subscribe to the <c>TransferCompleted</c>
    /// event to know when the server has acknowledged the final chunk.
    /// </summary>
    public sealed class UploadedFile
    {
        public UploadedFile(MediaId transferId, long fileId, int parts, string name, string md5Checksum)
        {
            TransferId = transferId;
            FileId = fileId;
            Parts = parts;
            Name = name ?? string.Empty;
            Md5Checksum = md5Checksum ?? string.Empty;
        }

        public MediaId TransferId { get; private set; }
        public long FileId { get; private set; }
        public int Parts { get; private set; }
        public string Name { get; private set; }
        public string Md5Checksum { get; private set; }
    }
}
