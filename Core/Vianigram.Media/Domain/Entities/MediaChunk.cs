// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Domain.Entities
{
    /// <summary>
    /// One sub-range of a transfer. Identity = (TransferId, ChunkIndex).
    /// Tracks local state machine: Pending -> InFlight -> (Completed | Failed).
    /// FloodWait is a soft pause: the chunk re-enters Pending after the wait
    /// has elapsed. Retry count grows monotonically across all failure modes.
    /// </summary>
    public sealed class MediaChunk
    {
        public MediaChunk(int index, long offset, int size)
        {
            if (index < 0) throw new ArgumentOutOfRangeException("index");
            if (offset < 0) throw new ArgumentOutOfRangeException("offset");
            if (size <= 0) throw new ArgumentOutOfRangeException("size");

            Index = index;
            Offset = offset;
            Size = size;
            State = MediaChunkState.Pending;
            RetryCount = 0;
        }

        public int Index { get; private set; }
        public long Offset { get; private set; }
        public int Size { get; private set; }
        public MediaChunkState State { get; private set; }
        public int RetryCount { get; private set; }
        public string LastErrorReason { get; private set; }

        /// <summary>
        /// Bytes for this chunk once the chunk completes. For uploads the
        /// payload is supplied at construction time and we never null it.
        /// For downloads it is filled in on success and read by the assembler.
        /// </summary>
        public byte[] Payload { get; private set; }

        public void MarkInFlight()
        {
            State = MediaChunkState.InFlight;
            LastErrorReason = null;
        }

        public void MarkCompleted(byte[] payload)
        {
            Payload = payload ?? new byte[0];
            State = MediaChunkState.Completed;
            LastErrorReason = null;
        }

        public void MarkFailed(string reason)
        {
            RetryCount += 1;
            State = MediaChunkState.Failed;
            LastErrorReason = reason ?? string.Empty;
        }

        public void MarkPendingForRetry(string reason)
        {
            RetryCount += 1;
            State = MediaChunkState.Pending;
            LastErrorReason = reason ?? string.Empty;
        }

        public void AttachUploadPayload(byte[] payload)
        {
            if (payload == null) throw new ArgumentNullException("payload");
            if (payload.Length > Size)
                throw new ArgumentException("payload exceeds chunk size", "payload");
            Payload = payload;
        }
    }

    public enum MediaChunkState
    {
        Pending = 0,
        InFlight = 1,
        Completed = 2,
        Failed = 3
    }
}
