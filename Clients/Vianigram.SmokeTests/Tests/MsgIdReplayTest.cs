// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MsgIdReplayTest — defensive unit-style test that the MTProto layer rejects
// out-of-window msg_id values and that MsgIdGenerator produces strictly
// monotonic ids.
//
// The actual generator/window logic lives on the C++ side; this shim simply
// invokes MtProtoSelfTest.RunFastStep() and surfaces ONLY the msg_id-related rows
// (anything whose Name contains "msg_id"). If no such row exists, that itself
// is a failure — the C++ self-test set must cover msg_id replay.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.SmokeTests.Tests
{
    public static class MsgIdReplayTest
    {
        public static Task<TestEntry> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var cachedMtProtoEntries = MtProtoSelfTestSmoke.TryGetCachedEntries();
            if (cachedMtProtoEntries != null)
                return Task.FromResult(BuildFromMtProtoEntries(cachedMtProtoEntries));

            return Task.Run<TestEntry>(() =>
            {
                var entry = new TestEntry
                {
                    Suite = "MTProto",
                    Name = "msg_id replay/monotonicity"
                };

                try
                {
                    ct.ThrowIfCancellationRequested();
                    int stepCount = global::Vianigram.MTProto.MtProtoSelfTest.RunFastStepCount();
                    if (stepCount <= 0)
                    {
                        entry.Passed = false;
                        entry.Detail = "MtProtoSelfTest.RunFastStepCount returned " + stepCount + ".";
                        return entry;
                    }

                    int seen = 0;
                    int failed = 0;
                    var failingNames = new List<string>();

                    for (int i = 0; i < stepCount; i++)
                    {
                        string stepName = global::Vianigram.MTProto.MtProtoSelfTest.RunFastStepName(i);
                        if (string.IsNullOrEmpty(stepName))
                            continue;
                        // Pick up msg_id, msgid, message_id variants; case-insensitive.
                        if (stepName.IndexOf("msg_id", StringComparison.OrdinalIgnoreCase) < 0 &&
                            stepName.IndexOf("msgid", StringComparison.OrdinalIgnoreCase) < 0 &&
                            stepName.IndexOf("message_id", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            continue;
                        }

                        var r = global::Vianigram.MTProto.MtProtoSelfTest.RunFastStep(i);
                        if (r == null)
                        {
                            seen++;
                            failed++;
                            failingNames.Add(stepName + " (RunFastStep returned null)");
                            continue;
                        }

                        seen++;
                        if (!r.Passed)
                        {
                            failed++;
                            failingNames.Add(r.Name + (string.IsNullOrEmpty(r.Detail) ? string.Empty : " (" + r.Detail + ")"));
                        }
                    }

                    if (seen == 0)
                    {
                        entry.Passed = false;
                        entry.Detail = "No msg_id-related vectors in MtProtoSelfTest.RunFast - coverage gap.";
                        return entry;
                    }

                    if (failed > 0)
                    {
                        entry.Passed = false;
                        entry.Detail = failed + " of " + seen + " msg_id vectors failed: " + string.Join("; ", failingNames);
                        return entry;
                    }

                    entry.Passed = true;
                    entry.Detail = "All " + seen + " msg_id vectors passed.";
                    return entry;
                }
                catch (OperationCanceledException)
                {
                    entry.Passed = false;
                    entry.Detail = "Cancelled.";
                    return entry;
                }
                catch (Exception ex)
                {
                    entry.Passed = false;
                    entry.Detail = "Exception: " + ex.GetType().Name + ": " + ex.Message;
                    return entry;
                }
            }, ct);
        }

        private static TestEntry BuildFromMtProtoEntries(IList<TestEntry> entries)
        {
            var entry = new TestEntry
            {
                Suite = "MTProto",
                Name = "msg_id replay/monotonicity"
            };

            int seen = 0;
            int failed = 0;
            var failingNames = new List<string>();

            for (int i = 0; i < entries.Count; i++)
            {
                var source = entries[i];
                if (source == null || string.IsNullOrEmpty(source.Name) ||
                    !IsMsgIdName(source.Name))
                {
                    continue;
                }

                seen++;
                if (!source.Passed)
                {
                    failed++;
                    failingNames.Add(source.Name +
                        (string.IsNullOrEmpty(source.Detail)
                            ? string.Empty
                            : " (" + source.Detail + ")"));
                }
            }

            if (seen == 0)
            {
                entry.Passed = false;
                entry.Detail = "No msg_id-related vectors in cached MtProtoSelfTest.RunFast - coverage gap.";
                return entry;
            }

            if (failed > 0)
            {
                entry.Passed = false;
                entry.Detail = failed + " of " + seen +
                    " cached msg_id vectors failed: " +
                    string.Join("; ", failingNames);
                return entry;
            }

            entry.Passed = true;
            entry.Detail = "All " + seen + " msg_id vectors passed.";
            return entry;
        }

        private static bool IsMsgIdName(string name)
        {
            return name.IndexOf("msg_id", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("msgid", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("message_id", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
