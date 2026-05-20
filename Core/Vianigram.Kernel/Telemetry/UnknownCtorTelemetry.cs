// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// UnknownCtorTelemetry.cs — Vianigram.Kernel.Telemetry
//
// Process-wide observer for TL constructors that no decoder in the codebase
// recognizes. Used by Sync's TL decoder, MessagesUpdatesProcessor, and
// CallsUpdatesProcessor to surface schema drift early — when Telegram
// rotates a layer or adds a new Update variant, the unrecognised
// constructor would otherwise be silently dropped (TlDecoder returns null,
// the surrounding vector aborts mid-list, the user simply doesn't see the
// event). With this helper we get an EarlyLog warning the first 5 times a
// given ctor is seen, then a summary line every 100 occurrences, so the
// drift is visible in attached debug sessions and in production telemetry
// without flooding the log on a sustained mismatch.
//
// Process-static by design: the observation set spans bounded contexts
// (Sync, Messages, Calls) and the underlying state is harmless to share.
// All operations are thread-safe via a single lock.

using System.Collections.Generic;
using Vianigram.Kernel.Logging;

namespace Vianigram.Kernel.Telemetry
{
    /// <summary>
    /// Counts and logs unknown TL constructors observed by decoders so we
    /// can detect Telegram schema drift before users notice. Drop-in helper
    /// — no DI required.
    /// </summary>
    public static class UnknownCtorTelemetry
    {
        // First time we see a ctor in a context, log a Warn. Subsequent
        // sightings get a single summary every <BurstThreshold> occurrences
        // so a sustained mismatch doesn't flood the log.
        private const int LogFirstNOccurrences = 5;
        private const int BurstThreshold = 100;

        private static readonly object _gate = new object();
        private static readonly Dictionary<string, Counter> _counts =
            new Dictionary<string, Counter>();

        /// <summary>
        /// Observe an unknown TL constructor.
        /// </summary>
        /// <param name="contextName">Decoder context identifier (e.g.
        /// "Sync.TlDecoder", "Messages.UpdatesProcessor", "Calls.UpdatesProcessor").
        /// Used as the EarlyLog category and as part of the dedupe key.</param>
        /// <param name="ctor">The TL constructor id observed.</param>
        /// <param name="hint">Optional free-form hint (e.g. "inside updates vector at offset 124").</param>
        public static void Observe(string contextName, uint ctor, string hint = null)
        {
            if (string.IsNullOrEmpty(contextName)) contextName = "?";

            string key = contextName + ":" + ctor.ToString("x8");
            int seenCount;
            bool shouldLog;
            bool shouldSummarize = false;

            lock (_gate)
            {
                Counter c;
                if (!_counts.TryGetValue(key, out c))
                {
                    c = new Counter();
                    _counts[key] = c;
                }
                c.Total++;
                seenCount = c.Total;
                shouldLog = c.Total <= LogFirstNOccurrences;
                if (!shouldLog && (c.Total % BurstThreshold) == 0)
                {
                    shouldSummarize = true;
                }
            }

            if (shouldLog)
            {
                string suffix = hint == null ? string.Empty : " (" + hint + ")";
                EarlyLog.Write(
                    contextName,
                    "unknown_ctor 0x" + ctor.ToString("x8") +
                    " seen #" + seenCount + suffix);
            }
            else if (shouldSummarize)
            {
                EarlyLog.Write(
                    contextName,
                    "unknown_ctor 0x" + ctor.ToString("x8") +
                    " sustained burst: " + seenCount + " total occurrences");
            }
        }

        /// <summary>
        /// Snapshot of observation counts for diagnostics. Returns
        /// "<context>:<ctor-hex>" → totalOccurrences. Allocates; intended
        /// for occasional health-page rendering, not the hot path.
        /// </summary>
        public static IDictionary<string, int> Snapshot()
        {
            var copy = new Dictionary<string, int>();
            lock (_gate)
            {
                foreach (var kv in _counts)
                {
                    copy[kv.Key] = kv.Value.Total;
                }
            }
            return copy;
        }

        /// <summary>Reset all counters. For tests only.</summary>
        public static void ResetForTests()
        {
            lock (_gate)
            {
                _counts.Clear();
            }
        }

        private sealed class Counter
        {
            public int Total;
        }
    }
}
