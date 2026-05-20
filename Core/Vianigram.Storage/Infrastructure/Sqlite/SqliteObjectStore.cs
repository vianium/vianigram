// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SqliteObjectStore<T> — IObjectStore implementation that persists each
// aggregate as a single (scope, key="state") row in the shared kv table.
// JSON serialization is preserved (DataContractJsonSerializer) so existing
// repos and on-disk tests continue to work; only the storage backend swaps.

using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Storage.Application;

namespace Vianigram.Storage.Infrastructure.Sqlite
{
    /// <summary>
    /// SQLite-backed <see cref="IObjectStore{T}"/>. Each aggregate occupies a
    /// single row keyed by <c>(scope, "state")</c>. Optional at-rest
    /// encryption mirrors <see cref="JsonObjectStore{T}"/>: JSON bytes are
    /// wrapped by <see cref="IDataProtector.ProtectAsync"/> before being
    /// stored as a BLOB.
    /// </summary>
    /// <remarks>
    /// Concurrency: serialized writes via the shared <see cref="SqliteDatabase.Gate"/>.
    /// Atomicity: every write runs inside an implicit transaction (BEGIN
    /// IMMEDIATE / COMMIT) so partial writes are impossible.
    /// </remarks>
    public sealed class SqliteObjectStore<T> : IObjectStore<T> where T : class, new()
    {
        private const string RowKey = "state";

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private readonly SqliteDatabase _db;
        private readonly string _scope;
        private readonly bool _encrypted;
        private readonly IDataProtector _protector;
        private readonly object _serializerGate = new object();
        private DataContractJsonSerializer _serializer;

        public SqliteObjectStore(SqliteDatabase db, string scope, bool encrypted, IDataProtector protector)
        {
            if (db == null) throw new ArgumentNullException("db");
            if (string.IsNullOrEmpty(scope)) throw new ArgumentException("scope required", "scope");
            if (encrypted && protector == null) throw new ArgumentNullException("protector", "encrypted store requires a protector");

            _db = db;
            _scope = scope;
            _encrypted = encrypted;
            _protector = protector;
        }

        public async Task<T> LoadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            byte[] raw = ReadRow();
            if (raw == null || raw.Length == 0)
            {
                return new T();
            }

            byte[] jsonBytes;
            if (_encrypted)
            {
                jsonBytes = await _protector.UnprotectAsync(raw, ct).ConfigureAwait(false);
            }
            else
            {
                jsonBytes = raw;
            }

            using (var ms = new MemoryStream(jsonBytes, false))
            {
                object obj = Serializer.ReadObject(ms);
                T value = obj as T;
                return value != null ? value : new T();
            }
        }

        public async Task SaveAsync(T value, CancellationToken ct)
        {
            if (value == null) throw new ArgumentNullException("value");
            ct.ThrowIfCancellationRequested();

            byte[] jsonBytes;
            using (var ms = new MemoryStream())
            {
                Serializer.WriteObject(ms, value);
                jsonBytes = ms.ToArray();
            }

            byte[] payload;
            if (_encrypted)
            {
                payload = await _protector.ProtectAsync(jsonBytes, ct).ConfigureAwait(false);
            }
            else
            {
                payload = jsonBytes;
            }

            WriteRow(payload);
        }

        public Task DeleteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            DeleteRow();
            return CompletedTask();
        }

        // -- I/O --------------------------------------------------------------

        private DataContractJsonSerializer Serializer
        {
            get
            {
                if (_serializer != null) return _serializer;
                lock (_serializerGate)
                {
                    if (_serializer == null)
                    {
                        _serializer = new DataContractJsonSerializer(typeof(T));
                    }
                    return _serializer;
                }
            }
        }

        private byte[] ReadRow()
        {
            byte[] result = null;
            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "SELECT value FROM kv WHERE scope = ? AND key = ? LIMIT 1");

            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(_db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException("prepare select: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, _scope);
                    BindText(stmt, 2, RowKey);

                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc == Sqlite3Native.SQLITE_ROW)
                    {
                        int n = Sqlite3Native.sqlite3_column_bytes(stmt, 0);
                        IntPtr p = Sqlite3Native.sqlite3_column_blob(stmt, 0);
                        if (n > 0 && p != IntPtr.Zero)
                        {
                            result = new byte[n];
                            System.Runtime.InteropServices.Marshal.Copy(p, result, 0, n);
                        }
                        else
                        {
                            result = new byte[0];
                        }
                    }
                    else if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException("step select: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }

            return result;
        }

        private void WriteRow(byte[] payload)
        {
            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "INSERT OR REPLACE INTO kv(scope, key, value, ts) VALUES (?, ?, ?, ?)");

            lock (_db.Gate)
            {
                _db.Exec("BEGIN IMMEDIATE");
                bool committed = false;
                try
                {
                    IntPtr stmt;
                    int rc = Sqlite3Native.sqlite3_prepare_v2(_db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                    if (rc != Sqlite3Native.SQLITE_OK)
                    {
                        throw new SqliteException("prepare upsert: " + _db.LastError(), rc);
                    }
                    try
                    {
                        BindText(stmt, 1, _scope);
                        BindText(stmt, 2, RowKey);
                        BindBlob(stmt, 3, payload ?? new byte[0]);
                        BindInt64(stmt, 4, (long)(DateTime.UtcNow - UnixEpoch).TotalMilliseconds);

                        rc = Sqlite3Native.sqlite3_step(stmt);
                        if (rc != Sqlite3Native.SQLITE_DONE)
                        {
                            throw new SqliteException("step upsert: " + _db.LastError(), rc);
                        }
                    }
                    finally
                    {
                        Sqlite3Native.sqlite3_finalize(stmt);
                    }

                    _db.Exec("COMMIT");
                    committed = true;
                }
                finally
                {
                    if (!committed)
                    {
                        try { _db.Exec("ROLLBACK"); } catch { /* swallow */ }
                    }
                }
            }
        }

        private void DeleteRow()
        {
            byte[] sqlZ = Sqlite3Native.Utf8Z("DELETE FROM kv WHERE scope = ? AND key = ?");

            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(_db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException("prepare delete: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, _scope);
                    BindText(stmt, 2, RowKey);

                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException("step delete: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }
        }

        // -- Bind helpers -----------------------------------------------------

        private static void BindText(IntPtr stmt, int idx, string value)
        {
            byte[] buf = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            int rc = Sqlite3Native.sqlite3_bind_text(stmt, idx, buf, buf.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("bind_text idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindBlob(IntPtr stmt, int idx, byte[] value)
        {
            int rc = Sqlite3Native.sqlite3_bind_blob(stmt, idx, value, value.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("bind_blob idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindInt64(IntPtr stmt, int idx, long value)
        {
            int rc = Sqlite3Native.sqlite3_bind_int64(stmt, idx, value);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("bind_int64 idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static Task CompletedTask()
        {
            // WP8.1 .NET Core lacks Task.CompletedTask.
            var tcs = new TaskCompletionSource<int>();
            tcs.SetResult(0);
            return tcs.Task;
        }
    }
}
