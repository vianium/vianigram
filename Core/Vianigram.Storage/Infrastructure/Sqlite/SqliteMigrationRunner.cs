// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SqliteMigrationRunner — one-shot migration from the legacy JsonObjectStore
// per-aggregate .bin files into the shared vianigram.db kv table. Runs
// once per install: when each source .bin file is found, its raw bytes are
// inserted under the matching scope and the file is renamed with a
// .migrated suffix so subsequent launches skip it.

using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage;

namespace Vianigram.Storage.Infrastructure.Sqlite
{
    /// <summary>
    /// One-time migration of legacy .bin aggregate files into the shared
    /// SQLite store. Idempotent: repeated calls only act on files that still
    /// exist (i.e. that have not been renamed to <c>.migrated</c>).
    /// </summary>
    public static class SqliteMigrationRunner
    {
        // Maps the on-disk JsonObjectStore filenames to the SQLite scope used
        // by SqliteObjectStore<T>. Order is irrelevant — each row is
        // independent. The scope literals match the conventions used by the
        // composition root for each repository.
        private static readonly string[,] _map = new string[,]
        {
            { "auth_keys.bin",  "auth_keys"  },
            { "dialogs.bin",    "dialogs"    },
            { "messages.bin",   "messages"   },
            { "sync_state.bin", "sync_state" },
        };

        /// <summary>
        /// Ports any legacy .bin files that still live in
        /// <see cref="ApplicationData.LocalFolder"/> into the supplied database.
        /// Encrypted aggregates are migrated as opaque ciphertext blobs — the
        /// SqliteObjectStore decrypts on read using the same protector that
        /// produced them.
        /// </summary>
        public static async Task<int> RunAsync(SqliteDatabase db)
        {
            if (db == null) throw new ArgumentNullException("db");

            int migrated = 0;
            StorageFolder folder = ApplicationData.Current.LocalFolder;
            int rows = _map.GetLength(0);

            for (int i = 0; i < rows; i++)
            {
                string fileName = _map[i, 0];
                string scope = _map[i, 1];

                StorageFile file = await TryGetFileAsync(folder, fileName).ConfigureAwait(false);
                if (file == null) continue;

                byte[] raw = await ReadAllBytesAsync(file).ConfigureAwait(false);
                if (raw == null) raw = new byte[0];

                InsertRaw(db, scope, raw);

                // Rename so we never re-migrate. ReplaceExisting keeps a single
                // .migrated copy if the user retries the migration after an
                // earlier crash.
                try
                {
                    await file.RenameAsync(fileName + ".migrated", NameCollisionOption.ReplaceExisting)
                              .AsTask().ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort. Failing to rename does not invalidate the
                    // migrated row; the next run will simply overwrite the
                    // same scope.
                }

                migrated++;
            }

            return migrated;
        }

        // -- Helpers ----------------------------------------------------------

        private static void InsertRaw(SqliteDatabase db, string scope, byte[] payload)
        {
            // We bypass SqliteObjectStore here because it would re-serialize
            // the (deserialized) aggregate. Migration must preserve the on-disk
            // bytes exactly so encrypted blobs unwrap correctly later.
            byte[] sqlZ = Sqlite3Native.Utf8Z(
                "INSERT OR REPLACE INTO kv(scope, key, value, ts) VALUES (?, 'state', ?, ?)");

            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            long ts = (long)(DateTime.UtcNow - epoch).TotalMilliseconds;

            lock (db.Gate)
            {
                IntPtr stmt;
                int rc = Sqlite3Native.sqlite3_prepare_v2(db.Handle, sqlZ, sqlZ.Length, out stmt, IntPtr.Zero);
                if (rc != Sqlite3Native.SQLITE_OK)
                {
                    throw new SqliteException("migration prepare: " + db.LastError(), rc);
                }
                try
                {
                    byte[] scopeBuf = System.Text.Encoding.UTF8.GetBytes(scope);
                    Sqlite3Native.sqlite3_bind_text(stmt, 1, scopeBuf, scopeBuf.Length, Sqlite3Native.SQLITE_TRANSIENT);
                    Sqlite3Native.sqlite3_bind_blob(stmt, 2, payload, payload.Length, Sqlite3Native.SQLITE_TRANSIENT);
                    Sqlite3Native.sqlite3_bind_int64(stmt, 3, ts);

                    rc = Sqlite3Native.sqlite3_step(stmt);
                    if (rc != Sqlite3Native.SQLITE_DONE)
                    {
                        throw new SqliteException("migration step: " + db.LastError(), rc);
                    }
                }
                finally
                {
                    Sqlite3Native.sqlite3_finalize(stmt);
                }
            }
        }

        private static async Task<StorageFile> TryGetFileAsync(StorageFolder folder, string name)
        {
            if (folder == null || string.IsNullOrEmpty(name)) return null;

            // WP 8.1 contract surface does not expose StorageFolder.TryGetItemAsync;
            // fall back to GetFileAsync + FileNotFoundException, which is the
            // documented pattern for this platform.
            try
            {
                return await folder.GetFileAsync(name).AsTask().ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }

        private static async Task<byte[]> ReadAllBytesAsync(StorageFile file)
        {
            Windows.Storage.Streams.IBuffer buf = await FileIO.ReadBufferAsync(file).AsTask().ConfigureAwait(false);
            byte[] bytes;
            Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(buf, out bytes);
            return bytes;
        }
    }
}
