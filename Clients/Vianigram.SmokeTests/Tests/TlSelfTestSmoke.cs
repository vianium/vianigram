// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// TlSelfTestSmoke — wraps Vianigram.Tl.TlSelfTest.RunAll().
//
// The C++/CX TL serializer hosts vector round-trips (vector_long, msg_container,
// gzip_packed, primitive ints/strings/bytes). This shim surfaces each result as
// a TestEntry row for the runner.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.SmokeTests.Tests
{
    public static class TlSelfTestSmoke
    {
        private static readonly object CacheGate = new object();
        private static List<TestEntry> cachedEntries;

        public static Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var cached = TryGetCachedEntries();
            if (cached != null)
                return Task.FromResult(cached);

            return Task.Run(() =>
            {
                var entries = new List<TestEntry>();
                try
                {
                    ct.ThrowIfCancellationRequested();
                    var results = global::Vianigram.Tl.TlSelfTest.RunAll();
                    if (results == null)
                    {
                        entries.Add(new TestEntry
                        {
                            Suite = "Tl",
                            Name = "TlSelfTest.RunAll",
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
                            Suite = "Tl",
                            Name = r.Name,
                            Passed = r.Passed,
                            Detail = r.Detail
                        });
                    }

                    if (entries.Count == 0)
                    {
                        entries.Add(new TestEntry
                        {
                            Suite = "Tl",
                            Name = "TlSelfTest.RunAll",
                            Passed = false,
                            Detail = "RunAll returned no vectors — likely wired-up failure."
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    entries.Add(new TestEntry
                    {
                        Suite = "Tl",
                        Name = "TlSelfTest.RunAll",
                        Passed = false,
                        Detail = "Cancelled."
                    });
                }
                catch (Exception ex)
                {
                    entries.Add(new TestEntry
                    {
                        Suite = "Tl",
                        Name = "TlSelfTest.RunAll",
                        Passed = false,
                        Detail = "Exception: " + ex.GetType().Name + ": " + ex.Message
                    });
                }
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
