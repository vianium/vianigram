// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;

namespace Vianigram.SmokeTests.Tests
{
    internal static class DeterministicSmokeCache
    {
        public static bool CanCache(IList<TestEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return false;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                    return false;

                if (!entry.Passed && !entry.Skipped)
                    return false;
            }

            return true;
        }

        public static List<TestEntry> Clone(IList<TestEntry> entries)
        {
            var clone = new List<TestEntry>();
            if (entries == null)
                return clone;

            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null)
                    continue;

                clone.Add(new TestEntry
                {
                    Suite = entry.Suite,
                    Name = entry.Name,
                    Passed = entry.Passed,
                    Skipped = entry.Skipped,
                    Detail = entry.Detail
                });
            }

            return clone;
        }
    }
}
