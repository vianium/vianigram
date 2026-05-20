// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SmokeTestRunner — orchestrates the Vianigram smoke-test suites.
//
// Coverage:
//   - Crypto vectors pass (AES-IGE, SHA, RSA-PKCS1, MODP DH).
//   - TL serializer round-trips (vector_long, msg_container, etc.).
//   - MTProto C++-side self-tests pass (msg_id monotonicity, seq_no parity).
//   - Live MTProto handshake against Telegram test DC #2 succeeds.
//
// This is a class library — there is no Main. The runner is invoked from
// Vianigram.App or from instrumentation hosts. All test code is expected to
// never throw out of RunAllAsync; failures surface via TestEntry.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.SmokeTests.Tests;

namespace Vianigram.SmokeTests
{
    /// <summary>
    /// Aggregate result of a Vianigram smoke-test run.
    /// </summary>
    public sealed class TestSummary
    {
        public TestSummary()
        {
            Entries = new List<TestEntry>();
        }

        public int TotalRun { get; set; }
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public IList<TestEntry> Entries { get; set; }

        public bool AllPassed
        {
            get { return Failed == 0; }
        }
    }

    /// <summary>
    /// One entry in the smoke-test summary.
    /// </summary>
    public sealed class TestEntry
    {
        public string Suite { get; set; }   // "Crypto", "Tl", "MTProto", "Live"
        public string Name { get; set; }
        public bool Passed { get; set; }
        public bool Skipped { get; set; }
        public string Detail { get; set; }
        public TimeSpan Elapsed { get; set; }
    }

    /// <summary>
    /// Public entry point. Runs all wired smoke suites and returns a summary.
    /// Never throws — exceptions are captured into failed TestEntry rows.
    /// </summary>
    public static class SmokeTestRunner
    {
        // Component name for the standardized log format.
        private const string Component = "SmokeTest";
        private static readonly object WarmUpGate = new object();
        private static Task _warmUpTask;

        /// <summary>
        /// Starts a best-effort, non-destructive warm-up of thread-pool and
        /// media WinRT projections. The smoke runner app calls this after
        /// navigation so the measured run focuses on test work, not first-use
        /// module activation. No network, auth-key, or persistent storage work
        /// happens here.
        /// </summary>
        public static void BeginWarmUp()
        {
            lock (WarmUpGate)
            {
                if (_warmUpTask != null)
                    return;

                _warmUpTask = Task.Run(() => RunWarmUpBodyAsync(CancellationToken.None));
            }
        }

        public static void ShutdownWarmResources()
        {
            try
            {
                MtProtoSessionPoolSmokeTest.CloseCachedChannel();
            }
            catch (Exception ex)
            {
                EarlyLog.Write(Component, "Warm resource shutdown ignored failure: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        public static async Task<TestSummary> RunAllAsync(CancellationToken ct = default(CancellationToken))
        {
            await AwaitStartedWarmUpAsync(ct).ConfigureAwait(false);

            var summary = new TestSummary();
            var overall = Stopwatch.StartNew();

            EarlyLog.Write(Component, "=== Vianigram smoke-test run starting ===");

            // The Opus smoke is independent from MTProto/storage and is mostly
            // first-load native module cost on WP. Start it early so warm live
            // runs are gated by real dependencies, not by serialized module
            // activation. Results are still recorded in the canonical order
            // below, after the live checks complete.
            var opusSuite = BeginSuiteOnWorkerAsync(
                "Media",
                "Media.RunAll",
                () => OpusDecoderSmokeTest.RunAsync(ct),
                ct);

            // 0-3. Offline suites have no shared durable state, no auth key,
            // and no network. Start them together, then record in canonical
            // order so the output remains easy to compare between runs.
            var localSuite = BeginSuiteOnWorkerAsync(
                "Local",
                "Local.RunAll",
                () => PeerCacheSmokeTest.RunAsync(ct),
                ct);

            var proxyConfigSuite = BeginSuiteOnWorkerAsync(
                "ProxyConfig",
                "ProxyConfig.RunAll",
                () => ProxyConfigSmokeTest.RunAsync(ct),
                ct);

            var mtProxyRuntimeSuite = BeginSuiteOnWorkerAsync(
                "MtProxyRuntime",
                "MtProxyRuntime.RunAll",
                () => MtProxyRuntimeSmokeTest.RunAsync(ct),
                ct);

            var proxyPersistenceSuite = BeginSuiteOnWorkerAsync(
                "ProxyPersistence",
                "ProxyPersistence.RunAll",
                () => ProxyPersistenceSmokeTest.RunAsync(ct),
                ct);

            var callsSuite = BeginSuiteAsync(
                "Calls",
                "Calls.RunAll",
                () => CallsTlSmokeTest.RunAsync(ct),
                ct);

            var cryptoSuite = BeginSuiteAsync(
                "Crypto",
                "Crypto.RunAll",
                () => CryptoSelfTestSmoke.RunAsync(ct),
                ct);

            var tlSuite = BeginSuiteAsync(
                "Tl",
                "Tl.RunAll",
                () => TlSelfTestSmoke.RunAsync(ct),
                ct);

            var mtprotoSuite = BeginSuiteAsync(
                "MTProto",
                "MTProto.RunAll",
                () => MtProtoSelfTestSmoke.RunAsync(ct),
                ct);

            // Live auth-key availability can run alongside the offline
            // suites. Its result is still recorded later in canonical order,
            // but starting it here lets the real pool-open network work begin
            // as early as possible on warm runs.
            var liveAuthTest = BeginSingleOnWorkerAsync(
                "Live",
                "MTProto auth key availability for test DC #2",
                () => MtProtoHandshakeSmokeTest.RunAsync(ct),
                ct);

            var sessionPoolTest = BeginSingleAfterAsync(
                "Live",
                "MTProto session pool open (DC #2)",
                liveAuthTest,
                () => MtProtoSessionPoolSmokeTest.RunAsync(ct),
                ct);

            var webpSuite = BeginSuiteAfterAsync(
                "Media",
                "Media.RunAll",
                liveAuthTest,
                () => WebpDecoderSmokeTest.RunAsync(ct),
                ct);

            var sqliteTest = BeginSingleAfterAsync(
                "Local",
                "SQLite object store round-trip",
                liveAuthTest,
                () => SqliteStorageSmokeTest.RunAsync(ct),
                ct);

            await CompleteSuiteAsync(summary, localSuite).ConfigureAwait(false);
            await CompleteSuiteAsync(summary, proxyConfigSuite).ConfigureAwait(false);
            await CompleteSuiteAsync(summary, mtProxyRuntimeSuite).ConfigureAwait(false);
            await CompleteSuiteAsync(summary, proxyPersistenceSuite).ConfigureAwait(false);
            await CompleteSuiteAsync(summary, callsSuite).ConfigureAwait(false);
            await CompleteSuiteAsync(summary, cryptoSuite).ConfigureAwait(false);
            await CompleteSuiteAsync(summary, tlSuite).ConfigureAwait(false);
            await CompleteSuiteAsync(summary, mtprotoSuite).ConfigureAwait(false);

            // 4. msg_id replay/monotonicity (delegates into MTProto self-tests).
            await RunSingleAsync(
                summary,
                "MTProto",
                "msg_id replay/monotonicity",
                () => MsgIdReplayTest.RunAsync(ct),
                ct).ConfigureAwait(false);

            // 5. Live auth-key availability against Telegram test DC #2.
            // Uses the persisted encrypted key when available; generates a
            // fresh DH key only on first run / cache miss. Requires network
            // when cold or when the subsequent channel open runs.
            await CompleteSingleAsync(summary, liveAuthTest).ConfigureAwait(false);

            // 6. Connection pool — verifies the per-DC 4-socket
            // pool comes up and routes through MtProtoChannel.PoolSize.
            // Requires network (handshake + N parallel TCP connects).
            await CompleteSingleAsync(summary, sessionPoolTest).ConfigureAwait(false);

            // 7. Media decoder offline smoke. Currently exercises the
            // OpusDecoderStub via Vianigram.Media.AudioDecoder.DecodeOpusAsync.
            // When libopus is vendored (see opus_decoder.h) this suite is
            // updated to use real Opus packets / RFC 6716 vectors.
            await CompleteSuiteAsync(summary, opusSuite).ConfigureAwait(false);

            // 8. WebP decoder facade smoke. Exercises the V2 entry
            // point (Vianigram.Media.ImageDecoder.DecodeWebpV2Async). The
            // native back-end currently delegates to WebpDecoderStub (1x1
            // placeholder). When libwebp is vendored, see the webp
            // vendor markers in webp_decoder.{h,cpp} and the steps in
            // docs/native-port/04-media.md §3 — this suite gains real WebP
            // reference vectors and a per-pixel correctness assertion.
            await CompleteSuiteAsync(summary, webpSuite).ConfigureAwait(false);

            // 9. SQLite storage — local-only round-trip + bulk write.
            // Verifies the Vianigram.Storage SqliteObjectStore<T> path against
            // the SDK-bundled native sqlite3.dll. No network.
            await CompleteSingleAsync(summary, sqliteTest).ConfigureAwait(false);

            overall.Stop();

            EarlyLog.Write(Component, "=== Run complete in " +
                           overall.ElapsedMilliseconds + " ms — total=" + summary.TotalRun +
                           " passed=" + summary.Passed +
                           " failed=" + summary.Failed +
                           " skipped=" + summary.Skipped + " ===");

            return summary;
        }

        // --- Internal helpers ----------------------------------------------------

        private static Task GetWarmUpTask()
        {
            lock (WarmUpGate)
            {
                return _warmUpTask;
            }
        }

        private static async Task AwaitStartedWarmUpAsync(CancellationToken ct)
        {
            var warmUp = GetWarmUpTask();
            if (warmUp == null || warmUp.IsCompleted)
                return;

            try
            {
                await warmUp.ConfigureAwait(false);
            }
            catch
            {
                // Warm-up is an optimization only. Any real failure is still
                // surfaced by the smoke suite that exercises the same path.
            }

            ct.ThrowIfCancellationRequested();
        }

        private static async Task RunWarmUpBodyAsync(CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                // Empty inputs exercise the projection, async plumbing, and
                // native module load while avoiding allocations, network, and
                // durable state changes.
                await global::Vianigram.Media.AudioDecoder
                    .DecodeOpusAsync(new byte[0])
                    .AsTask(ct)
                    .ConfigureAwait(false);

                await global::Vianigram.Media.ImageDecoder
                    .DecodeWebpV2Async(new byte[0])
                    .AsTask(ct)
                    .ConfigureAwait(false);

                stopwatch.Stop();
                EarlyLog.Write(Component, "Warm-up complete in " +
                    stopwatch.ElapsedMilliseconds + " ms");
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                EarlyLog.Write(Component, "Warm-up cancelled after " +
                    stopwatch.ElapsedMilliseconds + " ms");
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                EarlyLog.Write(Component, "Warm-up ignored failure after " +
                    stopwatch.ElapsedMilliseconds + " ms: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static async Task RunSuiteAsync(
            TestSummary summary,
            string suite,
            Func<Task<List<TestEntry>>> body,
            CancellationToken ct)
        {
            var pending = BeginSuiteAsync(suite, suite + ".RunAll", body, ct);
            await CompleteSuiteAsync(summary, pending).ConfigureAwait(false);
        }

        private sealed class PendingSuite
        {
            public string Suite { get; set; }
            public string Name { get; set; }
            public Task<SuiteRunResult> Task { get; set; }
        }

        private sealed class SuiteRunResult
        {
            public List<TestEntry> Entries { get; set; }
            public TimeSpan Elapsed { get; set; }
        }

        private static PendingSuite BeginSuiteAsync(
            string suite,
            string name,
            Func<Task<List<TestEntry>>> body,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            LogBegin(suite, name);
            return new PendingSuite
            {
                Suite = suite,
                Name = name,
                Task = RunSuiteBodyAsync(suite, name, body, stopwatch, ct)
            };
        }

        private static PendingSuite BeginSuiteOnWorkerAsync(
            string suite,
            string name,
            Func<Task<List<TestEntry>>> body,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            LogBegin(suite, name);
            return new PendingSuite
            {
                Suite = suite,
                Name = name,
                Task = Task.Run(
                    () => RunSuiteBodyAsync(suite, name, body, stopwatch, ct),
                    ct)
            };
        }

        private static PendingSuite BeginSuiteAfterAsync(
            string suite,
            string name,
            PendingSingle dependency,
            Func<Task<List<TestEntry>>> body,
            CancellationToken ct)
        {
            return new PendingSuite
            {
                Suite = suite,
                Name = name,
                Task = RunSuiteAfterAsync(suite, name, dependency.Task, body, ct)
            };
        }

        private static async Task<SuiteRunResult> RunSuiteAfterAsync(
            string suite,
            string name,
            Task<SingleRunResult> dependency,
            Func<Task<List<TestEntry>>> body,
            CancellationToken ct)
        {
            try
            {
                await dependency.ConfigureAwait(false);
            }
            catch
            {
                // Dependent suite still reports its own failure. The
                // dependency result is recorded separately in canonical order.
            }

            var stopwatch = Stopwatch.StartNew();
            LogBegin(suite, name);
            return await RunSuiteBodyAsync(
                suite, name, body, stopwatch, ct).ConfigureAwait(false);
        }

        private static async Task<SuiteRunResult> RunSuiteBodyAsync(
            string suite,
            string name,
            Func<Task<List<TestEntry>>> body,
            Stopwatch stopwatch,
            CancellationToken ct)
        {
            List<TestEntry> entries;
            try
            {
                entries = await body().ConfigureAwait(false);
                if (entries == null)
                {
                    entries = new List<TestEntry>
                    {
                        new TestEntry
                        {
                            Suite = suite,
                            Name = name,
                            Passed = false,
                            Detail = "Suite returned null result list."
                        }
                    };
                }
            }
            catch (OperationCanceledException)
            {
                entries = new List<TestEntry>
                {
                    new TestEntry
                    {
                        Suite = suite,
                        Name = name,
                        Passed = false,
                        Detail = "Suite cancelled."
                    }
                };
            }
            catch (Exception ex)
            {
                entries = new List<TestEntry>
                {
                    new TestEntry
                    {
                        Suite = suite,
                        Name = name,
                        Passed = false,
                        Detail = "Suite threw: " + ex.GetType().Name + ": " + ex.Message
                    }
                };
            }
            stopwatch.Stop();
            return new SuiteRunResult
            {
                Entries = entries,
                Elapsed = stopwatch.Elapsed
            };
        }

        private static async Task CompleteSuiteAsync(TestSummary summary, PendingSuite pending)
        {
            var result = await pending.Task.ConfigureAwait(false);
            var entries = result.Entries;

            LogEndSuite(pending.Suite, pending.Name, entries.Count, result.Elapsed);

            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;
                if (string.IsNullOrEmpty(entry.Suite))
                    entry.Suite = pending.Suite;
                if (entry.Elapsed == TimeSpan.Zero)
                    entry.Elapsed = result.Elapsed;
                Record(summary, entry);
            }
        }

        private static async Task RunSingleAsync(
            TestSummary summary,
            string suite,
            string name,
            Func<Task<TestEntry>> body,
            CancellationToken ct)
        {
            var pending = BeginSingleAsync(suite, name, body, ct);
            await CompleteSingleAsync(summary, pending).ConfigureAwait(false);
        }

        private sealed class PendingSingle
        {
            public string Suite { get; set; }
            public string Name { get; set; }
            public Task<SingleRunResult> Task { get; set; }
        }

        private sealed class SingleRunResult
        {
            public TestEntry Entry { get; set; }
            public TimeSpan Elapsed { get; set; }
        }

        private static PendingSingle BeginSingleAsync(
            string suite,
            string name,
            Func<Task<TestEntry>> body,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            LogBegin(suite, name);
            return new PendingSingle
            {
                Suite = suite,
                Name = name,
                Task = RunSingleBodyAsync(suite, name, body, stopwatch, ct)
            };
        }

        private static PendingSingle BeginSingleOnWorkerAsync(
            string suite,
            string name,
            Func<Task<TestEntry>> body,
            CancellationToken ct)
        {
            var stopwatch = Stopwatch.StartNew();
            LogBegin(suite, name);
            return new PendingSingle
            {
                Suite = suite,
                Name = name,
                Task = Task.Run(
                    () => RunSingleBodyAsync(suite, name, body, stopwatch, ct),
                    ct)
            };
        }

        private static PendingSingle BeginSingleAfterAsync(
            string suite,
            string name,
            PendingSingle dependency,
            Func<Task<TestEntry>> body,
            CancellationToken ct)
        {
            return new PendingSingle
            {
                Suite = suite,
                Name = name,
                Task = RunSingleAfterAsync(suite, name, dependency.Task, body, ct)
            };
        }

        private static async Task<SingleRunResult> RunSingleAfterAsync(
            string suite,
            string name,
            Task<SingleRunResult> dependency,
            Func<Task<TestEntry>> body,
            CancellationToken ct)
        {
            try
            {
                await dependency.ConfigureAwait(false);
            }
            catch
            {
                // Dependent test still reports its own failure. The
                // dependency result is recorded separately in canonical order.
            }

            var stopwatch = Stopwatch.StartNew();
            LogBegin(suite, name);
            return await RunSingleBodyAsync(
                suite, name, body, stopwatch, ct).ConfigureAwait(false);
        }

        private static async Task<SingleRunResult> RunSingleBodyAsync(
            string suite,
            string name,
            Func<Task<TestEntry>> body,
            Stopwatch stopwatch,
            CancellationToken ct)
        {
            TestEntry entry;
            try
            {
                entry = await body().ConfigureAwait(false);
                if (entry == null)
                {
                    entry = new TestEntry
                    {
                        Suite = suite,
                        Name = name,
                        Passed = false,
                        Detail = "Test returned null."
                    };
                }
            }
            catch (OperationCanceledException)
            {
                entry = new TestEntry
                {
                    Suite = suite,
                    Name = name,
                    Passed = false,
                    Detail = "Test cancelled."
                };
            }
            catch (Exception ex)
            {
                entry = new TestEntry
                {
                    Suite = suite,
                    Name = name,
                    Passed = false,
                    Detail = "Test threw: " + ex.GetType().Name + ": " + ex.Message
                };
            }
            stopwatch.Stop();
            return new SingleRunResult
            {
                Entry = entry,
                Elapsed = stopwatch.Elapsed
            };
        }

        private static async Task CompleteSingleAsync(TestSummary summary, PendingSingle pending)
        {
            var result = await pending.Task.ConfigureAwait(false);
            var entry = result.Entry;

            LogEndSingle(pending.Suite, pending.Name, result.Elapsed);

            if (string.IsNullOrEmpty(entry.Suite))
                entry.Suite = pending.Suite;
            if (string.IsNullOrEmpty(entry.Name))
                entry.Name = pending.Name;
            if (entry.Elapsed == TimeSpan.Zero)
                entry.Elapsed = result.Elapsed;

            Record(summary, entry);
        }

        private static void Record(TestSummary summary, TestEntry entry)
        {
            summary.Entries.Add(entry);
            summary.TotalRun++;
            if (entry.Skipped)
            {
                summary.Skipped++;
                EarlyLog.Write(Component, "SKIP [" + entry.Suite + "] " + entry.Name +
                               " (" + (long)entry.Elapsed.TotalMilliseconds + " ms)" +
                               (string.IsNullOrEmpty(entry.Detail) ? string.Empty : " - " + entry.Detail));
            }
            else if (entry.Passed)
            {
                summary.Passed++;
                LogPass(entry);
            }
            else
            {
                summary.Failed++;
                EarlyLog.Write(Component, "FAIL [" + entry.Suite + "] " + entry.Name +
                               " (" + (long)entry.Elapsed.TotalMilliseconds + " ms) — " +
                               (string.IsNullOrEmpty(entry.Detail) ? "<no detail>" : entry.Detail));
            }
        }

        [Conditional("VIANIGRAM_SMOKE_VERBOSE")]
        private static void LogBegin(string suite, string name)
        {
            EarlyLog.Write(Component, "BEGIN [" + suite + "] " + name);
        }

        [Conditional("VIANIGRAM_SMOKE_VERBOSE")]
        private static void LogEndSuite(
            string suite,
            string name,
            int count,
            TimeSpan elapsed)
        {
            EarlyLog.Write(Component, "END [" + suite + "] " + name +
                           " entries=" + count +
                           " elapsed=" + (long)elapsed.TotalMilliseconds + " ms");
        }

        [Conditional("VIANIGRAM_SMOKE_VERBOSE")]
        private static void LogEndSingle(string suite, string name, TimeSpan elapsed)
        {
            EarlyLog.Write(Component, "END [" + suite + "] " + name +
                           " elapsed=" + (long)elapsed.TotalMilliseconds + " ms");
        }

        [Conditional("VIANIGRAM_SMOKE_VERBOSE")]
        private static void LogPass(TestEntry entry)
        {
            EarlyLog.Write(Component, "PASS [" + entry.Suite + "] " + entry.Name +
                           " (" + (long)entry.Elapsed.TotalMilliseconds + " ms)" +
                           (string.IsNullOrEmpty(entry.Detail) ? string.Empty : " — " + entry.Detail));
        }
    }
}
