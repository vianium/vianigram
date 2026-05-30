// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Sqlite
{
    /// <summary>
    /// SQLite-backed <see cref="IImportedAuthorizationCacheStore"/>.
    /// Persists the 128-byte authorization blob handed back by
    /// <c>auth.exportAuthorization</c> so a subsequent login can skip
    /// the expensive cross-DC export and feed the blob directly into
    /// <c>auth.importAuthorization</c> on the media DC. Layout follows
    /// the same conventions as <see cref="SqliteAvatarCacheStore"/>:
    /// shared <see cref="SqliteDatabase.Gate"/>, best-effort error
    /// handling, prepared statements with explicit finalisation.
    /// </summary>
    public sealed class SqliteImportedAuthorizationCacheStore : IImportedAuthorizationCacheStore
    {
        // Telegram treats the auth.exportAuthorization blob as
        // effectively one-time-use: after the first auth.importAuthorization
        // consumes it the server invalidates the bytes within a few
        // minutes (observed empirically — there's no public TTL doc).
        // Reusing a stale blob comes back as AUTH_BYTES_INVALID, which
        // costs us evict + re-export + re-import (~3.5 s per media DC,
        // worse than skipping the cache entirely).
        //
        // We keep the SQLite-backed table for the in-session retry case
        // (e.g. handshake half-fails and a follow-up call lands within
        // seconds) but expire rows older than this window on read. Past
        // the TTL the row is evicted and the caller falls back to the
        // live export+import path on the first try — strictly cheaper
        // than trying the stale blob.
        private const long MaxAgeSeconds = 60L;

        private readonly SqliteDatabase _db;

        public SqliteImportedAuthorizationCacheStore(SqliteDatabase db)
        {
            if (db == null) throw new ArgumentNullException("db");
            _db = db;
        }

        public Task<ImportedAuthorizationCacheRecord> TryLoadAsync(
            long userId, int targetDcId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (userId == 0L || targetDcId <= 0)
                return TaskFromResult<ImportedAuthorizationCacheRecord>(null);

            ImportedAuthorizationCacheRecord record = null;
            bool stale = false;
            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "SELECT auth_blob, home_dc_id, cached_at FROM imported_authorization_cache " +
                "WHERE user_id = ? AND target_dc_id = ? LIMIT 1");

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
                            "imported_authorization_cache prepare select: " + _db.LastError(), rc);
                    }
                    try
                    {
                        BindInt64(stmt, 1, userId);
                        BindInt64(stmt, 2, targetDcId);

                        int step = Sqlite3Native.sqlite3_step(stmt);
                        if (step == Sqlite3Native.SQLITE_ROW)
                        {
                            int len = Sqlite3Native.sqlite3_column_bytes(stmt, 0);
                            byte[] blob = null;
                            if (len > 0)
                            {
                                IntPtr ptr = Sqlite3Native.sqlite3_column_blob(stmt, 0);
                                if (ptr != IntPtr.Zero)
                                {
                                    blob = new byte[len];
                                    Marshal.Copy(ptr, blob, 0, len);
                                }
                            }
                            int homeDcId = (int)Sqlite3Native.sqlite3_column_int64(stmt, 1);
                            long cachedAtSec = Sqlite3Native.sqlite3_column_int64(stmt, 2);

                            // TTL gate. The blob is one-time-use in
                            // practice; we only trust it for in-session
                            // retries within a tight window. Past the
                            // window, the row is treated as a miss and
                            // marked for eviction below.
                            long nowSec = NowUnixSeconds();
                            long ageSec = nowSec - cachedAtSec;
                            if (cachedAtSec > 0 && ageSec > MaxAgeSeconds)
                            {
                                stale = true;
                                EarlyLog.Write("Storage",
                                    "imported_authorization_cache TTL expired userId=" +
                                    userId.ToString(CultureInfo.InvariantCulture) +
                                    " targetDc=" + targetDcId +
                                    " ageSec=" + ageSec);
                            }
                            else if (blob != null && blob.Length > 0)
                            {
                                record = new ImportedAuthorizationCacheRecord
                                {
                                    UserId = userId,
                                    TargetDcId = targetDcId,
                                    HomeDcId = homeDcId,
                                    AuthBlob = blob,
                                    CachedAt = FromUnixSeconds(cachedAtSec)
                                };
                            }
                        }
                        else if (step != Sqlite3Native.SQLITE_DONE)
                        {
                            throw new SqliteException(
                                "imported_authorization_cache step select: " + _db.LastError(), step);
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
                    "imported_authorization_cache TryLoadAsync failed userId=" +
                    userId.ToString(CultureInfo.InvariantCulture) +
                    " targetDc=" + targetDcId +
                    " " + ex.GetType().Name + ": " + ex.Message);
                record = null;
            }

            // Stale rows are evicted lazily so a future SaveAsync (which
            // INSERT OR REPLACEs on the same primary key) and any
            // subsequent TTL-fresh load stays in lock-step.
            if (stale)
            {
                try
                {
                    EvictForTargetAsync(userId, targetDcId, CancellationToken.None);
                }
                catch
                {
                    // Best-effort — a leftover stale row simply produces
                    // another TTL miss next time.
                }
            }

            return TaskFromResult(record);
        }

        public Task SaveAsync(
            long userId, int targetDcId, int homeDcId, byte[] authBlob, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (userId == 0L || targetDcId <= 0 || homeDcId <= 0) return CompletedTask;
            if (authBlob == null || authBlob.Length == 0) return CompletedTask;

            long nowUnixSec = NowUnixSeconds();
            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "INSERT OR REPLACE INTO imported_authorization_cache" +
                "(user_id, target_dc_id, auth_blob, home_dc_id, cached_at) " +
                "VALUES (?, ?, ?, ?, ?)");

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
                            "imported_authorization_cache prepare upsert: " + _db.LastError(), rc);
                    }
                    try
                    {
                        BindInt64(stmt, 1, userId);
                        BindInt64(stmt, 2, targetDcId);
                        BindBlob(stmt, 3, authBlob);
                        BindInt64(stmt, 4, homeDcId);
                        BindInt64(stmt, 5, nowUnixSec);

                        rc = Sqlite3Native.sqlite3_step(stmt);
                        if (rc != Sqlite3Native.SQLITE_DONE)
                        {
                            throw new SqliteException(
                                "imported_authorization_cache step upsert: " + _db.LastError(), rc);
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
                // Best-effort: if we can't persist, the caller already
                // performed the live export+import; next login just pays
                // the same cost again.
                EarlyLog.Write("Storage",
                    "imported_authorization_cache SaveAsync failed userId=" +
                    userId.ToString(CultureInfo.InvariantCulture) +
                    " targetDc=" + targetDcId +
                    " " + ex.GetType().Name + ": " + ex.Message);
            }

            return CompletedTask;
        }

        public Task EvictAllForUserAsync(long userId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (userId == 0L) return CompletedTask;

            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "DELETE FROM imported_authorization_cache WHERE user_id = ?");

            return ExecDelete(sqlZ, userId, 0, "evict-user");
        }

        public Task EvictForTargetAsync(long userId, int targetDcId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (userId == 0L || targetDcId <= 0) return CompletedTask;

            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "DELETE FROM imported_authorization_cache WHERE user_id = ? AND target_dc_id = ?");

            return ExecDelete(sqlZ, userId, targetDcId, "evict-target");
        }

        private Task ExecDelete(byte[] sqlZ, long userId, int targetDcId, string opTag)
        {
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
                            "imported_authorization_cache prepare " + opTag + ": " + _db.LastError(), rc);
                    }
                    try
                    {
                        BindInt64(stmt, 1, userId);
                        if (targetDcId > 0)
                        {
                            BindInt64(stmt, 2, targetDcId);
                        }
                        rc = Sqlite3Native.sqlite3_step(stmt);
                        if (rc != Sqlite3Native.SQLITE_DONE)
                        {
                            throw new SqliteException(
                                "imported_authorization_cache step " + opTag + ": " + _db.LastError(), rc);
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
                    "imported_authorization_cache " + opTag + " failed userId=" +
                    userId.ToString(CultureInfo.InvariantCulture) +
                    (targetDcId > 0 ? " targetDc=" + targetDcId : "") +
                    " " + ex.GetType().Name + ": " + ex.Message);
            }

            return CompletedTask;
        }

        // ---- Bind helpers (mirror SqliteAvatarCacheStore) ------------------

        private static void BindBlob(IntPtr stmt, int idx, byte[] value)
        {
            int rc = Sqlite3Native.sqlite3_bind_blob(
                stmt, idx, value, value.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "imported_authorization_cache bind_blob idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindInt64(IntPtr stmt, int idx, long value)
        {
            int rc = Sqlite3Native.sqlite3_bind_int64(stmt, idx, value);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "imported_authorization_cache bind_int64 idx=" + idx + " rc=" + rc, rc);
            }
        }

        // ---- Time / task helpers -------------------------------------------

        private static long NowUnixSeconds()
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(DateTime.UtcNow - epoch).TotalSeconds;
        }

        private static DateTimeOffset FromUnixSeconds(long unixSec)
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return new DateTimeOffset(epoch.AddSeconds(unixSec));
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
