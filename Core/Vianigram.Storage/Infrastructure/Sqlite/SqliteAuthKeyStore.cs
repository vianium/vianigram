// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Storage.Application;
using Vianigram.Storage.Ports.Stubs;

namespace Vianigram.Storage.Infrastructure.Sqlite
{
    /// <summary>
    /// Compact SQLite auth-key store. Unlike JsonAuthKeyStore, it stores one
    /// encrypted binary row per DC, so cold boot can load the home auth_key
    /// without waking DataContractJsonSerializer.
    /// </summary>
    public sealed class SqliteAuthKeyStore : IAuthKeyStore
    {
        private const string Scope = "auth_keys_v2";
        private const int FormatVersion = 2;

        private readonly SqliteDatabase _db;
        private readonly IDataProtector _protector;
        private readonly IAuthKeyStore _legacy;

        public SqliteAuthKeyStore(SqliteDatabase db, IDataProtector protector, IAuthKeyStore legacy)
        {
            if (db == null) throw new ArgumentNullException("db");
            if (protector == null) throw new ArgumentNullException("protector");

            _db = db;
            _protector = protector;
            _legacy = legacy;
        }

        public async Task<AuthKeyRecord> GetAsync(int dcId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            byte[] encrypted = ReadRow(dcId);
            if (encrypted != null && encrypted.Length > 0)
            {
                try
                {
                    byte[] payload = await _protector.UnprotectAsync(encrypted, ct).ConfigureAwait(false);
                    AuthKeyRecord record = Decode(payload, dcId);
                    if (record != null)
                    {
                        EarlyLog.Write("Storage", "auth-key-v2 hit dc=" + dcId.ToString(CultureInfo.InvariantCulture));
                        return record;
                    }
                }
                catch
                {
                    // Fall back to the legacy JSON envelope below. A corrupt
                    // compact row should not strand an otherwise valid session.
                }
            }

            AuthKeyRecord legacyFast = await TryReadLegacyJsonAsync(dcId, ct).ConfigureAwait(false);
            if (legacyFast != null)
            {
                await PutCompactAsync(legacyFast, ct).ConfigureAwait(false);
                EarlyLog.Write("Storage", "auth-key-v2 migrated-fast dc=" + dcId.ToString(CultureInfo.InvariantCulture));
                return legacyFast;
            }

            if (_legacy == null) return null;
            AuthKeyRecord migrated = await _legacy.GetAsync(dcId, ct).ConfigureAwait(false);
            if (migrated != null)
            {
                await PutCompactAsync(migrated, ct).ConfigureAwait(false);
                EarlyLog.Write("Storage", "auth-key-v2 migrated-legacy dc=" + dcId.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                EarlyLog.Write("Storage", "auth-key-v2 miss dc=" + dcId.ToString(CultureInfo.InvariantCulture));
            }
            return migrated;
        }

        private async Task<AuthKeyRecord> TryReadLegacyJsonAsync(int dcId, CancellationToken ct)
        {
            byte[] encrypted = ReadRow("auth_keys", "state");
            if (encrypted == null || encrypted.Length == 0) return null;

            try
            {
                byte[] jsonBytes = await _protector.UnprotectAsync(encrypted, ct).ConfigureAwait(false);
                string json = Encoding.UTF8.GetString(jsonBytes, 0, jsonBytes.Length);
                return TryParseLegacyJson(json, dcId);
            }
            catch
            {
                return null;
            }
        }

        public async Task PutAsync(AuthKeyRecord record, CancellationToken ct)
        {
            if (record == null) throw new ArgumentNullException("record");
            ct.ThrowIfCancellationRequested();

            await PutCompactAsync(record, ct).ConfigureAwait(false);
        }

        private async Task PutCompactAsync(AuthKeyRecord record, CancellationToken ct)
        {
            byte[] payload = Encode(record);
            byte[] encrypted = await _protector.ProtectAsync(payload, ct).ConfigureAwait(false);
            WriteRow(record.DcId, encrypted);
        }

        public async Task DeleteAsync(int dcId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            DeleteRow(dcId);

            if (_legacy != null)
            {
                try
                {
                    await _legacy.DeleteAsync(dcId, ct).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        public async Task ClearAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ClearRows();

            if (_legacy != null)
            {
                try
                {
                    await _legacy.ClearAsync(ct).ConfigureAwait(false);
                }
                catch
                {
                }
            }
        }

        private static byte[] Encode(AuthKeyRecord record)
        {
            using (var ms = new MemoryStream())
            {
                using (var bw = new BinaryWriter(ms))
                {
                    bw.Write(FormatVersion);
                    bw.Write(record.DcId);
                    bw.Write(record.AuthKeyId);
                    bw.Write(record.CreatedAtUnix);
                    bw.Write(record.ServerTimeOffset);
                    WriteBytes(bw, record.ServerSalt);
                    WriteBytes(bw, record.AuthKey);
                    bw.Flush();
                    return ms.ToArray();
                }
            }
        }

        private static AuthKeyRecord Decode(byte[] payload, int expectedDcId)
        {
            if (payload == null || payload.Length == 0) return null;

            using (var ms = new MemoryStream(payload, false))
            using (var br = new BinaryReader(ms))
            {
                int version = br.ReadInt32();
                if (version != FormatVersion) return null;

                int dcId = br.ReadInt32();
                if (dcId != expectedDcId) return null;

                var record = new AuthKeyRecord();
                record.DcId = dcId;
                record.AuthKeyId = br.ReadInt64();
                record.CreatedAtUnix = br.ReadInt64();
                record.ServerTimeOffset = br.ReadInt32();
                record.ServerSalt = ReadBytes(br);
                record.AuthKey = ReadBytes(br);
                return record;
            }
        }

        private static void WriteBytes(BinaryWriter bw, byte[] value)
        {
            if (value == null)
            {
                bw.Write(-1);
                return;
            }

            bw.Write(value.Length);
            bw.Write(value);
        }

        private static byte[] ReadBytes(BinaryReader br)
        {
            int length = br.ReadInt32();
            if (length < 0) return null;
            if (length == 0) return new byte[0];
            return br.ReadBytes(length);
        }

        private byte[] ReadRow(int dcId)
        {
            return ReadRow(Scope, dcId.ToString(CultureInfo.InvariantCulture));
        }

        private byte[] ReadRow(string scope, string key)
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
                    throw new SqliteException("auth prepare select: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, scope);
                    BindText(stmt, 2, key);

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
                    }
                    else if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException("auth step select: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }

            return result;
        }

        private static AuthKeyRecord TryParseLegacyJson(string json, int dcId)
        {
            if (string.IsNullOrEmpty(json)) return null;

            int search = 0;
            while (search < json.Length)
            {
                int prop = IndexOfProperty(json, "dcId", search);
                if (prop < 0) return null;

                long parsedDcId;
                if (!TryReadLongPropertyAt(json, prop, out parsedDcId))
                {
                    search = prop + 1;
                    continue;
                }

                if (parsedDcId != dcId)
                {
                    search = prop + 1;
                    continue;
                }

                int start = FindObjectStart(json, prop);
                int end = FindObjectEnd(json, start);
                if (start < 0 || end <= start) return null;

                string obj = json.Substring(start, end - start + 1);
                long authKeyId;
                long createdAtUnix;
                long serverTimeOffset;
                byte[] authKey;
                byte[] serverSalt;

                if (!TryReadLongProperty(obj, "authKeyId", out authKeyId)) return null;
                if (!TryReadLongProperty(obj, "createdAtUnix", out createdAtUnix)) createdAtUnix = 0;
                if (!TryReadLongProperty(obj, "serverTimeOffset", out serverTimeOffset)) serverTimeOffset = 0;
                if (!TryReadBytesProperty(obj, "authKey", out authKey)) return null;
                if (!TryReadBytesProperty(obj, "serverSalt", out serverSalt)) serverSalt = null;

                return new AuthKeyRecord
                {
                    DcId = dcId,
                    AuthKeyId = authKeyId,
                    AuthKey = authKey,
                    ServerSalt = serverSalt,
                    CreatedAtUnix = createdAtUnix,
                    ServerTimeOffset = (int)serverTimeOffset
                };
            }

            return null;
        }

        private static int IndexOfProperty(string json, string name, int start)
        {
            return json.IndexOf("\"" + name + "\"", start, StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadLongProperty(string json, string name, out long value)
        {
            int prop = IndexOfProperty(json, name, 0);
            if (prop < 0)
            {
                value = 0;
                return false;
            }
            return TryReadLongPropertyAt(json, prop, out value);
        }

        private static bool TryReadLongPropertyAt(string json, int prop, out long value)
        {
            value = 0;
            int colon = json.IndexOf(':', prop);
            if (colon < 0) return false;

            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return false;

            bool negative = false;
            if (json[i] == '-')
            {
                negative = true;
                i++;
            }

            long acc = 0;
            bool any = false;
            while (i < json.Length && json[i] >= '0' && json[i] <= '9')
            {
                any = true;
                acc = (acc * 10) + (json[i] - '0');
                i++;
            }

            if (!any) return false;
            value = negative ? -acc : acc;
            return true;
        }

        private static bool TryReadBytesProperty(string json, string name, out byte[] value)
        {
            value = null;
            int prop = IndexOfProperty(json, name, 0);
            if (prop < 0) return false;

            int colon = json.IndexOf(':', prop);
            if (colon < 0) return false;

            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return false;

            if (json[i] == 'n') return true;

            if (json[i] == '"')
            {
                int end = json.IndexOf('"', i + 1);
                if (end < 0) return false;
                string text = json.Substring(i + 1, end - i - 1);
                text = text.Replace("\\/", "/");
                value = Convert.FromBase64String(text);
                return true;
            }

            if (json[i] == '[')
            {
                int end = json.IndexOf(']', i + 1);
                if (end < 0) return false;

                var bytes = new List<byte>();
                int pos = i + 1;
                while (pos < end)
                {
                    while (pos < end && (char.IsWhiteSpace(json[pos]) || json[pos] == ',')) pos++;
                    if (pos >= end) break;

                    int start = pos;
                    while (pos < end && json[pos] >= '0' && json[pos] <= '9') pos++;
                    if (start == pos) return false;

                    int n;
                    if (!int.TryParse(json.Substring(start, pos - start), NumberStyles.None, CultureInfo.InvariantCulture, out n))
                    {
                        return false;
                    }
                    if (n < 0 || n > 255) return false;
                    bytes.Add((byte)n);
                }

                value = bytes.ToArray();
                return true;
            }

            return false;
        }

        private static int FindObjectStart(string json, int from)
        {
            for (int i = from; i >= 0; i--)
            {
                if (json[i] == '{') return i;
            }
            return -1;
        }

        private static int FindObjectEnd(string json, int start)
        {
            if (start < 0 || start >= json.Length || json[start] != '{') return -1;

            bool inString = false;
            bool escaped = false;
            int depth = 0;
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (ch == '\\')
                    {
                        escaped = true;
                    }
                    else if (ch == '"')
                    {
                        inString = false;
                    }
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                }
                else if (ch == '{')
                {
                    depth++;
                }
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }

            return -1;
        }

        private void WriteRow(int dcId, byte[] payload)
        {
            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "INSERT OR REPLACE INTO kv(scope, key, value, ts) VALUES (?, ?, ?, ?)");

            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(_db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException("auth prepare upsert: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, Scope);
                    BindText(stmt, 2, dcId.ToString(CultureInfo.InvariantCulture));
                    BindBlob(stmt, 3, payload ?? new byte[0]);
                    BindInt64(stmt, 4, NowUnixMilliseconds());

                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException("auth step upsert: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }
        }

        private void DeleteRow(int dcId)
        {
            byte[] sqlZ = Sqlite3Native.Utf8Z("DELETE FROM kv WHERE scope = ? AND key = ?");
            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(_db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException("auth prepare delete: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, Scope);
                    BindText(stmt, 2, dcId.ToString(CultureInfo.InvariantCulture));
                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException("auth step delete: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }
        }

        private void ClearRows()
        {
            byte[] sqlZ = Sqlite3Native.Utf8Z("DELETE FROM kv WHERE scope = ?");
            lock (_db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(_db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException("auth prepare clear: " + _db.LastError(), rc);
                }
                try
                {
                    BindText(stmt, 1, Scope);
                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException("auth step clear: " + _db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }
        }

        private static void BindText(IntPtr stmt, int idx, string value)
        {
            byte[] buf = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            int rc = Sqlite3Native.sqlite3_bind_text(stmt, idx, buf, buf.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("auth bind_text idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindBlob(IntPtr stmt, int idx, byte[] value)
        {
            int rc = Sqlite3Native.sqlite3_bind_blob(stmt, idx, value, value.Length, Sqlite3Native.SQLITE_TRANSIENT);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("auth bind_blob idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static void BindInt64(IntPtr stmt, int idx, long value)
        {
            int rc = Sqlite3Native.sqlite3_bind_int64(stmt, idx, value);
            if (rc != Sqlite3Native.SQLITE_OK)
            {
                throw new SqliteException("auth bind_int64 idx=" + idx + " rc=" + rc, rc);
            }
        }

        private static long NowUnixMilliseconds()
        {
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            return (long)(DateTime.UtcNow - epoch).TotalMilliseconds;
        }
    }
}
