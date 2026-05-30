// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
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
    /// SQLite-backed <see cref="IEndpointHealthStore"/>. Persists one row
    /// per endpoint (keyed by <c>host:port</c>) in the shared
    /// <c>kv(scope, key, value, ts)</c> table under scope
    /// <c>endpoint_health_v1</c>. The payload is a compact binary blob —
    /// no encryption: nothing here is sensitive (it's reachability stats
    /// against public Telegram DCs that any observer already sees).
    ///
    /// On-disk layout v1 (all integers big-endian):
    ///   byte    version (1)
    ///   uint16  host_len
    ///   bytes   host_utf8
    ///   int32   port
    ///   int32   dc_id
    ///   byte    family (4 or 6)
    ///   int32   failures
    ///   int32   successes
    ///   int64   last_failure_unix_ms (0 = never)
    ///   int64   last_success_unix_ms (0 = never)
    ///   int64   cooldown_until_unix_ms (0 = none)
    ///   uint16  reason_len
    ///   bytes   reason_utf8
    /// </summary>
    public sealed class SqliteEndpointHealthStore : IEndpointHealthStore
    {
        private const string Scope = "endpoint_health_v1";
        private const byte FormatVersion = 1;

        private readonly SqliteDatabase _db;

        public SqliteEndpointHealthStore(SqliteDatabase db)
        {
            if (db == null) throw new ArgumentNullException("db");
            _db = db;
        }

        public Task<List<EndpointHealthRecord>> LoadAllAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            List<EndpointHealthRecord> records = new List<EndpointHealthRecord>();

            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "SELECT value FROM kv WHERE scope = ?");

            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(
                    _db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException(
                        "endpoint_health prepare select: " + _db.LastError(), rc);
                }

                try
                {
                    byte[] scopeBuf = Encoding.UTF8.GetBytes(Scope);
                    Sqlite3Native.sqlite3_bind_text(
                        stmt, 1, scopeBuf, scopeBuf.Length, Sqlite3Native.SQLITE_TRANSIENT);

                    while (true)
                    {
                        int step = Sqlite3Native.sqlite3_step(stmt);
                        if (step == Sqlite3Native.SQLITE_DONE) break;
                        if (step != Sqlite3Native.SQLITE_ROW)
                        {
                            throw new SqliteException(
                                "endpoint_health step: " + _db.LastError(), step);
                        }

                        int len = Sqlite3Native.sqlite3_column_bytes(stmt, 0);
                        if (len <= 0) continue;

                        IntPtr ptr = Sqlite3Native.sqlite3_column_blob(stmt, 0);
                        if (ptr == IntPtr.Zero) continue;

                        byte[] payload = new byte[len];
                        Marshal.Copy(ptr, payload, 0, len);

                        EndpointHealthRecord rec = TryDecode(payload);
                        if (rec != null)
                        {
                            records.Add(rec);
                        }
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }

            EarlyLog.Write(
                "Storage",
                "endpoint_health loaded count=" +
                records.Count.ToString(CultureInfo.InvariantCulture));
            return TaskFromResult(records);
        }

        public Task UpsertAsync(EndpointHealthRecord record, CancellationToken ct)
        {
            if (record == null) throw new ArgumentNullException("record");
            ct.ThrowIfCancellationRequested();

            byte[] payload = Encode(record);
            string key = record.Host + ":" +
                record.Port.ToString(CultureInfo.InvariantCulture);

            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "INSERT OR REPLACE INTO kv(scope, key, value, ts) VALUES (?, ?, ?, ?)");

            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(
                    _db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException(
                        "endpoint_health prepare upsert: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, Scope);
                    BindText(stmt, 2, key);
                    BindBlob(stmt, 3, payload);
                    BindInt64(stmt, 4, NowUnixMs());

                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException(
                            "endpoint_health step upsert: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }

            return CompletedTask;
        }

        public Task PruneOlderThanAsync(DateTime cutoffUtc, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            long cutoffMs = ToUnixMs(cutoffUtc);

            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "DELETE FROM kv WHERE scope = ? AND ts < ?");

            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(
                    _db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException(
                        "endpoint_health prepare prune: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, Scope);
                    BindInt64(stmt, 2, cutoffMs);

                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException(
                            "endpoint_health step prune: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }

            return CompletedTask;
        }

        // ---- Encoding ------------------------------------------------------

        private static byte[] Encode(EndpointHealthRecord r)
        {
            byte[] host = Encoding.UTF8.GetBytes(r.Host ?? string.Empty);
            byte[] reason = Encoding.UTF8.GetBytes(r.LastFailureReason ?? string.Empty);
            if (host.Length > ushort.MaxValue) throw new ArgumentException("host too long");
            if (reason.Length > ushort.MaxValue) reason = new byte[0];

            int size = 1
                + 2 + host.Length
                + 4
                + 4
                + 1
                + 4
                + 4
                + 8
                + 8
                + 8
                + 2 + reason.Length;

            byte[] buf = new byte[size];
            int o = 0;
            buf[o++] = FormatVersion;
            WriteU16BE(buf, ref o, (ushort)host.Length);
            Buffer.BlockCopy(host, 0, buf, o, host.Length); o += host.Length;
            WriteI32BE(buf, ref o, r.Port);
            WriteI32BE(buf, ref o, r.DcId);
            buf[o++] = (byte)r.Family;
            WriteI32BE(buf, ref o, r.Failures);
            WriteI32BE(buf, ref o, r.Successes);
            WriteI64BE(buf, ref o, ToUnixMs(r.LastFailureUtc));
            WriteI64BE(buf, ref o, ToUnixMs(r.LastSuccessUtc));
            WriteI64BE(buf, ref o, ToUnixMs(r.CooldownUntilUtc));
            WriteU16BE(buf, ref o, (ushort)reason.Length);
            Buffer.BlockCopy(reason, 0, buf, o, reason.Length); o += reason.Length;
            return buf;
        }

        private static EndpointHealthRecord TryDecode(byte[] buf)
        {
            if (buf == null || buf.Length < 1 + 2 + 4 + 4 + 1 + 4 + 4 + 8 + 8 + 8 + 2)
            {
                return null;
            }

            try
            {
                int o = 0;
                byte version = buf[o++];
                if (version != FormatVersion) return null;

                int hostLen = ReadU16BE(buf, ref o);
                if (hostLen <= 0 || o + hostLen > buf.Length) return null;
                string host = Encoding.UTF8.GetString(buf, o, hostLen);
                o += hostLen;

                int port = ReadI32BE(buf, ref o);
                int dcId = ReadI32BE(buf, ref o);
                int family = buf[o++];
                int failures = ReadI32BE(buf, ref o);
                int successes = ReadI32BE(buf, ref o);
                DateTime lastFail = FromUnixMs(ReadI64BE(buf, ref o));
                DateTime lastSucc = FromUnixMs(ReadI64BE(buf, ref o));
                DateTime cooldown = FromUnixMs(ReadI64BE(buf, ref o));
                int reasonLen = ReadU16BE(buf, ref o);
                string reason = string.Empty;
                if (reasonLen > 0 && o + reasonLen <= buf.Length)
                {
                    reason = Encoding.UTF8.GetString(buf, o, reasonLen);
                    o += reasonLen;
                }

                if (port <= 0 || port > 65535) return null;
                if (family != 4 && family != 6) return null;

                return new EndpointHealthRecord(
                    host, port, dcId, family,
                    failures, successes,
                    lastFail, lastSucc, cooldown, reason);
            }
            catch
            {
                return null;
            }
        }

        // ---- Binary IO helpers --------------------------------------------

        private static void WriteU16BE(byte[] b, ref int o, ushort v)
        {
            b[o++] = (byte)(v >> 8);
            b[o++] = (byte)(v & 0xFF);
        }

        private static int ReadU16BE(byte[] b, ref int o)
        {
            int v = (b[o] << 8) | b[o + 1];
            o += 2;
            return v;
        }

        private static void WriteI32BE(byte[] b, ref int o, int v)
        {
            b[o++] = (byte)((v >> 24) & 0xFF);
            b[o++] = (byte)((v >> 16) & 0xFF);
            b[o++] = (byte)((v >> 8) & 0xFF);
            b[o++] = (byte)(v & 0xFF);
        }

        private static int ReadI32BE(byte[] b, ref int o)
        {
            int v = (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
            o += 4;
            return v;
        }

        private static void WriteI64BE(byte[] b, ref int o, long v)
        {
            for (int i = 7; i >= 0; i--)
            {
                b[o++] = (byte)((v >> (i * 8)) & 0xFF);
            }
        }

        private static long ReadI64BE(byte[] b, ref int o)
        {
            long v = 0;
            for (int i = 0; i < 8; i++)
            {
                v = (v << 8) | b[o + i];
            }
            o += 8;
            return v;
        }

        private static long ToUnixMs(DateTime dt)
        {
            if (dt == DateTime.MinValue) return 0L;
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(dt.ToUniversalTime() - epoch).TotalMilliseconds;
        }

        private static DateTime FromUnixMs(long ms)
        {
            if (ms <= 0L) return DateTime.MinValue;
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return epoch.AddMilliseconds(ms);
        }

        private static long NowUnixMs()
        {
            return ToUnixMs(DateTime.UtcNow);
        }

        // ---- SqliteAuthKeyStore-style bind helpers -------------------------

        private static void BindText(IntPtr stmt, int idx, string value)
        {
            byte[] buf = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int rc = Sqlite3Native.sqlite3_bind_text(
                stmt, idx, buf, buf.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "endpoint_health bind_text idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindBlob(IntPtr stmt, int idx, byte[] value)
        {
            int rc = Sqlite3Native.sqlite3_bind_blob(
                stmt, idx, value, value.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "endpoint_health bind_blob idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindInt64(IntPtr stmt, int idx, long value)
        {
            int rc = Sqlite3Native.sqlite3_bind_int64(stmt, idx, value);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException(
                    "endpoint_health bind_int64 idx=" + idx + " rc=" + rc, rc);
            }
        }

        // ---- Task helpers (WP 8.1 has no Task.CompletedTask) ---------------

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
