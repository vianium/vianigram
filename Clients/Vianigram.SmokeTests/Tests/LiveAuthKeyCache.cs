// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Composition.Infrastructure;
using Vianigram.Storage.Infrastructure;
using Vianigram.Storage.Infrastructure.Repositories;
using Vianigram.Storage.Infrastructure.Sqlite;
using AccountAuthKeyRecord = Vianigram.Account.Ports.Outbound.AuthKeyRecord;

namespace Vianigram.SmokeTests.Tests
{
    internal sealed class LiveAuthKeyMaterial
    {
        public string Host { get; set; }
        public int Port { get; set; }
        public byte[] AuthKeyBytes { get; set; }
        public ulong AuthKeyId { get; set; }
        public long InitialServerSalt { get; set; }
        public int ServerTimeOffset { get; set; }
        public string Source { get; set; }

        public LiveAuthKeyMaterial Clone()
        {
            return new LiveAuthKeyMaterial
            {
                Host = Host,
                Port = Port,
                AuthKeyBytes = CloneBytes(AuthKeyBytes),
                AuthKeyId = AuthKeyId,
                InitialServerSalt = InitialServerSalt,
                ServerTimeOffset = ServerTimeOffset,
                Source = Source
            };
        }

        public void Clear()
        {
            if (AuthKeyBytes != null)
                Array.Clear(AuthKeyBytes, 0, AuthKeyBytes.Length);
        }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source == null)
                return null;

            byte[] copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }

    internal static class LiveAuthKeyCache
    {
        public const string TestDcHost = "149.154.167.40";
        public const int TestDcPort = 443;
        public const int TestDcId = 2;

        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan GenerateTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan PersistedLoadTimeout = TimeSpan.FromSeconds(5);

        private static readonly object Gate = new object();
        private static LiveAuthKeyMaterial cached;
        private static Task<LiveAuthKeyMaterial> pending;

        public static async Task<LiveAuthKeyMaterial> GetOrCreateAsync(
            Stopwatch stopwatch,
            CancellationToken ct,
            string owner)
        {
            LiveAuthKeyMaterial cachedCopy = TryCloneCached();
            if (cachedCopy != null)
            {
                cachedCopy.Source = "memory";
                LiveSmokeTestSupport.Diag(owner + " reusing cached auth_key_id=0x" +
                    cachedCopy.AuthKeyId.ToString("x16", CultureInfo.InvariantCulture));
                return cachedCopy;
            }

            LiveAuthKeyMaterial persisted = await TryLoadPersistedAsync(
                owner, ct).ConfigureAwait(false);
            if (persisted != null)
            {
                persisted.Source = "persisted";
                lock (Gate)
                {
                    if (cached == null)
                        cached = persisted.Clone();
                }
                LiveSmokeTestSupport.Diag(owner + " loaded persisted auth_key_id=0x" +
                    persisted.AuthKeyId.ToString("x16", CultureInfo.InvariantCulture));
                return persisted;
            }

            Task<LiveAuthKeyMaterial> task;
            lock (Gate)
            {
                if (cached != null)
                {
                    task = null;
                }
                else
                {
                    if (pending == null || pending.IsCanceled || pending.IsFaulted)
                        pending = AcquireAsync(stopwatch, ct, owner);
                    task = pending;
                }
            }

            if (task == null)
                return TryCloneCached();

            try
            {
                LiveAuthKeyMaterial material = await task.ConfigureAwait(false);
                LiveAuthKeyMaterial copy = material.Clone();
                if (string.IsNullOrEmpty(copy.Source))
                    copy.Source = "cold";
                return copy;
            }
            finally
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    lock (Gate)
                    {
                        if (pending == task)
                            pending = null;
                    }
                }
            }
        }

        private static LiveAuthKeyMaterial TryCloneCached()
        {
            lock (Gate)
            {
                return cached == null ? null : cached.Clone();
            }
        }

        private static async Task<LiveAuthKeyMaterial> AcquireAsync(
            Stopwatch stopwatch,
            CancellationToken ct,
            string owner)
        {
            global::Vianigram.MTProto.MtProtoConnection conn = null;
            global::Vianigram.MTProto.AuthKeyResult result = null;
            try
            {
                LiveSmokeTestSupport.Diag(
                    owner + " auth-key acquisition target=" + TestDcHost + ":" + TestDcPort);

                conn = await LiveSmokeTestSupport.AwaitAsync(
                    global::Vianigram.MTProto.MtProtoConnection
                        .ConnectAsync(TestDcHost, TestDcPort),
                    ConnectTimeout,
                    owner + " ConnectAsync",
                    stopwatch,
                    ct,
                    delegate(global::Vianigram.MTProto.MtProtoConnection lateConn)
                    {
                        if (lateConn != null)
                            lateConn.Close();
                    }).ConfigureAwait(false);

                if (conn == null)
                    throw new InvalidOperationException("ConnectAsync returned null.");

                result = await LiveSmokeTestSupport.AwaitAsync(
                    conn.GenerateAuthKeyAsync(),
                    GenerateTimeout,
                    owner + " GenerateAuthKeyAsync",
                    stopwatch,
                    ct).ConfigureAwait(false);

                LiveAuthKeyMaterial material = ValidateAndCopy(result);
                material.Source = "cold";

                lock (Gate)
                {
                    if (cached == null)
                        cached = material.Clone();
                    pending = null;
                }

                await TrySavePersistedAsync(material, owner, ct).ConfigureAwait(false);

                LiveSmokeTestSupport.Diag(owner + " cached auth_key_id=0x" +
                    material.AuthKeyId.ToString("x16", CultureInfo.InvariantCulture));
                return material;
            }
            catch (TimeoutException ex)
            {
                string nativeDiag = conn == null ? string.Empty : conn.LastDiagnostic;
                if (!string.IsNullOrEmpty(nativeDiag))
                {
                    throw new TimeoutException(
                        ex.Message + " NativeLastDiagnostic=" + nativeDiag);
                }
                throw;
            }
            finally
            {
                if (result != null && result.AuthKeyBytes != null)
                    Array.Clear(result.AuthKeyBytes, 0, result.AuthKeyBytes.Length);

                if (conn != null)
                {
                    try { conn.Close(); }
                    catch (Exception closeEx)
                    {
                        LiveSmokeTestSupport.Diag(owner + " Connection.Close threw " +
                            closeEx.GetType().Name + ": " + closeEx.Message);
                    }
                }
            }
        }

        private static LiveAuthKeyMaterial ValidateAndCopy(
            global::Vianigram.MTProto.AuthKeyResult result)
        {
            if (result == null)
                throw new InvalidOperationException("GenerateAuthKeyAsync returned null.");

            if (!result.Success)
                throw new InvalidOperationException("GenerateAuthKeyAsync failed: " +
                    (string.IsNullOrEmpty(result.ErrorMessage)
                        ? "<no error message>"
                        : result.ErrorMessage));

            int keyLen = result.AuthKeyBytes == null ? 0 : result.AuthKeyBytes.Length;
            if (keyLen != 256)
            {
                throw new InvalidOperationException(
                    "AuthKey length wrong: expected 256, got " +
                    keyLen.ToString(CultureInfo.InvariantCulture));
            }

            if (result.AuthKeyId == 0)
                throw new InvalidOperationException("AuthKeyId is zero.");

            return new LiveAuthKeyMaterial
            {
                Host = TestDcHost,
                Port = TestDcPort,
                AuthKeyBytes = result.AuthKeyBytes == null
                    ? null
                    : (byte[])result.AuthKeyBytes.Clone(),
                AuthKeyId = result.AuthKeyId,
                InitialServerSalt = result.InitialServerSalt,
                ServerTimeOffset = result.ServerTimeOffset
            };
        }

        private static async Task<LiveAuthKeyMaterial> TryLoadPersistedAsync(
            string owner,
            CancellationToken outerToken)
        {
            BridgeAuthKeyStore store = TryCreatePersistentStore(owner);
            if (store == null)
                return null;

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(outerToken))
            {
                cts.CancelAfter(PersistedLoadTimeout);
                AccountAuthKeyRecord record = null;
                try
                {
                    record = await store.LoadAsync(TestDcId, cts.Token).ConfigureAwait(false);
                    if (record == null)
                        return null;

                    if (!IsValidRecord(record))
                    {
                        LiveSmokeTestSupport.Diag(
                            owner + " persisted auth key malformed; deleting smoke cache");
                        await store.DeleteAsync(TestDcId, cts.Token).ConfigureAwait(false);
                        return null;
                    }

                    return new LiveAuthKeyMaterial
                    {
                        Host = TestDcHost,
                        Port = TestDcPort,
                        AuthKeyBytes = CloneBytes(record.AuthKey),
                        AuthKeyId = record.AuthKeyId,
                        InitialServerSalt = record.ServerSalt,
                        ServerTimeOffset = record.ServerTimeOffset,
                        Source = "persisted"
                    };
                }
                catch (OperationCanceledException)
                {
                    if (outerToken.IsCancellationRequested)
                        throw;
                    LiveSmokeTestSupport.Diag(owner + " persisted auth key load timed out; using cold path");
                    return null;
                }
                catch (Exception ex)
                {
                    LiveSmokeTestSupport.Diag(owner + " persisted auth key load skipped: " +
                        ex.GetType().Name + ": " + ex.Message);
                    return null;
                }
                finally
                {
                    if (record != null && record.AuthKey != null)
                        Array.Clear(record.AuthKey, 0, record.AuthKey.Length);
                }
            }
        }

        private static async Task TrySavePersistedAsync(
            LiveAuthKeyMaterial material,
            string owner,
            CancellationToken ct)
        {
            BridgeAuthKeyStore store = TryCreatePersistentStore(owner);
            if (store == null)
                return;

            AccountAuthKeyRecord record = null;
            try
            {
                record = new AccountAuthKeyRecord
                {
                    AuthKey = CloneBytes(material.AuthKeyBytes),
                    AuthKeyId = material.AuthKeyId,
                    ServerSalt = material.InitialServerSalt,
                    ServerTimeOffset = material.ServerTimeOffset
                };
                await store.SaveAsync(TestDcId, record, ct).ConfigureAwait(false);
                LiveSmokeTestSupport.Diag(owner + " persisted auth_key_id=0x" +
                    material.AuthKeyId.ToString("x16", CultureInfo.InvariantCulture));
            }
            catch (Exception ex)
            {
                LiveSmokeTestSupport.Diag(owner + " persisted auth key save skipped: " +
                    ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (record != null && record.AuthKey != null)
                    Array.Clear(record.AuthKey, 0, record.AuthKey.Length);
            }
        }

        private static BridgeAuthKeyStore TryCreatePersistentStore(string owner)
        {
            try
            {
                var db = SqliteDatabase.Acquire();
                var protector = new PlatformDataProtector();
                var storageStore = new JsonAuthKeyStore(
                    new SqliteObjectStore<AuthKeyStoreState>(
                        db, "smoke_live_auth_keys", true, protector));
                return new BridgeAuthKeyStore(storageStore);
            }
            catch (Exception ex)
            {
                LiveSmokeTestSupport.Diag(owner + " persistent auth key store unavailable: " +
                    ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static bool IsValidRecord(AccountAuthKeyRecord record)
        {
            if (record == null) return false;
            if (record.AuthKey == null || record.AuthKey.Length != 256) return false;
            if (record.AuthKeyId == 0) return false;
            return true;
        }

        private static byte[] CloneBytes(byte[] source)
        {
            if (source == null)
                return null;

            byte[] copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
