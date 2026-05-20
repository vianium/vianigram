// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProtoSelfTestSmoke — wraps Vianigram.MTProto.MtProtoSelfTest.RunFastStep().
//
// The C++/CX MTProto layer hosts offline self-tests for msg_id monotonicity,
// seq_no parity (content vs. service), salt rotation, and acknowledgement
// scheduling. This shim surfaces each result as a TestEntry row for the runner.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.SmokeTests.Tests
{
    public static class MtProtoSelfTestSmoke
    {
        private static readonly object CacheGate = new object();
        private static List<TestEntry> cachedEntries;

        [Conditional("VIANIGRAM_SMOKE_VERBOSE")]
        private static void DiagLog(string message)
        {
            string stamp = DateTime.UtcNow.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            Debug.WriteLine("[" + stamp + " MTProto.Diag] " + message);
        }

        public static Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            DiagLog("RunAsync entry - about to Task.Run");
            ct.ThrowIfCancellationRequested();

            var cached = TryGetCachedEntries();
            if (cached != null)
                return Task.FromResult(cached);

            return Task.Run(() =>
            {
                DiagLog("Task.Run lambda begin");
                var entries = new List<TestEntry>();
                try
                {
                    ct.ThrowIfCancellationRequested();
                    DiagLog("about to call MtProtoSelfTest.RunFastStepCount()");
                    int stepCount = global::Vianigram.MTProto.MtProtoSelfTest.RunFastStepCount();
                    DiagLog("RunFastStepCount returned " + stepCount);
                    if (stepCount <= 0)
                    {
                        entries.Add(new TestEntry
                        {
                            Suite = "MTProto",
                            Name = "MtProtoSelfTest.RunFastStepCount",
                            Passed = false,
                            Detail = "RunFastStepCount returned " + stepCount + "."
                        });
                        return entries;
                    }

                    for (int i = 0; i < stepCount; i++)
                    {
                        ct.ThrowIfCancellationRequested();
                        string stepName = global::Vianigram.MTProto.MtProtoSelfTest.RunFastStepName(i);
                        if (string.IsNullOrEmpty(stepName))
                            stepName = "step " + i;
                        DiagLog("about to call MtProtoSelfTest.RunFastStep(" + i + ") - " + stepName);
                        var sw = Stopwatch.StartNew();
                        var r = global::Vianigram.MTProto.MtProtoSelfTest.RunFastStep(i);
                        sw.Stop();
                        DiagLog("RunFastStep(" + i + ") returned in " + sw.ElapsedMilliseconds + " ms - " + (r == null ? "<null>" : r.Name + " passed=" + r.Passed));
                        if (r == null)
                        {
                            entries.Add(new TestEntry
                            {
                                Suite = "MTProto",
                                Name = stepName,
                                Passed = false,
                                Detail = "RunFastStep(" + i + ") returned null."
                            });
                            continue;
                        }
                        entries.Add(new TestEntry
                        {
                            Suite = "MTProto",
                            Name = r.Name,
                            Passed = r.Passed,
                            Detail = r.Detail
                        });
                    }

                    if (entries.Count == 0)
                    {
                        entries.Add(new TestEntry
                        {
                            Suite = "MTProto",
                            Name = "MtProtoSelfTest.RunFastStep",
                            Passed = false,
                            Detail = "RunFastStep returned no vectors - likely wired-up failure."
                        });
                    }

                    entries.Add(new TestEntry
                    {
                        Suite = "MTProto",
                        Name = "Pollard rho large-factor burn-in",
                        Passed = false,
                        Skipped = true,
                        Detail = "Deferred from device smoke; call MtProtoSelfTest.RunExpensive() manually."
                    });
                }
                catch (OperationCanceledException)
                {
                    DiagLog("OperationCanceledException - suite was cancelled");
                    entries.Add(new TestEntry
                    {
                        Suite = "MTProto",
                        Name = "MtProtoSelfTest.RunFast",
                        Passed = false,
                        Detail = "Cancelled."
                    });
                }
                catch (Exception ex)
                {
                    DiagLog("Exception: " + ex.GetType().FullName + " - " + ex.Message);
                    DiagLog("StackTrace: " + (ex.StackTrace ?? "<null>"));
                    if (ex.InnerException != null)
                        DiagLog("InnerException: " + ex.InnerException.GetType().FullName + " - " + ex.InnerException.Message);
                    entries.Add(new TestEntry
                    {
                        Suite = "MTProto",
                        Name = "MtProtoSelfTest.RunFast",
                        Passed = false,
                        Detail = "Exception: " + ex.GetType().Name + ": " + ex.Message
                    });
                }
                DiagLog("Task.Run lambda returning entries.Count=" + entries.Count);
                CacheIfSuccessful(entries);
                return entries;
            }, ct);
        }

        public static List<TestEntry> TryGetCachedEntries()
        {
            lock (CacheGate)
            {
                return cachedEntries == null
                    ? null
                    : DeterministicSmokeCache.Clone(cachedEntries);
            }
        }

        private static void CacheIfSuccessful(List<TestEntry> entries)
        {
            if (!DeterministicSmokeCache.CanCache(entries))
                return;

            lock (CacheGate)
            {
                if (cachedEntries == null)
                    cachedEntries = DeterministicSmokeCache.Clone(entries);
            }
        }
    }
}
