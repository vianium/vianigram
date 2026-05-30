// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MessageThumbnailFetcher.cs — Vianigram.App.Services
//
// Auto-download a sharp medium-size thumbnail for each photo / video
// message visible in the chat. The inline `photoStrippedSize` ships
// with every message but its
// reconstructed JPEG is only ~40×50 px — when a 280 px-wide bubble
// renders it via Stretch=UniformToFill the result looks heavily
// pixelated. The official clients show a sharp ~320 px thumbnail
// instead by issuing `upload.getFile` for a non-stripped size from
// the photo's `sizes:Vector<PhotoSize>` list.
//
// Pipeline:
//   1) Pick the best non-stripped thumbnail from the message's photo
//      sizes — prefer "x" (~800 px) ➜ "m" (~320 px) ➜ "s" (~100 px),
//      falling back to whatever has Bytes.Length == 0 with the
//      largest declared (Width × Height). Stripped sizes are skipped
//      because they already shipped inline as PreviewBytes.
//   2) Build a FileLocation.Photo with the chosen thumb's SizeType.
//   3) Issue IMediaApi.DownloadAsync — the handler chunks against the
//      photo's DC, caches bytes via IMediaCache, and emits
//      TransferCompleted on the bus when done.
//   4) On completion read the bytes from IMediaCache and persist them
//      into ApplicationData.Current.LocalFolder under
//      "MessageThumbs/{photoId}_{sizeType}.jpg" — ChatPageViewModel
//      picks that path up and assigns it to row.MediaSource so
//      PhotoBubble.LoadImageSourceAsync swaps the blurry stripped
//      preview for the sharp thumbnail.
//
// Coalescing: the same photoId requested across multiple chat opens
// hits the same in-flight Task; a completed photoId stays in the
// memo dictionary so a second chat scroll-back doesn't redownload.
//
// All failure paths are silent — the caller keeps the stripped
// preview visible. Network blips, FILE_REFERENCE_EXPIRED, decode
// glitches all collapse to "use the inline thumbnail".

using System;
using System.Collections.Generic;
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
    public sealed class MessageThumbnailFetcher
    {
        private readonly IMediaApi _media;
        private readonly IMediaCache _cache;
        private readonly IComponentLogger _log;

        private readonly Dictionary<long, string> _pathCache = new Dictionary<long, string>();
        private readonly Dictionary<long, Task<string>> _inFlight = new Dictionary<long, Task<string>>();
        private readonly object _gate = new object();

        public MessageThumbnailFetcher(IMediaApi media, IMediaCache cache, IComponentLogger log)
        {
            if (media == null) throw new ArgumentNullException("media");
            if (cache == null) throw new ArgumentNullException("cache");
            _media = media;
            _cache = cache;
            _log = log; // null-tolerant
        }

        /// <summary>
        /// Fetch a sharp thumbnail for the given photo. Returns the
        /// local file path on success, or null when the photo's wire
        /// data doesn't include a non-stripped size, the download
        /// fails, or persistence trips. Idempotent: the same photoId
        /// resolves to the cached path on subsequent calls.
        /// </summary>
        public Task<string> FetchAsync(
            TelegramMediaFile photoFile,
            IList<MediaThumbnail> thumbnails,
            CancellationToken ct)
        {
            if (photoFile == null) return Task.FromResult<string>(null);

            long photoId;
            if (!long.TryParse(photoFile.FileId, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out photoId) || photoId == 0L)
            {
                return Task.FromResult<string>(null);
            }
            if (photoFile.DcId <= 0) return Task.FromResult<string>(null);

            lock (_gate)
            {
                string cached;
                if (_pathCache.TryGetValue(photoId, out cached) && !string.IsNullOrEmpty(cached))
                {
                    return Task.FromResult(cached);
                }
                Task<string> running;
                if (_inFlight.TryGetValue(photoId, out running) && running != null)
                {
                    return running;
                }
                Task<string> fresh = FetchCoreAsync(photoFile, photoId, thumbnails, ct);
                _inFlight[photoId] = fresh;
                return fresh;
            }
        }

        private async Task<string> FetchCoreAsync(
            TelegramMediaFile photoFile,
            long photoId,
            IList<MediaThumbnail> thumbnails,
            CancellationToken ct)
        {
            string producedPath = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                MediaThumbnail chosen = ChooseThumb(thumbnails);
                if (chosen == null)
                {
                    if (_log != null) _log.Info(
                        "thumb.fetch skip photoId=" + photoId + " no_non_stripped_size");
                    return null;
                }

                FileLocation location = FileLocation.Photo(
                    dcId: photoFile.DcId,
                    id: photoId,
                    accessHash: photoFile.AccessHash,
                    fileReference: photoFile.FileReference,
                    thumbSize: chosen.SizeType ?? string.Empty);

                // Cache fast-path.
                MediaCacheEntry hit = null;
                try { hit = await _cache.TryGetAsync(location, ct).ConfigureAwait(false); }
                catch { /* best-effort */ }
                ct.ThrowIfCancellationRequested();

                byte[] bytes = hit != null ? hit.Payload : null;
                if (bytes == null || bytes.Length == 0)
                {
                    // Pass totalSize=0 and rely on StartDownloadHandler's
                    // autodetect path to fetch sequential 64 KB chunks
                    // until EOF. Most "m"/"x" thumbnails fit in 1-3
                    // chunks (50-200 KB total).
                    var dl = await _media.DownloadAsync(
                        location, FileType.Photo, 0L, ct).ConfigureAwait(false);
                    if (dl.IsFail)
                    {
                        if (_log != null && dl.Error != null)
                        {
                            _log.Info("thumb.fetch fail photoId=" + photoId +
                                " size=" + chosen.SizeType + " err=" + dl.Error);
                        }
                        return null;
                    }
                    ct.ThrowIfCancellationRequested();
                    try { hit = await _cache.TryGetAsync(location, ct).ConfigureAwait(false); }
                    catch { hit = null; }
                    ct.ThrowIfCancellationRequested();
                    bytes = hit != null ? hit.Payload : null;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    return null;
                }

                ct.ThrowIfCancellationRequested();
                producedPath = await PersistAsync(photoId, chosen.SizeType, bytes, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if (_log != null)
                {
                    _log.Info("thumb.fetch ok photoId=" + photoId +
                        " size=" + chosen.SizeType +
                        " bytes=" + bytes.Length +
                        " path=" + (producedPath ?? "null"));
                }
                return producedPath;
            }
            catch (OperationCanceledException) { return null; }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn(
                    "thumb.fetch threw " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(photoId);
                    if (!string.IsNullOrEmpty(producedPath)) _pathCache[photoId] = producedPath;
                }
            }
        }

        // Pick the best thumb size we can fetch via upload.getFile.
        // Stripped sizes ("a"/"b"/"c"/"d" prefixed with i / etc.) are
        // skipped — they're already in the wire payload as PreviewBytes
        // and don't need a network round-trip. Among the rest we
        // prefer "x" (~800 px) which fits a 304-wide bubble at >2x DPR
        // without looking soft, falling back to "m" (~320 px) and
        // finally any non-stripped size at all.
        private static MediaThumbnail ChooseThumb(IList<MediaThumbnail> thumbnails)
        {
            if (thumbnails == null || thumbnails.Count == 0) return null;

            MediaThumbnail bestRanked = null;
            int bestRank = int.MaxValue;
            for (int i = 0; i < thumbnails.Count; i++)
            {
                MediaThumbnail t = thumbnails[i];
                if (t == null) continue;

                // Skip stripped / inline cached: those have Bytes
                // already and going via upload.getFile would re-fetch
                // a representation we already have inline. The wire
                // ctor for stripped + cached produces non-empty
                // Bytes; non-stripped sizes produce empty Bytes.
                bool hasInlineBytes = t.Bytes != null && t.Bytes.Length > 0;
                if (hasInlineBytes) continue;

                int rank = RankSizeType(t.SizeType);
                if (rank < bestRank)
                {
                    bestRanked = t;
                    bestRank = rank;
                }
            }
            return bestRanked;
        }

        // Lower rank = more preferred. Anything not in the lookup
        // gets a rank that places it after the known sizes but
        // before "no idea what this is" (rank 1000).
        private static int RankSizeType(string sizeType)
        {
            if (string.IsNullOrEmpty(sizeType)) return 1000;
            switch (sizeType)
            {
                case "x": return 1; // ~800 px — best for chat bubbles
                case "y": return 2; // ~1280 px — overkill but acceptable
                case "m": return 3; // ~320 px
                case "s": return 4; // ~100 px
                case "w": return 5; // ~2560 px — too much, but better than nothing
                default:  return 100;
            }
        }

        private static async Task<string> PersistAsync(long photoId, string sizeType, byte[] bytes, CancellationToken ct)
        {
            try
            {
                StorageFolder root = ApplicationData.Current.LocalFolder;
                StorageFolder folder = await root.CreateFolderAsync(
                    "MessageThumbs", CreationCollisionOption.OpenIfExists);
                string fileName = photoId.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + "_" + (sizeType ?? "x") + ".jpg";
                StorageFile file = await folder.CreateFileAsync(
                    fileName, CreationCollisionOption.ReplaceExisting);
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
            catch (Exception)
            {
                // Persistence failure is non-fatal — caller will retry
                // on next viewport entry; the stripped preview stays
                // visible meanwhile.
                return null;
            }
        }
    }
}
