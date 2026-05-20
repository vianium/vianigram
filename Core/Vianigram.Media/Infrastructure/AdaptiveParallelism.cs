// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AdaptiveParallelism.cs
// Adaptive concurrency controller used by StartDownloadHandler /
// StartUploadHandler. Ramps concurrent chunk count between 1 and 8 based on
// observed RTT and FLOOD_WAIT signals. Initial slot count is 4. Sizes come
// from AdaptiveChunkSize — this type controls only the parallelism dimension.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.Media.Infrastructure
{
    /// <summary>
    /// Per-transfer controller that adapts concurrent chunk count between
    /// <see cref="MinSlots"/> (1) and <see cref="MaxSlots"/> (8) based on
    /// observed RTT and FLOOD_WAIT signals.
    ///
    /// <para><b>Ramp:</b> after <see cref="RampAfter"/> consecutive
    /// successful chunks with RTT &lt; <see cref="RampThresholdMs"/>, raise
    /// the slot count by one (cap <see cref="MaxSlots"/>) and release one
    /// extra semaphore permit so the new headroom is immediately usable.</para>
    ///
    /// <para><b>Demote:</b> on FLOOD_WAIT, OR after two consecutive
    /// responses with RTT &gt; <see cref="DemoteThresholdMs"/>, drop the
    /// slot count by one (floor <see cref="MinSlots"/>) and consume one
    /// permit asynchronously so the live concurrency reduces. The
    /// fire-and-forget WaitAsync(0) takes a slot only if one is currently
    /// available; otherwise the next chunk-completion implicitly hands one
    /// back at the lower cap.</para>
    ///
    /// <para>Thread-safety: <see cref="WaitAsync"/> and <see cref="Release"/>
    /// are forwarded to <see cref="SemaphoreSlim"/> directly. The slot-count
    /// math is serialized under <c>_gate</c>; the semaphore is grown via
    /// <c>Release(1)</c> and shrunk via a non-blocking <c>WaitAsync(0)</c>.</para>
    /// </summary>
    public sealed class AdaptiveParallelism : IDisposable
    {
        public const int MinSlots = 1;
        public const int MaxSlots = 8;
        public const int InitialSlots = 4;

        private const long RampThresholdMs = 200;
        private const long DemoteThresholdMs = 1000;
        private const int RampAfter = 5;
        private const int DemoteAfterSlow = 2;

        // Semaphore is constructed at MaxSlots so we can ramp up by calling
        // Release(1) without ever exceeding the underlying max-count. We
        // pre-take (MaxSlots - InitialSlots) permits in the ctor so the
        // effective starting concurrency is InitialSlots.
        private readonly SemaphoreSlim _sem;
        private int _slots;
        private int _consecutiveFastSuccess;
        private int _consecutiveSlowResponse;
        private readonly object _gate = new object();
        private bool _disposed;

        public AdaptiveParallelism()
        {
            _slots = InitialSlots;
            _sem = new SemaphoreSlim(InitialSlots, MaxSlots);
            _consecutiveFastSuccess = 0;
            _consecutiveSlowResponse = 0;
        }

        public int CurrentSlots
        {
            get { lock (_gate) { return _slots; } }
        }

        /// <summary>
        /// Acquires a slot, awaiting if none are currently free. Mirrors
        /// <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>.
        /// </summary>
        public Task WaitAsync(CancellationToken ct)
        {
            return _sem.WaitAsync(ct);
        }

        /// <summary>
        /// Releases a slot. Mirrors <see cref="SemaphoreSlim.Release()"/>.
        /// </summary>
        public void Release()
        {
            _sem.Release();
        }

        /// <summary>
        /// Records a successful chunk completion at the observed RTT.
        /// Promotes the slot count after <see cref="RampAfter"/> consecutive
        /// fast successes. Resets the slow-response counter regardless.
        /// </summary>
        public void OnSuccess(long rttMs)
        {
            lock (_gate)
            {
                _consecutiveSlowResponse = 0;
                if (rttMs < RampThresholdMs)
                {
                    _consecutiveFastSuccess++;
                    if (_consecutiveFastSuccess >= RampAfter && _slots < MaxSlots)
                    {
                        _slots++;
                        try { _sem.Release(1); }
                        catch (SemaphoreFullException) { /* race: undo locally */ _slots--; }
                        _consecutiveFastSuccess = 0;
                    }
                }
                else
                {
                    _consecutiveFastSuccess = 0;
                    if (rttMs > DemoteThresholdMs)
                    {
                        // Promote slow-response handling without throwing —
                        // OnSlowResponse already does the same accounting.
                        OnSlowResponseLocked(rttMs);
                    }
                }
            }
        }

        /// <summary>
        /// Records a FLOOD_WAIT response from the server. Drops one slot
        /// immediately (floor <see cref="MinSlots"/>) and resets the ramp
        /// counter. Safe to call from a chunk task that is currently
        /// holding a slot — the shrink takes a *different* slot via a
        /// non-blocking <see cref="SemaphoreSlim.WaitAsync(int)"/>.
        /// </summary>
        public void OnFloodWait()
        {
            lock (_gate)
            {
                _consecutiveFastSuccess = 0;
                _consecutiveSlowResponse = 0;
                ShrinkOneLocked();
            }
        }

        /// <summary>
        /// Records a slow (RTT &gt; <see cref="DemoteThresholdMs"/>)
        /// response. Demotes after <see cref="DemoteAfterSlow"/> consecutive
        /// slow responses to avoid over-reacting to a single latency spike.
        /// </summary>
        public void OnSlowResponse(long rttMs)
        {
            lock (_gate) { OnSlowResponseLocked(rttMs); }
        }

        private void OnSlowResponseLocked(long rttMs)
        {
            if (rttMs <= DemoteThresholdMs) return;
            _consecutiveFastSuccess = 0;
            _consecutiveSlowResponse++;
            if (_consecutiveSlowResponse >= DemoteAfterSlow)
            {
                ShrinkOneLocked();
                _consecutiveSlowResponse = 0;
            }
        }

        // Caller holds _gate. Drops _slots by 1 (floor MinSlots) and tries
        // to consume one permit asynchronously so the live concurrency
        // matches. The WaitAsync(0) is fire-and-forget: if no permit is
        // currently free, the next chunk-completion will hand one back at
        // the lower cap and the live concurrency catches up implicitly.
        private void ShrinkOneLocked()
        {
            if (_slots <= MinSlots) return;
            _slots--;
            // Best-effort permit consumption. We swallow exceptions so a
            // shrink never throws into the caller's success path.
            try
            {
                var task = _sem.WaitAsync(0);
                // Observe the task to avoid the unobserved-task finalizer
                // warning; we don't care about the result.
                task.ContinueWith(SwallowFaulted, TaskContinuationOptions.OnlyOnFaulted);
            }
            catch
            {
                // ignore — the semaphore is just back-pressure here.
            }
        }

        private static void SwallowFaulted(Task t)
        {
            // Touch Exception so the unobserved exception isn't escalated.
            var _ = t.Exception;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sem.Dispose();
        }
    }
}
