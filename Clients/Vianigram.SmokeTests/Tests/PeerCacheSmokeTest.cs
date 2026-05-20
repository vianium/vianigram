// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PeerCacheSmokeTest — verifies the InMemoryPeerCache port wiring.
//
// The per-(client,peer) access_hash cache is the shared seam that lets
// MtProtoChannelAdapter populate inputUser /
// inputChannel / inputPeerUser / inputPeerChannel with real access_hash
// values. The smoke test exercises the public surface (per-key set/get,
// Vector<User> bulk update) against the in-memory adapter; it does not
// require a live channel.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Composition.Infrastructure;

namespace Vianigram.SmokeTests.Tests
{
    public static class PeerCacheSmokeTest
    {
        private static readonly object CacheGate = new object();
        private static List<TestEntry> cachedEntries;

        public static Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var cached = TryGetCachedEntries();
            if (cached != null)
                return Task.FromResult(cached);

            var entries = new List<TestEntry>();

            // 1. Per-key set / get round-trip.
            try
            {
                ct.ThrowIfCancellationRequested();
                var cache = new InMemoryPeerCache();
                cache.SetUserAccessHash(123L, unchecked((long)0xDEADBEEFL));
                long? got = cache.GetUserAccessHash(123L);
                bool ok = got.HasValue && got.Value == unchecked((long)0xDEADBEEFL);
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "SetUserAccessHash round-trips",
                    Passed = ok,
                    Detail = ok ? "0xDEADBEEF roundtrip" : "expected 0xDEADBEEF, got " +
                        (got.HasValue ? got.Value.ToString("x") : "null")
                });
            }
            catch (System.Exception ex)
            {
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "SetUserAccessHash round-trips",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                });
            }

            // 2. Cache miss returns null (not 0L sentinel).
            try
            {
                ct.ThrowIfCancellationRequested();
                var cache = new InMemoryPeerCache();
                long? got = cache.GetUserAccessHash(999L);
                bool ok = !got.HasValue;
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "GetUserAccessHash miss returns null",
                    Passed = ok,
                    Detail = ok ? "null on miss" : "expected null, got " + got.Value.ToString("x")
                });
            }
            catch (System.Exception ex)
            {
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "GetUserAccessHash miss returns null",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                });
            }

            // 3. UpdateFromUsersSlice bulk hydration.
            try
            {
                ct.ThrowIfCancellationRequested();
                var cache = new InMemoryPeerCache();
                var slice = new List<RawUser>();
                var u1 = new RawUser(); u1.Id = 1L; u1.AccessHash = 0xAAL;
                var u2 = new RawUser(); u2.Id = 2L; u2.AccessHash = 0xBBL;
                slice.Add(u1);
                slice.Add(u2);
                cache.UpdateFromUsersSlice(slice);

                long? a = cache.GetUserAccessHash(1L);
                long? b = cache.GetUserAccessHash(2L);
                bool ok = a.HasValue && a.Value == 0xAAL
                       && b.HasValue && b.Value == 0xBBL
                       && cache.UserCount == 2;
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "UpdateFromUsersSlice hydrates both ids",
                    Passed = ok,
                    Detail = ok ? "id=1 -> 0xAA, id=2 -> 0xBB, count=2" :
                        "id=1 -> " + (a.HasValue ? a.Value.ToString("x") : "null") +
                        ", id=2 -> " + (b.HasValue ? b.Value.ToString("x") : "null") +
                        ", count=" + cache.UserCount
                });
            }
            catch (System.Exception ex)
            {
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "UpdateFromUsersSlice hydrates both ids",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                });
            }

            // 4. Channel set/get round-trip + UpdateFromChatsSlice.
            try
            {
                ct.ThrowIfCancellationRequested();
                var cache = new InMemoryPeerCache();
                cache.SetChannelAccessHash(777L, unchecked((long)0xCAFEBABEL));
                long? got = cache.GetChannelAccessHash(777L);
                bool roundTrip = got.HasValue && got.Value == unchecked((long)0xCAFEBABEL);

                var chats = new List<RawChat>();
                var c1 = new RawChat(); c1.Id = 100L; c1.AccessHash = 0xCC; c1.IsChannel = true;
                var c2 = new RawChat(); c2.Id = 200L; c2.AccessHash = 0L;    c2.IsChannel = false; // basic chat
                chats.Add(c1);
                chats.Add(c2);
                cache.UpdateFromChatsSlice(chats);
                long? channelGot = cache.GetChannelAccessHash(100L);
                long? basicGot = cache.GetChannelAccessHash(200L);
                bool channelOk = channelGot.HasValue && channelGot.Value == 0xCCL;
                bool basicSkipped = !basicGot.HasValue;

                bool ok = roundTrip && channelOk && basicSkipped;
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "Channel set/get + UpdateFromChatsSlice",
                    Passed = ok,
                    Detail = ok ? "channel hydrated, basic chat skipped" :
                        "rt=" + roundTrip + " ch=" + channelOk + " basic-skip=" + basicSkipped
                });
            }
            catch (System.Exception ex)
            {
                entries.Add(new TestEntry
                {
                    Suite = "PeerCache",
                    Name = "Channel set/get + UpdateFromChatsSlice",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                });
            }

            CacheIfSuccessful(entries);
            return Task.FromResult(entries);
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
