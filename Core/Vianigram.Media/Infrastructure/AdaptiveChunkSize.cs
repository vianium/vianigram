// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Infrastructure
{
    /// <summary>
    /// Per-transfer chunk-size strategy. Starts conservative (64 KiB) and
    /// scales up on healthy completions, scales down on FLOOD_WAIT or
    /// repeated timeouts. The strategy is a thin policy object — handlers
    /// own one per transfer and call it on every chunk completion.
    ///
    /// <para><b>Why these thresholds:</b> 512 MB-class hardware gets
    /// roughly 200–400 ms RTT to Telegram DCs over 3G. A 64 KiB chunk at
    /// 300 ms is ~210 KB/s — fine for a thumbnail and small enough to
    /// recover from a FLOOD_WAIT without retransmitting a megabyte. Once we
    /// observe a sub-300 ms RTT we ramp to 256 KiB which doubles effective
    /// throughput on a single chunk in flight; with <c>SemaphoreSlim(4)</c>
    /// we approach 3.5 MB/s peak which is roughly the device ceiling.</para>
    ///
    /// <para>The 5-success ramp threshold is borrowed from TDLib's
    /// <c>FileLoadManager</c> heuristic: ramp only after sustained success
    /// to avoid oscillating on a single lucky chunk.</para>
    /// </summary>
    public sealed class AdaptiveChunkSize
    {
        private const int RampSuccessThreshold = 5;
        private const int RttRampMs = 300;

        private ChunkSize _current;
        private int _consecutiveSuccess;
        private int _lastRttMs;

        public AdaptiveChunkSize()
        {
            _current = ChunkSize.Default;
            _consecutiveSuccess = 0;
            _lastRttMs = int.MaxValue;
        }

        public ChunkSize Current
        {
            get { return _current; }
        }

        /// <summary>
        /// Record a successful chunk completion. After
        /// <c>RampSuccessThreshold</c> back-to-back successes with RTT under
        /// <c>RttRampMs</c>, we promote to the next-larger size (capped at
        /// 1 MiB).
        /// </summary>
        public void OnChunkSuccess(int rttMs)
        {
            _consecutiveSuccess += 1;
            if (rttMs > 0) _lastRttMs = rttMs;

            if (_consecutiveSuccess >= RampSuccessThreshold && _lastRttMs < RttRampMs)
            {
                var next = _current.NextLarger();
                if (next != _current)
                {
                    _current = next;
                    _consecutiveSuccess = 0;
                }
            }
        }

        /// <summary>
        /// FLOOD_WAIT immediately demotes one step (back down to 64 KiB
        /// floor) and resets the success counter. Demotion mid-transfer is
        /// safe because each chunk's offset/size is encoded per-RPC.
        /// </summary>
        public void OnFloodWait()
        {
            _consecutiveSuccess = 0;
            _current = _current.NextSmaller();
        }

        /// <summary>
        /// A timeout / network error resets the success counter but does not
        /// demote — we want to retry at the same size before assuming the
        /// path is constrained.
        /// </summary>
        public void OnTimeoutOrNetworkError()
        {
            _consecutiveSuccess = 0;
        }
    }
}
