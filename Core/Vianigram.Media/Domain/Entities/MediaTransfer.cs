// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Domain.Entities
{
    /// <summary>
    /// Aggregate root for one in-flight upload or download. State machine:
    ///
    ///   Queued -> InProgress -> { Completed | Failed | Cancelled }
    ///       |          |
    ///       |          +-> Paused -> InProgress (Resume)
    ///       +--------------> Cancelled
    ///
    /// Identity is <see cref="MediaId"/>. Direction is fixed at construction.
    ///
    /// Concurrency contract: this aggregate is single-writer — the handler
    /// owns it and serializes all mutations. Chunk parallelism happens above
    /// the aggregate (handlers fan out chunk RPCs); chunk completion comes
    /// back through a single producer/consumer step that updates the
    /// aggregate.
    /// </summary>
    public sealed class MediaTransfer
    {
        private readonly List<MediaChunk> _chunks;

        private MediaTransfer(
            MediaId id,
            TransferDirection direction,
            FileType fileType,
            long totalSize,
            ChunkSize chunkSize,
            string fileName,
            long fileId,
            DateTime createdUtc)
        {
            Id = id;
            Direction = direction;
            FileType = fileType;
            TotalSize = totalSize;
            ChunkSize = chunkSize;
            FileName = fileName ?? string.Empty;
            FileId = fileId;
            CreatedUtc = createdUtc;
            State = MediaTransferState.Queued;
            _chunks = new List<MediaChunk>();
        }

        public MediaId Id { get; private set; }
        public TransferDirection Direction { get; private set; }
        public FileType FileType { get; private set; }
        public long TotalSize { get; private set; }
        public ChunkSize ChunkSize { get; private set; }
        public string FileName { get; private set; }

        /// <summary>
        /// Telegram <c>file_id</c> for uploads (random int64, generated client-side
        /// per the protocol) or document/photo id for downloads.
        /// </summary>
        public long FileId { get; private set; }

        public DateTime CreatedUtc { get; private set; }
        public DateTime? StartedUtc { get; private set; }
        public DateTime? CompletedUtc { get; private set; }
        public string FailureReason { get; private set; }

        public MediaTransferState State { get; private set; }

        public IList<MediaChunk> Chunks { get { return _chunks; } }

        public long BytesCompleted
        {
            get
            {
                long sum = 0;
                for (int i = 0; i < _chunks.Count; i++)
                {
                    if (_chunks[i].State == MediaChunkState.Completed) sum += _chunks[i].Size;
                }
                return sum;
            }
        }

        // ---------- Factories ----------

        public static MediaTransfer NewDownload(MediaId id, FileType type, long totalSize, ChunkSize chunkSize, long fileId, DateTime nowUtc)
        {
            if (totalSize < 0) throw new ArgumentOutOfRangeException("totalSize");
            var t = new MediaTransfer(id, TransferDirection.Download, type, totalSize, chunkSize, string.Empty, fileId, nowUtc);
            t.PlanChunks();
            return t;
        }

        public static MediaTransfer NewUpload(MediaId id, long fileId, string fileName, long totalSize, ChunkSize chunkSize, DateTime nowUtc)
        {
            if (totalSize <= 0) throw new ArgumentOutOfRangeException("totalSize");
            var t = new MediaTransfer(id, TransferDirection.Upload, FileType.Document, totalSize, chunkSize, fileName, fileId, nowUtc);
            t.PlanChunks();
            return t;
        }

        // ---------- State transitions ----------

        public void Start(DateTime nowUtc)
        {
            if (State != MediaTransferState.Queued && State != MediaTransferState.Paused)
                throw new InvalidOperationException("Cannot start a transfer in state " + State);
            State = MediaTransferState.InProgress;
            if (!StartedUtc.HasValue) StartedUtc = nowUtc;
        }

        public void Pause()
        {
            if (State != MediaTransferState.InProgress) return;
            State = MediaTransferState.Paused;
        }

        public void Resume()
        {
            if (State != MediaTransferState.Paused) return;
            State = MediaTransferState.InProgress;
        }

        public void Cancel()
        {
            if (State == MediaTransferState.Completed || State == MediaTransferState.Cancelled) return;
            State = MediaTransferState.Cancelled;
        }

        public void Complete(DateTime nowUtc)
        {
            State = MediaTransferState.Completed;
            CompletedUtc = nowUtc;
        }

        public void Fail(string reason, DateTime nowUtc)
        {
            State = MediaTransferState.Failed;
            FailureReason = reason ?? string.Empty;
            CompletedUtc = nowUtc;
        }

        // ---------- Internals ----------

        private void PlanChunks()
        {
            int cs = ChunkSize.Bytes;
            if (TotalSize == 0) return;
            long offset = 0;
            int index = 0;
            while (offset < TotalSize)
            {
                long remaining = TotalSize - offset;
                int size = remaining > cs ? cs : (int)remaining;
                _chunks.Add(new MediaChunk(index, offset, size));
                offset += size;
                index += 1;
            }
        }
    }

    public enum TransferDirection
    {
        Download = 0,
        Upload = 1
    }

    public enum MediaTransferState
    {
        Queued = 0,
        InProgress = 1,
        Paused = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }
}
