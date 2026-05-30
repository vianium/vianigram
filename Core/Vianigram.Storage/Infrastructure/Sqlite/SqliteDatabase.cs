// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SqliteDatabase — process-wide handle to the single shared vianigram.db
// file under ApplicationData.LocalFolder. All SqliteObjectStore instances
// share this connection; writes are serialized through a private monitor so
// the C API's per-handle thread restriction (FULLMUTEX notwithstanding) is
// never the bottleneck and we get clean transaction boundaries.

using System;
using System.IO;
using System.Threading;
using Windows.Storage;

namespace Vianigram.Storage.Infrastructure.Sqlite
{
    /// <summary>
    /// Process-wide SQLite handle wrapper. Lazy-opened on first
    /// <see cref="Acquire"/> call; survives the lifetime of the appx process.
    /// </summary>
    public sealed class SqliteDatabase
    {
        private const string DbFileName = "vianigram.db";

        private static readonly object _staticGate = new object();
        private static SqliteDatabase _shared;

        private readonly string _path;
        private readonly object _gate = new object();
        private IntPtr _handle;
        private bool _initialized;

        private SqliteDatabase(string path)
        {
            _path = path;
        }

        /// <summary>Path to the shared database file.</summary>
        public string Path { get { return _path; } }

        /// <summary>Native sqlite3* handle. Zero until <see cref="Open"/>.</summary>
        public IntPtr Handle { get { return _handle; } }

        /// <summary>Lock object guarding all sqlite3_step calls on this handle.</summary>
        public object Gate { get { return _gate; } }

        /// <summary>
        /// Returns the singleton instance, opening the database lazily on the
        /// first call. Subsequent callers share the same handle.
        /// </summary>
        public static SqliteDatabase Acquire()
        {
            lock (_staticGate)
            {
                if (_shared == null)
                {
                    string folder = ApplicationData.Current.LocalFolder.Path;
                    string path = System.IO.Path.Combine(folder, DbFileName);
                    _shared = new SqliteDatabase(path);
                }
                _shared.Open();
                return _shared;
            }
        }

        /// <summary>
        /// Opens the database and runs the schema bootstrap once per process.
        /// Throws <see cref="SqliteException"/> on failure so callers can fall
        /// back to the JSON path without crashing the host.
        /// </summary>
        public void Open()
        {
            lock (_gate)
            {
                if (_initialized) return;

                IntPtr db;
                int flags = Sqlite3Native.SQLITE_OPEN_READWRITE
                          | Sqlite3Native.SQLITE_OPEN_CREATE
                          | Sqlite3Native.SQLITE_OPEN_FULLMUTEX;
                int rc = Sqlite3Native.sqlite3_open_v2(_path, out db, flags, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    string msg = "sqlite3_open_v2 rc=" + rc + " path=" + _path;
                    if (db != IntPtr.Zero) Sqlite3Native.sqlite3_close_v2(db);
                    throw new SqliteException(msg, rc);
                }

                _handle = db;

                // Pragmas + schema. WAL keeps writers from blocking readers and
                // gives crash-consistent point-in-time recovery without us
                // needing a separate journal file.
                Exec("PRAGMA journal_mode=WAL");
                Exec("PRAGMA synchronous=NORMAL");
                Exec("PRAGMA foreign_keys=ON");
                Exec(
                    "CREATE TABLE IF NOT EXISTS kv (" +
                    "  scope TEXT NOT NULL," +
                    "  key   TEXT NOT NULL," +
                    "  value BLOB NOT NULL," +
                    "  ts    INTEGER NOT NULL," +
                    "  PRIMARY KEY(scope, key)" +
                    ")");
                Exec(
                    "CREATE INDEX IF NOT EXISTS ix_kv_scope_ts " +
                    "  ON kv(scope, ts DESC)");

                // Avatar JPEG/PNG cache. Each row holds the bytes of a
                // single peer-photo (160x160 small face), keyed by
                // Telegram's photo_id. dc_id is recorded so a future
                // cross-DC migration can re-validate the source; format
                // distinguishes JPEG vs PNG so the decoder is fed the
                // right magic; byte_len + cached_at drive LRU pruning.
                Exec(
                    "CREATE TABLE IF NOT EXISTS avatar_cache (" +
                    "  photo_id  INTEGER NOT NULL PRIMARY KEY," +
                    "  dc_id     INTEGER NOT NULL," +
                    "  bytes     BLOB    NOT NULL," +
                    "  format    TEXT    NOT NULL," +
                    "  cached_at INTEGER NOT NULL," +
                    "  byte_len  INTEGER NOT NULL" +
                    ")");
                Exec(
                    "CREATE INDEX IF NOT EXISTS idx_avatar_cache_cached_at " +
                    "  ON avatar_cache(cached_at)");

                // imported_authorization_cache — see
                // SqliteImportedAuthorizationCacheStore. Holds the
                // 128-byte blob returned by auth.exportAuthorization,
                // keyed by (user_id, target_dc_id). home_dc_id is
                // recorded so the consumer can invalidate when the
                // user's home DC changes (post-migrate). cached_at is
                // here only for diagnostics — these blobs do not
                // expire on a server-side timer; they remain valid
                // until the user explicitly revokes the session.
                Exec(
                    "CREATE TABLE IF NOT EXISTS imported_authorization_cache (" +
                    "  user_id      INTEGER NOT NULL," +
                    "  target_dc_id INTEGER NOT NULL," +
                    "  auth_blob    BLOB    NOT NULL," +
                    "  home_dc_id   INTEGER NOT NULL," +
                    "  cached_at    INTEGER NOT NULL," +
                    "  PRIMARY KEY (user_id, target_dc_id)" +
                    ")");

                _initialized = true;
            }
        }

        /// <summary>
        /// Best-effort close. Called from app suspending paths; the singleton
        /// is reusable after this — the next Acquire reopens.
        /// </summary>
        public void Close()
        {
            lock (_gate)
            {
                if (_handle != IntPtr.Zero)
                {
                    Sqlite3Native.sqlite3_close_v2(_handle);
                    _handle = IntPtr.Zero;
                }
                _initialized = false;
            }
        }

        /// <summary>
        /// Executes a non-result SQL statement. Throws on non-OK return code.
        /// Caller is responsible for owning the gate; this method takes it
        /// only when invoked from outside an active transaction.
        /// </summary>
        public void Exec(string sql)
        {
            if (string.IsNullOrEmpty(sql)) return;
            byte[] sqlZ = Sqlite3Native.Utf8Z(sql);
            int rc = Sqlite3Native.sqlite3_exec(_handle, sqlZ, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("sqlite3_exec failed: " + sql + " — " + LastError(), rc);
            }
        }

        /// <summary>Reads sqlite3_errmsg from the current connection.</summary>
        public string LastError()
        {
            if (_handle == IntPtr.Zero) return "(handle closed)";
            return Sqlite3Native.PtrToStringUtf8(Sqlite3Native.sqlite3_errmsg(_handle)) ?? "(no message)";
        }
    }

    /// <summary>Thrown when a sqlite3 C API call returns a non-OK code.</summary>
    public sealed class SqliteException : Exception
    {
        public int ResultCode { get; private set; }

        public SqliteException(string message, int rc)
            : base(message)
        {
            ResultCode = rc;
        }
    }
}
