// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Media.Domain.ValueObjects
{
    /// <summary>
    /// Power-of-two chunk sizes accepted by Telegram's <c>upload.*</c>
    /// methods. The wire protocol requires the offset to be aligned to the
    /// chunk size and the size itself to divide 1 MiB evenly, which yields a
    /// fixed lattice of {64, 128, 256, 512, 1024} KiB.
    ///
    /// 64 KiB is the "polite" default — small enough to keep memory pressure
    /// low on 512 MB devices and to recover quickly from FLOOD_WAIT, large enough
    /// that we are not paying RPC overhead per byte. The
    /// <c>AdaptiveChunkSize</c> strategy ramps to 256 KiB once we observe a
    /// healthy round-trip; halves on FLOOD_WAIT.
    /// </summary>
    public struct ChunkSize : IEquatable<ChunkSize>
    {
        public const int Bytes64K = 64 * 1024;
        public const int Bytes128K = 128 * 1024;
        public const int Bytes256K = 256 * 1024;
        public const int Bytes512K = 512 * 1024;
        public const int Bytes1M = 1024 * 1024;

        private readonly int _bytes;

        private ChunkSize(int bytes)
        {
            _bytes = bytes;
        }

        public int Bytes { get { return _bytes; } }

        public static ChunkSize Default
        {
            get { return new ChunkSize(Bytes64K); }
        }

        public static ChunkSize FromBytes(int bytes)
        {
            if (!IsValid(bytes))
                throw new ArgumentOutOfRangeException("bytes", bytes, "chunk size must be one of {64K, 128K, 256K, 512K, 1M}");
            return new ChunkSize(bytes);
        }

        public static bool TryFromBytes(int bytes, out ChunkSize size)
        {
            if (!IsValid(bytes))
            {
                size = default(ChunkSize);
                return false;
            }
            size = new ChunkSize(bytes);
            return true;
        }

        public static bool IsValid(int bytes)
        {
            return bytes == Bytes64K
                || bytes == Bytes128K
                || bytes == Bytes256K
                || bytes == Bytes512K
                || bytes == Bytes1M;
        }

        /// <summary>
        /// Return the next power-of-two up to 1 MiB, or this instance if
        /// already at the cap. Used by <c>AdaptiveChunkSize</c> on a healthy
        /// chunk completion.
        /// </summary>
        public ChunkSize NextLarger()
        {
            int next = _bytes * 2;
            if (next > Bytes1M) next = Bytes1M;
            return new ChunkSize(next);
        }

        /// <summary>
        /// Return the previous power-of-two down to 64 KiB, or this instance
        /// if already at the floor. Used by <c>AdaptiveChunkSize</c> after a
        /// FLOOD_WAIT.
        /// </summary>
        public ChunkSize NextSmaller()
        {
            int prev = _bytes / 2;
            if (prev < Bytes64K) prev = Bytes64K;
            return new ChunkSize(prev);
        }

        public bool Equals(ChunkSize other) { return _bytes == other._bytes; }
        public override bool Equals(object obj) { return obj is ChunkSize && Equals((ChunkSize)obj); }
        public override int GetHashCode() { return _bytes; }
        public override string ToString() { return (_bytes / 1024) + "KiB"; }

        public static bool operator ==(ChunkSize a, ChunkSize b) { return a.Equals(b); }
        public static bool operator !=(ChunkSize a, ChunkSize b) { return !a.Equals(b); }
    }
}
