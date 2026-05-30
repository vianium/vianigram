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
    /// SQLite-backed <see cref="IDcOptionsStore"/>. One row per dc/host/port
    /// triple in the shared kv table under scope <c>dc_options_v1</c>.
    /// Key format is <c>dc:host:port[:6]</c> so the same host can have
    /// distinct rows for IPv4 / IPv6 / different ports.
    ///
    /// Payload binary layout v1 (all integers big-endian):
    ///   byte    version (1)
    ///   int32   dc_id
    ///   uint16  host_len
    ///   bytes   host_utf8
    ///   int32   port
    ///   byte    flags  (bit0=ipv6, bit1=media_only, bit2=tcpo_only,
    ///                   bit3=cdn,  bit4=static,     bit5=this_port_only,
    ///                   bit6=has_secret)
    ///   uint16  secret_len      (only if bit6 set)
    ///   bytes   secret          (only if bit6 set)
    ///   int64   fetched_at_unix_ms
    /// </summary>
    public sealed class SqliteDcOptionsStore : IDcOptionsStore
    {
        private const string Scope = "dc_options_v1";
        private const byte FormatVersion = 1;

        private readonly SqliteDatabase _db;

        public SqliteDcOptionsStore(SqliteDatabase db)
        {
            if (db == null) throw new ArgumentNullException("db");
            _db = db;
        }

        public Task<List<DcOptionRecord>> LoadAllAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            List<DcOptionRecord> records = new List<DcOptionRecord>();

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
                        "dc_options prepare select: " + _db.LastError(), rc);
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
                                "dc_options step: " + _db.LastError(), step);
                        }

                        int len = Sqlite3Native.sqlite3_column_bytes(stmt, 0);
                        if (len <= 0) continue;

                        IntPtr ptr = Sqlite3Native.sqlite3_column_blob(stmt, 0);
                        if (ptr == IntPtr.Zero) continue;

                        byte[] payload = new byte[len];
                        Marshal.Copy(ptr, payload, 0, len);

                        DcOptionRecord rec = TryDecode(payload);
                        if (rec != null) records.Add(rec);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }

            EarlyLog.Write(
                "Storage",
                "dc_options loaded count=" +
                records.Count.ToString(CultureInfo.InvariantCulture));
            return TaskFromResult(records);
        }

        public Task ReplaceAllAsync(IReadOnlyList<DcOptionRecord> records, CancellationToken ct)
        {
            if (records == null) throw new ArgumentNullException("records");
            ct.ThrowIfCancellationRequested();

            lock (_db.Gate)
            {
                ExecRaw("BEGIN");
                try
                {
                    // 1) wipe
                    using (var del = Prepare("DELETE FROM kv WHERE scope = ?"))
                    {
                        BindText(del.Stmt, 1, Scope);
                        StepDone(del);
                    }

                    // 2) insert each
                    foreach (DcOptionRecord r in records)
                    {
                        if (r == null) continue;
                        byte[] payload = Encode(r);
                        string key = BuildKey(r);

                        using (var ins = Prepare(
                            "INSERT OR REPLACE INTO kv(scope, key, value, ts) VALUES (?, ?, ?, ?)"))
                        {
                            BindText(ins.Stmt, 1, Scope);
                            BindText(ins.Stmt, 2, key);
                            BindBlob(ins.Stmt, 3, payload);
                            BindInt64(ins.Stmt, 4, NowUnixMs());
                            StepDone(ins);
                        }
                    }

                    ExecRaw("COMMIT");
                }
                catch
                {
                    try { ExecRaw("ROLLBACK"); } catch { }
                    throw;
                }
            }

            EarlyLog.Write(
                "Storage",
                "dc_options replaced count=" +
                records.Count.ToString(CultureInfo.InvariantCulture));
            return CompletedTask;
        }

        public Task ClearAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[] sqlZ = Sqlite3Native.Utf8Z("DELETE FROM kv WHERE scope = ?");

            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(
                    _db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException(
                        "dc_options prepare clear: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, Scope);
                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException(
                            "dc_options step clear: " + _db.LastError(), rc);
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

        private static string BuildKey(DcOptionRecord r)
        {
            return r.DcId.ToString(CultureInfo.InvariantCulture) +
                ":" + r.Host +
                ":" + r.Port.ToString(CultureInfo.InvariantCulture) +
                (r.Ipv6 ? ":6" : ":4");
        }

        private static byte[] Encode(DcOptionRecord r)
        {
            byte[] host = Encoding.UTF8.GetBytes(r.Host ?? string.Empty);
            byte[] secret = r.Secret;
            bool hasSecret = secret != null && secret.Length > 0;

            byte flags = 0;
            if (r.Ipv6)          flags |= 0x01;
            if (r.MediaOnly)     flags |= 0x02;
            if (r.TcpoOnly)      flags |= 0x04;
            if (r.Cdn)           flags |= 0x08;
            if (r.StaticFlag)    flags |= 0x10;
            if (r.ThisPortOnly)  flags |= 0x20;
            if (hasSecret)       flags |= 0x40;

            int size = 1 + 4 + 2 + host.Length + 4 + 1;
            if (hasSecret) size += 2 + secret.Length;
            size += 8;

            byte[] buf = new byte[size];
            int o = 0;
            buf[o++] = FormatVersion;
            WriteI32BE(buf, ref o, r.DcId);
            WriteU16BE(buf, ref o, (ushort)host.Length);
            Buffer.BlockCopy(host, 0, buf, o, host.Length); o += host.Length;
            WriteI32BE(buf, ref o, r.Port);
            buf[o++] = flags;
            if (hasSecret)
            {
                WriteU16BE(buf, ref o, (ushort)secret.Length);
                Buffer.BlockCopy(secret, 0, buf, o, secret.Length);
                o += secret.Length;
            }
            WriteI64BE(buf, ref o, ToUnixMs(r.FetchedAt));
            return buf;
        }

        private static DcOptionRecord TryDecode(byte[] buf)
        {
            if (buf == null || buf.Length < 1 + 4 + 2 + 4 + 1 + 8) return null;

            try
            {
                int o = 0;
                if (buf[o++] != FormatVersion) return null;
                int dcId = ReadI32BE(buf, ref o);
                int hostLen = ReadU16BE(buf, ref o);
                if (hostLen <= 0 || o + hostLen > buf.Length) return null;
                string host = Encoding.UTF8.GetString(buf, o, hostLen); o += hostLen;
                int port = ReadI32BE(buf, ref o);
                byte flags = buf[o++];

                byte[] secret = null;
                if ((flags & 0x40) != 0)
                {
                    int sLen = ReadU16BE(buf, ref o);
                    if (sLen > 0 && o + sLen <= buf.Length)
                    {
                        secret = new byte[sLen];
                        Buffer.BlockCopy(buf, o, secret, 0, sLen);
                        o += sLen;
                    }
                }
                DateTime fetched = FromUnixMs(ReadI64BE(buf, ref o));

                return new DcOptionRecord(
                    dcId, host, port,
                    (flags & 0x01) != 0,
                    (flags & 0x02) != 0,
                    (flags & 0x04) != 0,
                    (flags & 0x08) != 0,
                    (flags & 0x10) != 0,
                    (flags & 0x20) != 0,
                    secret,
                    fetched);
            }
            catch
            {
                return null;
            }
        }

        // ---- Binary IO helpers --------------------------------------------

        private static void WriteU16BE(byte[] b, ref int o, ushort v) { b[o++] = (byte)(v >> 8); b[o++] = (byte)(v & 0xFF); }
        private static int ReadU16BE(byte[] b, ref int o) { int v = (b[o] << 8) | b[o + 1]; o += 2; return v; }
        private static void WriteI32BE(byte[] b, ref int o, int v) { b[o++] = (byte)((v >> 24) & 0xFF); b[o++] = (byte)((v >> 16) & 0xFF); b[o++] = (byte)((v >> 8) & 0xFF); b[o++] = (byte)(v & 0xFF); }
        private static int ReadI32BE(byte[] b, ref int o) { int v = (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3]; o += 4; return v; }
        private static void WriteI64BE(byte[] b, ref int o, long v) { for (int i = 7; i >= 0; i--) { b[o++] = (byte)((v >> (i * 8)) & 0xFF); } }
        private static long ReadI64BE(byte[] b, ref int o) { long v = 0; for (int i = 0; i < 8; i++) { v = (v << 8) | b[o + i]; } o += 8; return v; }

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

        private static long NowUnixMs() { return ToUnixMs(DateTime.UtcNow); }

        // ---- SQL plumbing --------------------------------------------------

        private sealed class StmtScope : IDisposable
        {
            public IntPtr Stmt;
            public void Dispose() { if (Stmt != IntPtr.Zero) Sqlite3Native.sqlite3_finalize(Stmt); }
        }

        private StmtScope Prepare(string sql)
        {
            byte[] sqlZ = Sqlite3Native.Utf8Z(sql);
            IntPtr stmt;
            int rc = Sqlite3Native.sqlite3_prepare_v2(_db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("dc_options prepare: " + _db.LastError(), rc);
            }
            return new StmtScope { Stmt = stmt };
        }

        private void StepDone(StmtScope s)
        {
            int rc = Sqlite3Native.sqlite3_step(s.Stmt);
            if (rc != Sqlite3Native.SQLITE_DONE)
            {
                throw new SqliteException("dc_options step: " + _db.LastError(), rc);
            }
        }

        private void ExecRaw(string sql)
        {
            using (var s = Prepare(sql)) { StepDone(s); }
        }

        private static void BindText(IntPtr stmt, int idx, string value)
        {
            byte[] buf = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int rc = Sqlite3Native.sqlite3_bind_text(stmt, idx, buf, buf.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("dc_options bind_text idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindBlob(IntPtr stmt, int idx, byte[] value)
        {
            int rc = Sqlite3Native.sqlite3_bind_blob(stmt, idx, value, value.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("dc_options bind_blob idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindInt64(IntPtr stmt, int idx, long value)
        {
            int rc = Sqlite3Native.sqlite3_bind_int64(stmt, idx, value);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("dc_options bind_int64 idx=" + idx + " rc=" + rc, rc);
            }
        }

        // ---- Task helpers --------------------------------------------------

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
