// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Media.Domain;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Media.Ports.Inbound;
using Vianigram.Media.Ports.Outbound;
using Vianigram.Messages.Domain.ValueObjects;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Vianigram.App.Services
{
    /// <summary>
    /// Downloads Telegram documents through the media bounded context,
    /// persists them under LocalState\Documents with their original extension,
    /// and returns the path that Windows.System.Launcher can open natively.
    /// </summary>
    public sealed class DocumentFileFetcher
    {
        private readonly IMediaApi _media;
        private readonly IMediaCache _cache;
        private readonly IComponentLogger _log;

        private readonly Dictionary<string, string> _pathCache = new Dictionary<string, string>();
        private readonly Dictionary<string, Task<string>> _inFlight = new Dictionary<string, Task<string>>();
        private readonly object _gate = new object();

        public DocumentFileFetcher(IMediaApi media, IMediaCache cache, IComponentLogger log)
        {
            if (media == null) throw new ArgumentNullException("media");
            if (cache == null) throw new ArgumentNullException("cache");
            _media = media;
            _cache = cache;
            _log = log;
        }

        public Task<string> FetchAsync(
            long documentId,
            long accessHash,
            byte[] fileReference,
            int dcId,
            string fileName,
            string mimeType,
            long totalSize,
            CancellationToken ct)
        {
            return FetchDocumentMediaAsync(
                documentId, accessHash, fileReference, dcId,
                fileName, mimeType, totalSize, FileType.Document, ct);
        }

        public Task<string> FetchDocumentMediaAsync(
            long documentId,
            long accessHash,
            byte[] fileReference,
            int dcId,
            string fileName,
            string mimeType,
            long totalSize,
            FileType fileType,
            CancellationToken ct)
        {
            if (documentId == 0L || accessHash == 0L || dcId <= 0)
                return TaskFromResult<string>(null);

            FileLocation location = FileLocation.Document(
                dcId, documentId, accessHash, fileReference ?? new byte[0], string.Empty);
            string key = MakeKey("doc", documentId, accessHash, string.Empty);
            lock (_gate)
            {
                string cached;
                if (_pathCache.TryGetValue(key, out cached) && !string.IsNullOrEmpty(cached))
                    return TaskFromResult(cached);

                Task<string> running;
                if (_inFlight.TryGetValue(key, out running) && running != null)
                    return running;

                Task<string> fresh = FetchCoreAsync(
                    key, location, documentId, fileName, mimeType,
                    totalSize, fileType, ct);
                _inFlight[key] = fresh;
                return fresh;
            }
        }

        public Task<string> FetchPhotoAsync(
            long photoId,
            long accessHash,
            byte[] fileReference,
            int dcId,
            string thumbSize,
            long totalSize,
            CancellationToken ct)
        {
            if (photoId == 0L || accessHash == 0L || dcId <= 0 || string.IsNullOrEmpty(thumbSize))
                return TaskFromResult<string>(null);

            FileLocation location = FileLocation.Photo(
                dcId, photoId, accessHash, fileReference ?? new byte[0], thumbSize);
            string key = MakeKey("photo:" + thumbSize, photoId, accessHash, thumbSize);
            lock (_gate)
            {
                string cached;
                if (_pathCache.TryGetValue(key, out cached) && !string.IsNullOrEmpty(cached))
                    return TaskFromResult(cached);

                Task<string> running;
                if (_inFlight.TryGetValue(key, out running) && running != null)
                    return running;

                Task<string> fresh = FetchCoreAsync(
                    key, location, photoId, "photo.jpg", "image/jpeg",
                    totalSize, FileType.Photo, ct);
                _inFlight[key] = fresh;
                return fresh;
            }
        }

        private async Task<string> FetchCoreAsync(
            string key,
            FileLocation location,
            long stableId,
            string fileName,
            string mimeType,
            long totalSize,
            FileType fileType,
            CancellationToken ct)
        {
            string producedPath = null;
            try
            {
                ct.ThrowIfCancellationRequested();

                MediaCacheEntry hit = null;
                try { hit = await _cache.TryGetAsync(location, ct).ConfigureAwait(false); }
                catch { hit = null; }

                ct.ThrowIfCancellationRequested();
                byte[] bytes = hit != null ? hit.Payload : null;
                if (bytes == null || bytes.Length == 0)
                {
                    var dl = await _media.DownloadAsync(
                        location, fileType, totalSize, ct).ConfigureAwait(false);
                    if (dl.IsFail)
                    {
                        if (_log != null && dl.Error != null)
                        {
                            _log.Info("media.fetch fail id=" +
                                stableId.ToString(CultureInfo.InvariantCulture) +
                                " err=" + dl.Error);
                        }
                        return null;
                    }

                    ct.ThrowIfCancellationRequested();
                    try { hit = await _cache.TryGetAsync(location, ct).ConfigureAwait(false); }
                    catch { hit = null; }
                    bytes = hit != null ? hit.Payload : null;
                }

                if (bytes == null || bytes.Length == 0)
                    return null;

                ct.ThrowIfCancellationRequested();
                producedPath = await PersistAsync(stableId, fileName, mimeType, bytes, fileType).ConfigureAwait(false);
                if (_log != null)
                {
                    _log.Info("media.fetch ok id=" +
                        stableId.ToString(CultureInfo.InvariantCulture) +
                        " bytes=" + bytes.Length.ToString(CultureInfo.InvariantCulture) +
                        " path=" + (producedPath ?? "null"));
                }
                return producedPath;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                if (_log != null)
                    _log.Warn("media.fetch threw " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(key);
                    if (!string.IsNullOrEmpty(producedPath)) _pathCache[key] = producedPath;
                }
            }
        }

        private static async Task<string> PersistAsync(
            long documentId,
            string fileName,
            string mimeType,
            byte[] bytes,
            FileType fileType)
        {
            StorageFolder root = ApplicationData.Current.LocalFolder;
            StorageFolder folder = await root.CreateFolderAsync(
                FolderName(fileType), CreationCollisionOption.OpenIfExists);

            string safeName = SanitizeFileName(fileName);
            if (string.IsNullOrEmpty(safeName))
                safeName = "document" + ExtensionForMime(mimeType);
            if (safeName.IndexOf('.') < 0)
                safeName = safeName + ExtensionForMime(mimeType);

            string persistedName = documentId.ToString(CultureInfo.InvariantCulture) + "_" + safeName;
            StorageFile file = await folder.CreateFileAsync(
                persistedName, CreationCollisionOption.ReplaceExisting);

            using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.ReadWrite))
            using (var output = stream.GetOutputStreamAt(0))
            using (var writer = new DataWriter(output))
            {
                writer.WriteBytes(bytes);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            return file.Path;
        }

        private static string MakeKey(string kind, long documentId, long accessHash, string variant)
        {
            return (kind ?? string.Empty) + ":" +
                documentId.ToString(CultureInfo.InvariantCulture) + ":" +
                accessHash.ToString(CultureInfo.InvariantCulture) + ":" +
                (variant ?? string.Empty);
        }

        private static string FolderName(FileType fileType)
        {
            switch (fileType)
            {
                case FileType.Photo: return "Photos";
                case FileType.Video: return "Videos";
                case FileType.Voice: return "Audio";
                case FileType.Sticker: return "Stickers";
                default: return "Documents";
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
            if (mime.IndexOf("wordprocessingml.document", StringComparison.OrdinalIgnoreCase) >= 0) return ".docx";
            if (mime.IndexOf("msword", StringComparison.OrdinalIgnoreCase) >= 0) return ".doc";
            if (mime.IndexOf("spreadsheetml.sheet", StringComparison.OrdinalIgnoreCase) >= 0) return ".xlsx";
            if (mime.IndexOf("ms-excel", StringComparison.OrdinalIgnoreCase) >= 0) return ".xls";
            if (mime.IndexOf("presentationml.presentation", StringComparison.OrdinalIgnoreCase) >= 0) return ".pptx";
            if (mime.IndexOf("ms-powerpoint", StringComparison.OrdinalIgnoreCase) >= 0) return ".ppt";
            if (mime.IndexOf("pdf", StringComparison.OrdinalIgnoreCase) >= 0) return ".pdf";
            if (mime.IndexOf("zip", StringComparison.OrdinalIgnoreCase) >= 0) return ".zip";
            if (mime.IndexOf("jpeg", StringComparison.OrdinalIgnoreCase) >= 0) return ".jpg";
            if (mime.IndexOf("jpg", StringComparison.OrdinalIgnoreCase) >= 0) return ".jpg";
            if (mime.IndexOf("png", StringComparison.OrdinalIgnoreCase) >= 0) return ".png";
            if (mime.IndexOf("gif", StringComparison.OrdinalIgnoreCase) >= 0) return ".gif";
            if (mime.IndexOf("webp", StringComparison.OrdinalIgnoreCase) >= 0) return ".webp";
            if (mime.IndexOf("mp4", StringComparison.OrdinalIgnoreCase) >= 0) return ".mp4";
            if (mime.IndexOf("quicktime", StringComparison.OrdinalIgnoreCase) >= 0) return ".mov";
            if (mime.IndexOf("webm", StringComparison.OrdinalIgnoreCase) >= 0) return ".webm";
            if (mime.IndexOf("ogg", StringComparison.OrdinalIgnoreCase) >= 0) return ".ogg";
            if (mime.IndexOf("opus", StringComparison.OrdinalIgnoreCase) >= 0) return ".ogg";
            if (mime.IndexOf("mpeg", StringComparison.OrdinalIgnoreCase) >= 0) return ".mp3";
            if (mime.IndexOf("audio/mp4", StringComparison.OrdinalIgnoreCase) >= 0) return ".m4a";
            if (mime.StartsWith("text/", StringComparison.OrdinalIgnoreCase)) return ".txt";
            return ".bin";
        }

        private static Task<T> TaskFromResult<T>(T value)
        {
            var tcs = new TaskCompletionSource<T>();
            tcs.SetResult(value);
            return tcs.Task;
        }
    }
}
