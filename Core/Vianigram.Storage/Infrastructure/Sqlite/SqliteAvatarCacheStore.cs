// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Sqlite
{
    /// <summary>
    /// SQLite-backed <see cref="IAvatarCacheStore"/>. Persists raw JPEG/PNG
    /// avatar payloads in the dedicated <c>avatar_cache</c> table created
    /// by <see cref="SqliteDatabase"/> on first open. Concurrency is
    /// handled by the shared <see cref="SqliteDatabase.Gate"/> monitor —
    /// the chat list cold-start fan-out (16 parallel fetches) serialises
    /// against a single short-held lock, which is fine because each row
    /// read/write is &lt;5 ms.
    /// </summary>
    public sealed class SqliteAvatarCacheStore : IAvatarCacheStore
    {
        private readonly SqliteDatabase _db;

        public SqliteAvatarCacheStore(SqliteDatabase db)
        {
            if (db == null) throw new ArgumentNullException("db");
            _db = db;
        }

        public Task<byte[]> TryLoadAsync(long photoId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (photoId == 0L) return TaskFromResult<byte[]>(null);

            byte[] payload = null;
            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "SELECT bytes FROM avatar_cache WHERE photo_id = ? LIMIT 1");

            try
            {
                lock (_db.Gate)
                {
                    IntPtr stmt;
                    int rc = Sqlite3Native.sqlite3_prepare_v2(
                        _db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                    if (rc != Sqlite3Native.SQLITE_OK)
                    {
                        throw new SqliteException(
                            "avatar_cache prepare select: " + _db.LastError(), rc);
                    }
                    try
                    {
                        BindInt64(stmt, 1, photoId);

                        int step = Sqlite3Native.sqlite3_step(stmt);
                        if (step == Sqlite3Native.SQLITE_ROW)
                        {
                            int len = Sqlite3Native.sqlite3_column_bytes(stmt, 0);
                            if (len > 0)
                            {
                                IntPtr ptr = Sqlite3Native.sqlite3_column_blob(stmt, 0);
                                if (ptr != IntPtr.Zero)
                                {
                                    payload = new byte[len];
                                    Marshal.Copy(ptr, payload, 0, len);
                                }
                            }
                        }
                        else if (step != Sqlite3Native.SQLITE_DONE)
                        {
                            throw new SqliteException(
                                "avatar_cache step select: " + _db.LastError(), step);
                        }
                    }
                    finally
                    {
                        Sqlite3Native.sqlite3_finalize(stmt);
                    }
                }
            }
            catch (Exception ex)
            {
                // Cache is best-effort — never break the caller. Log and
                // fall back to a miss so the fetcher hits the network.
                EarlyLog.Write("Storage",
                    "avatar_cache TryLoadAsync failed photoId=" +
                    photoId.ToString(CultureInfo.InvariantCulture) +
                    " " + ex.GetType().Name + ": " + ex.Message);
                payload = null;
            }

            return TaskFromResult(payload);
        }

        public Task SaveAsync(long photoId, int dcId, byte[] bytes, string format, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (photoId == 0L) return CompletedTask;
            if (bytes == null || bytes.Length == 0) return CompletedTask;
            string fmt = string.IsNullOrEmpty(format) ? "jpeg" : format;
            long nowUnixSec = NowUnixSeconds();

            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "INSERT OR REPLACE INTO avatar_cache(photo_id, dc_id, bytes, format, cached_at, byte_len) " +
                "VALUES (?, ?, ?, ?, ?, ?)");

            try
            {
                lock (_db.Gate)
                {
                    IntPtr stmt;
                    int rc = Sqlite3Native.sqlite3_prepare_v2(
                        _db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                    if (rc != Sqlite3Native.SQLITE_OK)
                    {
                        throw new SqliteException(
                            "avatar_cache prepare upsert: " + _db.LastError(), rc);
                    }
                    try
                    {
                        BindInt64(stmt, 1, photoId);
                        BindInt64(stmt, 2, dcId);
                        BindBlob(stmt, 3, bytes);
                        BindText(stmt, 4, fmt);
                        BindInt64(stmt, 5, nowUnixSec);
                        BindInt64(stmt, 6, bytes.Length);

                        rc = Sqlite3Native.sqlite3_step(stmt);
                        if (rc != Sqlite3Native.SQLITE_DONE)
                        {
                            throw new SqliteException(
                                "avatar_cache step upsert: " + _db.LastError(), rc);
                        }
                    }
                    finally
                    {
                        Sqlite3Native.sqlite3_finalize(stmt);
                    }
                }
            }
            catch (Exception ex)
            {
                // Swallow — the avatar already loaded from the network;
                // failing to persist it just costs us a re-fetch next
                // launch.
                EarlyLog.Write("Storage",
                    "avatar_cache SaveAsync failed photoId=" +
                    photoId.ToString(CultureInfo.InvariantCulture) +
                    " " + ex.GetType().Name + ": " + ex.Message);
            }

            return CompletedTask;
        }

        public Task EvictOlderThanAsync(DateTimeOffset cutoff, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            // DateTimeOffset.ToUnixTimeSeconds() ships in .NET 4.6+ — this
            // assembly targets WP 8.1 (.NET 4.5.1), so compute the epoch
            // delta by hand. Matches SqliteEndpointHealthStore's helper.
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long cutoffUnixSec = (long)(cutoff.UtcDateTime - epoch).TotalSeconds;

            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "DELETE FROM avatar_cache WHERE cached_at < ?");

            try
            {
                lock (_db.Gate)
                {
                    IntPtr stmt;
                    int rc = Sqlite3Native.sqlite3_prepare_v2(
                        _db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                    if (rc != Sqlite3Native.SQLITE_OK)
                    {
                        throw new SqliteException(
                            "avatar_cache prepare evict: " + _db.LastError(), rc);
                    }
                    try
                    {
                        BindInt64(stmt, 1, cutoffUnixSec);
                        rc = Sqlite3Native.sqlite3_step(stmt);
                        if (rc != Sqlite3Native.SQLITE_DONE)
                        {
                            throw new SqliteException(
                                "avatar_cache step evict: " + _db.LastError(), rc);
                        }
                    }
                    finally
                    {
                        Sqlite3Native.sqlite3_finalize(stmt);
                    }
                }
            }
            catch (Exception ex)
            {
                EarlyLog.Write("Storage",
                    "avatar_cache EvictOlderThanAsync failed cutoffSec=" +
                    cutoffUnixSec.ToString(CultureInfo.InvariantCulture) +
                    " " + ex.GetType().Name + ": " + ex.Message);
            }

            return CompletedTask;
        }

        // ---- Bind helpers (mirror SqliteEndpointHealthStore conventions) --

        private static void BindText(IntPtr stmt, int idx, string value)
        {
            byte[] buf = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int rc = Sqlite3Native.sqlite3_bind_text(
                stmt, idx, buf, buf.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "avatar_cache bind_text idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindBlob(IntPtr stmt, int idx, byte[] value)
        {
            int rc = Sqlite3Native.sqlite3_bind_blob(
                stmt, idx, value, value.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "avatar_cache bind_blob idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindInt64(IntPtr stmt, int idx, long value)
        {
            int rc = Sqlite3Native.sqlite3_bind_int64(stmt, idx, value);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "avatar_cache bind_int64 idx=" + idx + " rc=" + rc, rc);
            }
        }

        // ---- Time / task helpers ------------------------------------------

        private static long NowUnixSeconds()
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(DateTime.UtcNow - epoch).TotalSeconds;
        }

        private static readonly Task _completedTask = TaskFromResultPrivate(0);
        private static Task CompletedTask { get { return _completedTask; } }

        private static Task<T> TaskFromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }

        private static Task TaskFromResultPrivate(int _)
        {
            var tcs = new TaskCompletionSource<int>();
            tcs.SetResult(0);
            return tcs.Task;
        }
    }
}
