// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SqliteStorageSmokeTest — exercises the SQLite-backed object store.
//
// Verifies: DB opens, schema bootstraps, IObjectStore<T> round-trips the
// canonical state document, and DeleteAsync clears it. Also writes 100
// distinct scopes via separate SqliteObjectStore<TestState> instances to
// confirm the (scope, key) primary key is wired correctly.

using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Composition.Infrastructure;
using AccountAuthKeyRecord = Vianigram.Account.Ports.Outbound.AuthKeyRecord;
using Vianigram.Storage.Application;
using Vianigram.Storage.Infrastructure;
using Vianigram.Storage.Infrastructure.Repositories;
using Vianigram.Storage.Infrastructure.Sqlite;

namespace Vianigram.SmokeTests.Tests
{
    public static class SqliteStorageSmokeTest
    {
        private static readonly object FullValidationGate = new object();
        private static bool fullValidationCompleted;
        private static int quickScopeCounter;

        public static async Task<TestEntry> RunAsync(CancellationToken ct)
        {
            if (HasFullValidationCompleted())
                return await RunQuickAsync(ct).ConfigureAwait(false);

            TestEntry entry = await RunFullAsync(ct).ConfigureAwait(false);
            if (entry != null && entry.Passed)
                MarkFullValidationCompleted();

            return entry;
        }

        private static async Task<TestEntry> RunFullAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                SqliteDatabase db = SqliteDatabase.Acquire();
                if (db.Handle == IntPtr.Zero)
                {
                    return Fail("Sqlite handle is zero after Acquire — native sqlite3.dll likely missing.");
                }

                // 1. Round-trip a single scope.
                IObjectStore<TestState> store =
                    new SqliteObjectStore<TestState>(db, "smoke_main", false, null);

                TestState saved = new TestState { Counter = 42, Tag = "hello" };
                await store.SaveAsync(saved, ct).ConfigureAwait(false);

                TestState loaded = await store.LoadAsync(ct).ConfigureAwait(false);
                if (loaded == null) return Fail("LoadAsync returned null");
                if (loaded.Counter != 42 || loaded.Tag != "hello")
                {
                    return Fail("Round-trip mismatch: counter=" + loaded.Counter + " tag=" + (loaded.Tag ?? "<null>"));
                }

                // 2. Bulk: 100 scopes, write then read, verify counts.
                const int N = 100;
                List<IObjectStore<TestState>> stores = new List<IObjectStore<TestState>>(N);
                for (int i = 0; i < N; i++)
                {
                    stores.Add(new SqliteObjectStore<TestState>(db, "smoke_bulk_" + i, false, null));
                    await stores[i].SaveAsync(new TestState { Counter = i, Tag = "row" + i }, ct).ConfigureAwait(false);
                }

                int verified = 0;
                for (int i = 0; i < N; i++)
                {
                    TestState row = await stores[i].LoadAsync(ct).ConfigureAwait(false);
                    if (row != null && row.Counter == i && row.Tag == "row" + i) verified++;
                }
                if (verified != N)
                {
                    return Fail("Bulk verify failed: " + verified + "/" + N);
                }

                // 3. Delete one and confirm it returns the default value.
                await stores[0].DeleteAsync(ct).ConfigureAwait(false);
                TestState afterDelete = await stores[0].LoadAsync(ct).ConfigureAwait(false);
                if (afterDelete == null)
                {
                    return Fail("After delete, LoadAsync returned null instead of default state.");
                }
                if (afterDelete.Counter != 0 || !string.IsNullOrEmpty(afterDelete.Tag))
                {
                    return Fail("After delete, LoadAsync returned non-default state: counter="
                        + afterDelete.Counter + " tag=" + (afterDelete.Tag ?? "<null>"));
                }

                // 4. Cleanup: delete the bulk rows so subsequent runs start clean.
                for (int i = 1; i < N; i++)
                {
                    await stores[i].DeleteAsync(ct).ConfigureAwait(false);
                }
                await store.DeleteAsync(ct).ConfigureAwait(false);

                string authKeyStoreDetail = await VerifyAuthKeyStoreAsync(db, ct).ConfigureAwait(false);

                return new TestEntry
                {
                    Suite = "Storage",
                    Name = "SqliteObjectStore round-trip + bulk + delete",
                    Passed = true,
                    Detail = "round-trip ok, " + N + "/" + N + " bulk rows verified, delete reset to default."
                        + " " + authKeyStoreDetail
                };
            }
            catch (OperationCanceledException)
            {
                return Fail("cancelled");
            }
            catch (Exception ex)
            {
                return Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static async Task<TestEntry> RunQuickAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();

                SqliteDatabase db = SqliteDatabase.Acquire();
                if (db.Handle == IntPtr.Zero)
                {
                    return Fail("Sqlite handle is zero after Acquire.");
                }

                string scope = "smoke_quick_" +
                    Interlocked.Increment(ref quickScopeCounter).ToString(
                        System.Globalization.CultureInfo.InvariantCulture);
                IObjectStore<TestState> store =
                    new SqliteObjectStore<TestState>(db, scope, false, null);

                await store.SaveAsync(
                    new TestState { Counter = 7, Tag = "quick" },
                    ct).ConfigureAwait(false);

                TestState loaded = await store.LoadAsync(ct).ConfigureAwait(false);
                if (loaded == null || loaded.Counter != 7 || loaded.Tag != "quick")
                {
                    return Fail("Quick round-trip mismatch: counter=" +
                        (loaded == null
                            ? "<null>"
                            : loaded.Counter.ToString(System.Globalization.CultureInfo.InvariantCulture)) +
                        " tag=" + (loaded == null ? "<null>" : (loaded.Tag ?? "<null>")));
                }

                await store.DeleteAsync(ct).ConfigureAwait(false);
                string authKeyStoreDetail = await VerifyAuthKeyStoreAsync(db, ct)
                    .ConfigureAwait(false);

                return new TestEntry
                {
                    Suite = "Storage",
                    Name = "SqliteObjectStore round-trip + bulk + delete",
                    Passed = true,
                    Detail = "quick round-trip ok after full validation. " +
                        authKeyStoreDetail
                };
            }
            catch (OperationCanceledException)
            {
                return Fail("cancelled");
            }
            catch (Exception ex)
            {
                return Fail(ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static bool HasFullValidationCompleted()
        {
            lock (FullValidationGate)
            {
                return fullValidationCompleted;
            }
        }

        private static void MarkFullValidationCompleted()
        {
            lock (FullValidationGate)
            {
                fullValidationCompleted = true;
            }
        }

        private static TestEntry Fail(string detail)
        {
            return new TestEntry
            {
                Suite = "Storage",
                Name = "SqliteObjectStore round-trip + bulk + delete",
                Passed = false,
                Detail = detail
            };
        }

        private static async Task<string> VerifyAuthKeyStoreAsync(SqliteDatabase db, CancellationToken ct)
        {
            var protector = new PlatformDataProtector();
            var storageStore = new JsonAuthKeyStore(
                new SqliteObjectStore<AuthKeyStoreState>(
                    db, "smoke_auth_keys", true, protector));
            var bridge = new BridgeAuthKeyStore(storageStore);

            const int DcId = 2;
            const int Offset = 123;
            const ulong AuthKeyId = 0x1020304050607080UL;
            const long ServerSalt = 0x1122334455667788L;

            byte[] key = new byte[256];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(i ^ 0x5A);
            }

            await bridge.DeleteAsync(DcId, ct).ConfigureAwait(false);
            await bridge.SaveAsync(DcId, new AccountAuthKeyRecord
            {
                AuthKey = key,
                AuthKeyId = AuthKeyId,
                ServerSalt = ServerSalt,
                ServerTimeOffset = Offset
            }, ct).ConfigureAwait(false);

            Array.Clear(key, 0, key.Length);

            AccountAuthKeyRecord loaded = await bridge.LoadAsync(DcId, ct).ConfigureAwait(false);
            if (loaded == null) throw new InvalidOperationException("AuthKeyStore load returned null.");
            if (loaded.AuthKey == null || loaded.AuthKey.Length != 256)
                throw new InvalidOperationException("AuthKeyStore key length mismatch.");
            if (loaded.AuthKey[0] != 0x5A || loaded.AuthKey[255] != (byte)(255 ^ 0x5A))
                throw new InvalidOperationException("AuthKeyStore did not clone/persist key bytes.");
            if (loaded.AuthKeyId != AuthKeyId)
                throw new InvalidOperationException("AuthKeyStore AuthKeyId mismatch.");
            if (loaded.ServerSalt != ServerSalt)
                throw new InvalidOperationException("AuthKeyStore ServerSalt mismatch.");
            if (loaded.ServerTimeOffset != Offset)
                throw new InvalidOperationException("AuthKeyStore ServerTimeOffset mismatch.");

            loaded.AuthKey[0] = 0;
            AccountAuthKeyRecord loadedAgain = await bridge.LoadAsync(DcId, ct).ConfigureAwait(false);
            if (loadedAgain == null || loadedAgain.AuthKey == null || loadedAgain.AuthKey[0] != 0x5A)
                throw new InvalidOperationException("AuthKeyStore exposed mutable stored key buffer.");

            Array.Clear(loaded.AuthKey, 0, loaded.AuthKey.Length);
            Array.Clear(loadedAgain.AuthKey, 0, loadedAgain.AuthKey.Length);
            await bridge.DeleteAsync(DcId, ct).ConfigureAwait(false);

            return "auth-key store encrypted bridge round-trip ok.";
        }

        /// <summary>
        /// DataContract sample type. The store serializes via
        /// <see cref="System.Runtime.Serialization.Json.DataContractJsonSerializer"/>
        /// so all members must be opt-in.
        /// </summary>
        [DataContract]
        public sealed class TestState
        {
            [DataMember] public int Counter { get; set; }
            [DataMember] public string Tag { get; set; }
        }
    }
}
