// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CryptoSelfTestSmoke — wraps Vianium.Crypto.CryptoSelfTest.RunAll().
//
// The C++/CX side (Vianium.Core.Crypto.WinRT projection of vianium-crypto)
// hosts the actual vector tables (AES, SHA, RSA-PSS, ChaCha20-Poly1305,
// X25519, ECDH-P256/P384). This shim translates the IList<CryptoSelfTestResult>
// result set into TestEntry rows for the runner.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;

namespace Vianigram.SmokeTests.Tests
{
    public static class CryptoSelfTestSmoke
    {
        private static readonly object CacheGate = new object();
        private static List<TestEntry> cachedEntries;

        // Deep diagnostics are opt-in because Debug.WriteLine over device
        // transport noticeably skews hot smoke timings. Add
        // VIANIGRAM_SMOKE_VERBOSE to the SmokeTests constants when tracking
        // a hang inside the native crypto suite.
        // reliably to VS Output → Debug over IpOverUsb; native OutputDebugStringW
        // apparently does NOT in this configuration.
        [Conditional("VIANIGRAM_SMOKE_VERBOSE")]
        private static void DiagLog(string message)
        {
            string stamp = DateTime.UtcNow.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture);
            Debug.WriteLine("[" + stamp + " Crypto.Diag] " + message);
        }

        public static Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            DiagLog("RunAsync entry — about to Task.Run");
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
                    DiagLog("about to call CryptoSelfTest.RunAll() - full KAT suite");
                    var sw = Stopwatch.StartNew();
                    var results = global::Vianium.Crypto.CryptoSelfTest.RunAll();
                    sw.Stop();
                    DiagLog("CryptoSelfTest.RunAll() returned in " + sw.ElapsedMilliseconds + " ms, results=" + (results == null ? "<null>" : results.Count.ToString()));
                    if (results == null)
                    {
                        entries.Add(new TestEntry
                        {
                            Suite = "Crypto",
                            Name = "CryptoSelfTest.RunAll",
                            Passed = false,
                            Detail = "RunAll returned null."
                        });
                        return entries;
                    }

                    foreach (var r in results)
                    {
                        if (r == null)
                            continue;
                        entries.Add(new TestEntry
                        {
                            Suite = "Crypto",
                            Name = r.Name,
                            Passed = r.Passed,
                            Detail = r.Detail
                        });
                    }

                    if (entries.Count == 0)
                    {
                        entries.Add(new TestEntry
                        {
                            Suite = "Crypto",
                            Name = "CryptoSelfTest.RunFast",
                            Passed = false,
                            Detail = "RunFast returned no vectors - likely wired-up failure."
                        });
                    }

                    entries.Add(new TestEntry
                    {
                        Suite = "Crypto",
                        Name = "DH/SRP 2048 full-exponent burn-in",
                        Passed = false,
                        Skipped = true,
                        Detail = "Deferred from device smoke; call CryptoSelfTest.RunExpensive() manually after bignum optimization."
                    });
                }
                catch (OperationCanceledException)
                {
                    DiagLog("OperationCanceledException — suite was cancelled");
                    entries.Add(new TestEntry
                    {
                        Suite = "Crypto",
                        Name = "CryptoSelfTest.RunFast",
                        Passed = false,
                        Detail = "Cancelled."
                    });
                }
                catch (Exception ex)
                {
                    DiagLog("Exception: " + ex.GetType().FullName + " — " + ex.Message);
                    DiagLog("StackTrace: " + (ex.StackTrace ?? "<null>"));
                    if (ex.InnerException != null)
                        DiagLog("InnerException: " + ex.InnerException.GetType().FullName + " — " + ex.InnerException.Message);
                    entries.Add(new TestEntry
                    {
                        Suite = "Crypto",
                        Name = "CryptoSelfTest.RunFast",
                        Passed = false,
                        Detail = "Exception: " + ex.GetType().Name + ": " + ex.Message
                    });
                }
                DiagLog("Task.Run lambda returning entries.Count=" + entries.Count);
                CacheIfSuccessful(entries);
                return entries;
            }, ct);
        }

        private static List<TestEntry> TryGetCachedEntries()
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
