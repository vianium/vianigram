// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Events;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Domain.Events
{
    /// <summary>
    /// Common base for Media domain events. Carries the transfer id and a
    /// timestamp; specific event types add direction-specific payload (chunk
    /// index, throughput, error reason, etc.).
    /// </summary>
    public abstract class MediaEventBase : IDomainEvent
    {
        protected MediaEventBase(MediaId id, DateTime timestampUtc)
        {
            Id = id;
            TimestampUtc = timestampUtc;
        }

        public MediaId Id { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }

    /// <summary>
    /// Emitted synchronously by Start{Download,Upload}Handler before the first
    /// chunk RPC. UI binds to this for the optimistic progress chip (M1).
    /// </summary>
    public sealed class TransferStarted : MediaEventBase
    {
        public TransferStarted(MediaId id, FileType type, long totalSize, int chunkCount, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
            FileType = type;
            TotalSize = totalSize;
            ChunkCount = chunkCount;
        }

        public FileType FileType { get; private set; }
        public long TotalSize { get; private set; }
        public int ChunkCount { get; private set; }
    }

    /// <summary>
    /// Emitted on every successful chunk completion. ChunkIndex is the
    /// 0-based index in the transfer's chunk plan; SizeBytes is the actual
    /// payload length (the last chunk is typically smaller than ChunkSize).
    /// </summary>
    public sealed class ChunkCompleted : MediaEventBase
    {
        public ChunkCompleted(MediaId id, int chunkIndex, int sizeBytes, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
            ChunkIndex = chunkIndex;
            SizeBytes = sizeBytes;
        }

        public int ChunkIndex { get; private set; }
        public int SizeBytes { get; private set; }
    }

    /// <summary>
    /// Coalesced progress update. Producers SHOULD throttle to at most a few
    /// Hz per transfer — UI is the only consumer and high-frequency updates
    /// just burn CPU. See <c>StartDownloadHandler</c> for the throttle
    /// strategy (one publish per chunk completion).
    /// </summary>
    public sealed class TransferProgress : MediaEventBase
    {
        public TransferProgress(MediaId id, MediaProgress progress, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
            Progress = progress;
        }

        public MediaProgress Progress { get; private set; }
    }

    public sealed class TransferCompleted : MediaEventBase
    {
        public TransferCompleted(MediaId id, long totalBytes, string localPath, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
            TotalBytes = totalBytes;
            LocalPath = localPath ?? string.Empty;
        }

        public long TotalBytes { get; private set; }
        public string LocalPath { get; private set; }
    }

    public sealed class TransferFailed : MediaEventBase
    {
        public TransferFailed(MediaId id, string reason, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; private set; }
    }

    public sealed class TransferPaused : MediaEventBase
    {
        public TransferPaused(MediaId id, string reason, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
            Reason = reason ?? string.Empty;
        }

        public string Reason { get; private set; }
    }

    public sealed class TransferResumed : MediaEventBase
    {
        public TransferResumed(MediaId id, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
        }
    }

    /// <summary>
    /// Emitted when a chunk RPC came back FLOOD_WAIT_X. The chunk re-enters
    /// Pending after Seconds elapses; other chunks may keep flowing.
    /// </summary>
    public sealed class TransferFloodWait : MediaEventBase
    {
        public TransferFloodWait(MediaId id, int chunkIndex, int seconds, DateTime timestampUtc)
            : base(id, timestampUtc)
        {
            ChunkIndex = chunkIndex;
            Seconds = seconds;
        }

        public int ChunkIndex { get; private set; }
        public int Seconds { get; private set; }
    }
}
