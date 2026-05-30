// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Media.Ports.Inbound;
using Vianigram.Media.Ports.Outbound;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Vianigram.App.Services
{
    /// <summary>
    /// Progressive audio/video fetcher. It writes Telegram ranges to a local
    /// file, returns once an initial buffer exists, then keeps filling the same
    /// file in the background so MediaElement can begin playback early.
    /// </summary>
    public sealed class ProgressiveMediaFileFetcher
    {
        private const int AudioChunkBytes = 256 * 1024;
        private const int VideoChunkBytes = 512 * 1024;
        private const int MaxInitialAudioBytes = 256 * 1024;
        private const int MaxInitialVideoBytes = 1024 * 1024;

        private readonly IMediaApi _media;
        private readonly IMediaCache _cache;
        private readonly IComponentLogger _log;
        private readonly Dictionary<string, string> _pathCache = new Dictionary<string, string>();
        private readonly Dictionary<string, Task<ProgressiveMediaFetchResult>> _inFlight =
            new Dictionary<string, Task<ProgressiveMediaFetchResult>>();
        private readonly object _gate = new object();

        public ProgressiveMediaFileFetcher(IMediaApi media, IMediaCache cache, IComponentLogger log)
        {
            if (media == null) throw new ArgumentNullException("media");
            if (cache == null) throw new ArgumentNullException("cache");
            _media = media;
            _cache = cache;
            _log = log;
        }

        public Task<ProgressiveMediaFetchResult> FetchDocumentMediaAsync(
            long documentId,
            long accessHash,
            byte[] fileReference,
            int dcId,
            string fileName,
            string mimeType,
            long totalSize,
            FileType fileType,
            Action<ProgressiveMediaProgress> progress,
            CancellationToken ct)
        {
            if (documentId == 0L || accessHash == 0L || dcId <= 0)
                return TaskFromResult<ProgressiveMediaFetchResult>(null);

            FileLocation location = FileLocation.Document(
                dcId, documentId, accessHash, fileReference ?? new byte[0], string.Empty);
            string key = MakeKey(documentId, accessHash);

            lock (_gate)
            {
                string cachedPath;
                if (_pathCache.TryGetValue(key, out cachedPath) && !string.IsNullOrEmpty(cachedPath))
                {
                    return TaskFromResult(new ProgressiveMediaFetchResult(
                        cachedPath, totalSize, totalSize, true, CompletedTask()));
                }

                Task<ProgressiveMediaFetchResult> running;
                if (_inFlight.TryGetValue(key, out running) && running != null)
                    return running;

                Task<ProgressiveMediaFetchResult> fresh = FetchCoreAsync(
                    key,
                    location,
                    documentId,
                    fileName,
                    mimeType,
                    totalSize < 0 ? 0 : totalSize,
                    fileType,
                    progress,
                    ct);
                _inFlight[key] = fresh;
                return fresh;
            }
        }

        private async Task<ProgressiveMediaFetchResult> FetchCoreAsync(
            string key,
            FileLocation location,
            long stableId,
            string fileName,
            string mimeType,
            long totalSize,
            FileType fileType,
            Action<ProgressiveMediaProgress> progress,
            CancellationToken ct)
        {
            string producedPath = null;
            bool cacheProducedPath = false;
            try
            {
                ct.ThrowIfCancellationRequested();

                MediaCacheEntry hit = null;
                try { hit = await _cache.TryGetAsync(location, ct).ConfigureAwait(false); }
                catch { hit = null; }

                byte[] cachedBytes = hit != null ? hit.Payload : null;
                if (cachedBytes != null && cachedBytes.Length > 0)
                {
                    producedPath = await PersistWholeAsync(
                        stableId, fileName, mimeType, cachedBytes, fileType).ConfigureAwait(false);
                    cacheProducedPath = true;
                    Publish(progress, producedPath, cachedBytes.Length, cachedBytes.Length, true, true);
                    return new ProgressiveMediaFetchResult(
                        producedPath, cachedBytes.Length, cachedBytes.Length, true, CompletedTask());
                }

                StorageFile file = await CreateBufferFileAsync(
                    stableId, fileName, mimeType, fileType).ConfigureAwait(false);
                producedPath = file.Path;

                int chunkBytes = PickChunkBytes(fileType, mimeType);
                long initialTarget = PickInitialTargetBytes(fileType, mimeType, totalSize, chunkBytes);
                long offset = 0;

                while (offset < initialTarget || offset == 0)
                {
                    RangeRead read = await DownloadAndWriteRangeAsync(
                        location, file, offset, chunkBytes, ct).ConfigureAwait(false);
                    if (read.Error != null)
                    {
                        LogInfo("progressive.fetch initial fail id=" + stableId + " err=" + read.Error);
                        return null;
                    }

                    offset += read.BytesRead;
                    bool completed = IsCompleted(totalSize, offset, read.BytesRead, chunkBytes);
                    Publish(progress, producedPath, offset, totalSize, completed, completed);

                    if (completed)
                    {
                        cacheProducedPath = true;
                        LogInfo("progressive.fetch complete-initial id=" + stableId +
                            " bytes=" + offset.ToString(CultureInfo.InvariantCulture));
                        return new ProgressiveMediaFetchResult(
                            producedPath, offset, totalSize, true, CompletedTask());
                    }

                    if (read.BytesRead <= 0) break;
                }

                Task completion = Task.Run(async delegate
                {
                    await ContinueDownloadAsync(
                        key, location, file, producedPath, offset, totalSize,
                        chunkBytes, progress, ct).ConfigureAwait(false);
                });

                Publish(progress, producedPath, offset, totalSize, true, false);
                cacheProducedPath = true;
                LogInfo("progressive.fetch playable id=" + stableId +
                    " buffered=" + offset.ToString(CultureInfo.InvariantCulture) +
                    " total=" + totalSize.ToString(CultureInfo.InvariantCulture) +
                    " path=" + producedPath);

                return new ProgressiveMediaFetchResult(
                    producedPath, offset, totalSize, false, completion);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                LogWarn("progressive.fetch threw " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(key);
                    if (cacheProducedPath && !string.IsNullOrEmpty(producedPath))
                        _pathCache[key] = producedPath;
                }
            }
        }

        private async Task ContinueDownloadAsync(
            string key,
            FileLocation location,
            StorageFile file,
            string path,
            long offset,
            long totalSize,
            int chunkBytes,
            Action<ProgressiveMediaProgress> progress,
            CancellationToken ct)
        {
            try
            {
                while (true)
                {
                    RangeRead read = await DownloadAndWriteRangeAsync(
                        location, file, offset, chunkBytes, ct).ConfigureAwait(false);
                    if (read.Error != null)
                    {
                        LogInfo("progressive.fetch background fail path=" + path + " err=" + read.Error);
                        return;
                    }

                    offset += read.BytesRead;
                    bool completed = IsCompleted(totalSize, offset, read.BytesRead, chunkBytes);
                    Publish(progress, path, offset, totalSize, true, completed);
                    if (completed)
                    {
                        LogInfo("progressive.fetch complete path=" + path +
                            " bytes=" + offset.ToString(CultureInfo.InvariantCulture));
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LogWarn("progressive.fetch background threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private async Task<RangeRead> DownloadAndWriteRangeAsync(
            FileLocation location,
            StorageFile file,
            long offset,
            int limit,
            CancellationToken ct)
        {
            Result<byte[], MediaError> range = await _media.DownloadRangeAsync(
                location, offset, limit, ct).ConfigureAwait(false);
            if (range.IsFail)
                return new RangeRead(0, range.Error);

            byte[] bytes = range.Value ?? new byte[0];
            if (bytes.Length > 0)
                await WriteRangeAsync(file, offset, bytes).ConfigureAwait(false);

            return new RangeRead(bytes.Length, null);
        }

        private static async Task WriteRangeAsync(StorageFile file, long offset, byte[] bytes)
        {
            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            using (IOutputStream output = stream.GetOutputStreamAt((ulong)offset))
            using (DataWriter writer = new DataWriter(output))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
        }

        private static async Task<string> PersistWholeAsync(
            long stableId,
            string fileName,
            string mimeType,
            byte[] bytes,
            FileType fileType)
        {
            StorageFile file = await CreateBufferFileAsync(stableId, fileName, mimeType, fileType)
                .ConfigureAwait(false);
            await WriteRangeAsync(file, 0, bytes).ConfigureAwait(false);
            return file.Path;
        }

        private static async Task<StorageFile> CreateBufferFileAsync(
            long stableId,
            string fileName,
            string mimeType,
            FileType fileType)
        {
            StorageFolder root = ApplicationData.Current.LocalFolder;
            StorageFolder folder = await root.CreateFolderAsync(
                "MediaBuffer", CreationCollisionOption.OpenIfExists);

            string safeName = SanitizeFileName(fileName);
            if (string.IsNullOrEmpty(safeName))
                safeName = DefaultName(fileType) + ExtensionForMime(mimeType);
            if (safeName.IndexOf('.') < 0)
                safeName = safeName + ExtensionForMime(mimeType);

            string persistedName = stableId.ToString(CultureInfo.InvariantCulture) + "_" + safeName;
            return await folder.CreateFileAsync(
                persistedName, CreationCollisionOption.ReplaceExisting);
        }

        private static int PickChunkBytes(FileType fileType, string mimeType)
        {
            if (IsVideo(fileType, mimeType)) return VideoChunkBytes;
            return AudioChunkBytes;
        }

        private static long PickInitialTargetBytes(FileType fileType, string mimeType, long totalSize, int chunkBytes)
        {
            long target = IsVideo(fileType, mimeType) ? MaxInitialVideoBytes : MaxInitialAudioBytes;
            if (target < chunkBytes) target = chunkBytes;
            if (totalSize > 0 && target > totalSize) target = totalSize;
            return target <= 0 ? chunkBytes : target;
        }

        private static bool IsCompleted(long totalSize, long offset, int bytesRead, int chunkBytes)
        {
            if (bytesRead <= 0) return true;
            if (bytesRead < chunkBytes) return true;
            return totalSize > 0 && offset >= totalSize;
        }

        private static bool IsVideo(FileType fileType, string mimeType)
        {
            if (fileType == FileType.Video) return true;
            return !string.IsNullOrEmpty(mimeType) &&
                mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        }

        private static void Publish(
            Action<ProgressiveMediaProgress> progress,
            string path,
            long bytesBuffered,
            long totalBytes,
            bool isPlayable,
            bool isCompleted)
        {
            if (progress == null) return;
            try
            {
                progress(new ProgressiveMediaProgress(
                    path, bytesBuffered, totalBytes, isPlayable, isCompleted));
            }
            catch { }
        }

        private static string MakeKey(long documentId, long accessHash)
        {
            return documentId.ToString(CultureInfo.InvariantCulture) + ":" +
                accessHash.ToString(CultureInfo.InvariantCulture);
        }

        private static string DefaultName(FileType fileType)
        {
            switch (fileType)
            {
                case FileType.Video: return "video";
                case FileType.Voice: return "voice";
                case FileType.Photo: return "photo";
                default: return "audio";
            }
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            var invalid = new char[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            var chars = trimmed.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] == invalid[j])
                    {
                        chars[i] = '_';
                        break;
                    }
                }
            }

            string result = new string(chars);
            while (result.EndsWith(".", StringComparison.Ordinal) ||
                   result.EndsWith(" ", StringComparison.Ordinal))
            {
                result = result.Substring(0, result.Length - 1);
            }
            return result;
        }

        private static string ExtensionForMime(string mimeType)
        {
            string mime = mimeType ?? string.Empty;
            if (mime.IndexOf("audio/mp4", StringComparison.OrdinalIgnoreCase) >= 0) return ".m4a";
            if (mime.IndexOf("mp4", StringComparison.OrdinalIgnoreCase) >= 0) return ".mp4";
            if (mime.IndexOf("quicktime", StringComparison.OrdinalIgnoreCase) >= 0) return ".mov";
            if (mime.IndexOf("3gpp", StringComparison.OrdinalIgnoreCase) >= 0) return ".3gp";
            if (mime.IndexOf("mpeg", StringComparison.OrdinalIgnoreCase) >= 0) return ".mp3";
            if (mime.IndexOf("ogg", StringComparison.OrdinalIgnoreCase) >= 0) return ".ogg";
            if (mime.IndexOf("opus", StringComparison.OrdinalIgnoreCase) >= 0) return ".ogg";
            if (mime.IndexOf("aac", StringComparison.OrdinalIgnoreCase) >= 0) return ".aac";
            if (mime.IndexOf("wav", StringComparison.OrdinalIgnoreCase) >= 0) return ".wav";
            if (mime.IndexOf("webm", StringComparison.OrdinalIgnoreCase) >= 0) return ".webm";
            return ".bin";
        }

        private void LogInfo(string message)
        {
            if (_log != null) _log.Info(message);
        }

        private void LogWarn(string message)
        {
            if (_log != null) _log.Warn(message);
        }

        private static Task<T> TaskFromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }

        private static Task CompletedTask()
        {
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);
            return tcs.Task;
        }

        private sealed class RangeRead
        {
            public RangeRead(int bytesRead, MediaError error)
            {
                BytesRead = bytesRead;
                Error = error;
            }

            public int BytesRead { get; private set; }
            public MediaError Error { get; private set; }
        }
    }

    public sealed class ProgressiveMediaFetchResult
    {
        public ProgressiveMediaFetchResult(
            string localPath,
            long bytesBuffered,
            long totalBytes,
            bool isComplete,
            Task completion)
        {
            LocalPath = localPath ?? string.Empty;
            BytesBuffered = bytesBuffered;
            TotalBytes = totalBytes;
            IsComplete = isComplete;
            Completion = completion;
        }

        public string LocalPath { get; private set; }
        public long BytesBuffered { get; private set; }
        public long TotalBytes { get; private set; }
        public bool IsComplete { get; private set; }
        public Task Completion { get; private set; }
    }

    public sealed class ProgressiveMediaProgress
    {
        public ProgressiveMediaProgress(
            string localPath,
            long bytesBuffered,
            long totalBytes,
            bool isPlayable,
            bool isCompleted)
        {
            LocalPath = localPath ?? string.Empty;
            BytesBuffered = bytesBuffered;
            TotalBytes = totalBytes;
            IsPlayable = isPlayable;
            IsCompleted = isCompleted;
        }

        public string LocalPath { get; private set; }
        public long BytesBuffered { get; private set; }
        public long TotalBytes { get; private set; }
        public bool IsPlayable { get; private set; }
        public bool IsCompleted { get; private set; }
        public double Percent
        {
            get
            {
                if (TotalBytes <= 0) return 0.0;
                double p = (BytesBuffered * 100.0) / TotalBytes;
                if (p < 0.0) return 0.0;
                if (p > 100.0) return 100.0;
                return p;
            }
        }
    }
}
