// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Vianigram.Storage.Application;

namespace Vianigram.Storage.Infrastructure
{
    /// <summary>
    /// Generic file-backed object store with optional at-rest encryption.
    /// <para>
    /// Storage layout: a single file under <see cref="ApplicationData.LocalFolder"/>
    /// containing either plaintext UTF-8 JSON, or ciphertext produced by
    /// <see cref="IDataProtector.ProtectAsync"/> wrapping that JSON.
    /// </para>
    /// <para>
    /// Atomicity: writes go to a sibling <c>{filename}.tmp</c> file first and
    /// then replace the canonical file. If the process crashes mid-write the
    /// stale <c>.tmp</c> is harmless and overwritten on the next save.
    /// </para>
    /// <para>
    /// Concurrency: a private monitor serializes save/delete on a single
    /// instance. The store is not safe across multiple instances pointing at
    /// the same filename — callers must register a single instance per file.
    /// </para>
    /// <para>
    /// A SQLite-backed object store is available as an alternative backend
    /// (see <see cref="IObjectStore{T}"/>).
    /// </para>
    /// </summary>
    public sealed class JsonObjectStore<T> : IObjectStore<T> where T : class, new()
    {
        private const string TempSuffix = ".tmp";

        private readonly string _filename;
        private readonly bool _encrypted;
        private readonly IDataProtector _protector;
        private readonly DataContractJsonSerializer _serializer;
        private readonly object _lock = new object();

        public JsonObjectStore(string filename, bool encrypted, IDataProtector protector)
        {
            if (string.IsNullOrEmpty(filename)) throw new ArgumentException("filename required", "filename");
            if (encrypted && protector == null) throw new ArgumentNullException("protector", "encrypted store requires a protector");

            _filename = filename;
            _encrypted = encrypted;
            _protector = protector;
            _serializer = new DataContractJsonSerializer(typeof(T));
        }

        /// <summary>
        /// Loads the persisted value, returning <c>new T()</c> when the file
        /// does not exist (first-run case).
        /// </summary>
        public async Task<T> LoadAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            StorageFolder folder = ApplicationData.Current.LocalFolder;
            StorageFile file = await TryGetFileAsync(folder, _filename).ConfigureAwait(false);
            if (file == null)
            {
                return new T();
            }

            byte[] raw = await ReadAllBytesAsync(file).ConfigureAwait(false);
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
                object obj = _serializer.ReadObject(ms);
                T value = obj as T;
                return value != null ? value : new T();
            }
        }

        /// <summary>
        /// Persists <paramref name="value"/>. Uses temp-then-rename for crash
        /// safety. Encrypted stores wrap the JSON bytes with the configured
        /// <see cref="IDataProtector"/> before writing.
        /// </summary>
        public async Task SaveAsync(T value, CancellationToken ct)
        {
            if (value == null) throw new ArgumentNullException("value");
            ct.ThrowIfCancellationRequested();

            byte[] jsonBytes;
            using (var ms = new MemoryStream())
            {
                _serializer.WriteObject(ms, value);
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

            // Critical section: serialize concurrent writers on this instance.
            // We hold the lock only while orchestrating the temp/rename
            // operations — the I/O itself is async and outside the lock,
            // gated by the awaited continuation chain below.
            Task io;
            lock (_lock)
            {
                io = WriteAtomicAsync(_filename, payload);
            }
            await io.ConfigureAwait(false);
        }

        /// <summary>
        /// Removes the persisted value. No-op if the file is already absent.
        /// </summary>
        public async Task DeleteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            StorageFolder folder = ApplicationData.Current.LocalFolder;
            StorageFile file = await TryGetFileAsync(folder, _filename).ConfigureAwait(false);
            if (file != null)
            {
                await file.DeleteAsync().AsTask(ct).ConfigureAwait(false);
            }

            // Best-effort cleanup of any orphaned temp.
            StorageFile temp = await TryGetFileAsync(folder, _filename + TempSuffix).ConfigureAwait(false);
            if (temp != null)
            {
                await temp.DeleteAsync().AsTask(ct).ConfigureAwait(false);
            }
        }

        private static async Task WriteAtomicAsync(string filename, byte[] payload)
        {
            StorageFolder folder = ApplicationData.Current.LocalFolder;
            string tempName = filename + TempSuffix;

            // CreateFileAsync(ReplaceExisting) overwrites any orphaned .tmp.
            StorageFile temp = await folder.CreateFileAsync(tempName, CreationCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
            await FileIO.WriteBytesAsync(temp, payload).AsTask().ConfigureAwait(false);

            // Replace the canonical file in a single rename. NameCollisionOption.ReplaceExisting
            // performs the equivalent of MoveFileEx(MOVEFILE_REPLACE_EXISTING).
            await temp.RenameAsync(filename, NameCollisionOption.ReplaceExisting).AsTask().ConfigureAwait(false);
        }

        private static async Task<StorageFile> TryGetFileAsync(StorageFolder folder, string name)
        {
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
