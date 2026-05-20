// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PeerAvatarFetcher.cs — Vianigram.App.Services
//
// HD avatar pipeline. Coordinates peer-photo downloads through IMediaApi:
//
//   1) Process-local memo so a single peer's avatar isn't re-downloaded
//      across multiple DialogRow creations (cold launch fans out 30
//      concurrent requests; we coalesce them to one per photo_id).
//   2) Build a FileLocation.PeerPhoto from the (dcId, peerKind, peerId,
//      access_hash, photo_id) tuple captured by the peer cache during
//      every users:Vector<User> / chats:Vector<Chat> slice.
//   3) Issue IMediaApi.DownloadAsync with FileType.Photo. The handler
//      runs the parallel chunked download against the right DC (the
//      adapter routes by FileLocation.DcId), caches the bytes via
//      IMediaCache, and emits TransferCompleted on the bus when done.
//   4) On the awaited completion we read the bytes from IMediaCache and
//      decode them on the UI thread into a BitmapImage suitable for
//      DialogRow.AvatarBitmap.
//
// Failures fall back silently to the stripped thumb — the user always
// sees *some* representation, never a blank circle. Network errors,
// FILE_REFERENCE_EXPIRED, cross-DC routing hiccups, and decoder
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
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace Vianigram.App.Services
{
    public sealed class PeerAvatarFetcher
    {
        private readonly IMediaApi _media;
        private readonly IMediaCache _cache;
        private readonly IComponentLogger _log;

        // Memoised completed BitmapImages — one per photo_id. The inner
        // ImageSource is GC-rooted by every DialogRow that binds to it,
        // so we hold a reference here too so a row coming and going
        // doesn't force a re-decode.
        private readonly Dictionary<long, BitmapImage> _bitmapCache =
            new Dictionary<long, BitmapImage>();

        // In-flight coalescing — a second request for the same photo_id
        // while the first is downloading hooks the same Task instead of
        // issuing a parallel duplicate.
        private readonly Dictionary<long, Task<BitmapImage>> _inFlight =
            new Dictionary<long, Task<BitmapImage>>();

        private readonly object _gate = new object();

        public PeerAvatarFetcher(IMediaApi media, IMediaCache cache, IComponentLogger log)
        {
            if (media == null) throw new ArgumentNullException("media");
            if (cache == null) throw new ArgumentNullException("cache");
            _media = media;
            _cache = cache;
            _log = log; // null-tolerant
        }

        /// <summary>
        /// Fetch the small (160×160) profile photo for the given peer.
        /// Returns null when the photo can't be downloaded (network,
        /// access, malformed payload). The caller is expected to leave
        /// the existing stripped-thumb avatar visible in that case.
        /// </summary>
        public Task<BitmapImage> FetchSmallAsync(
            PeerPhotoKind peerKind,
            long peerId,
            long peerAccessHash,
            int dcId,
            long photoId,
            CancellationToken ct)
        {
            if (photoId == 0L || dcId <= 0) return Task.FromResult<BitmapImage>(null);

            lock (_gate)
            {
                BitmapImage cached;
                if (_bitmapCache.TryGetValue(photoId, out cached) && cached != null)
                {
                    return Task.FromResult(cached);
                }
                Task<BitmapImage> running;
                if (_inFlight.TryGetValue(photoId, out running) && running != null)
                {
                    return running;
                }
                Task<BitmapImage> fresh = FetchCoreAsync(peerKind, peerId, peerAccessHash, dcId, photoId, ct);
                _inFlight[photoId] = fresh;
                return fresh;
            }
        }

        private async Task<BitmapImage> FetchCoreAsync(
            PeerPhotoKind peerKind,
            long peerId,
            long peerAccessHash,
            int dcId,
            long photoId,
            CancellationToken ct)
        {
            BitmapImage produced = null;
            try
            {
                if (_log != null) _log.Info(
                    "avatar.fetch begin peer=" + peerKind + ":" + peerId +
                    " photoId=" + photoId + " dc=" + dcId);

                FileLocation location = FileLocation.PeerPhoto(
                    dcId, peerKind, peerId, peerAccessHash, photoId, big: false);

                // Cache fast-path: if a previous run already wrote bytes
                // for this location, just decode them.
                MediaCacheEntry hit = null;
                try { hit = await _cache.TryGetAsync(location, ct).ConfigureAwait(false); }
                catch { /* cache miss / unavailable — fall through */ }

                byte[] bytes = hit != null ? hit.Payload : null;
                if (bytes == null || bytes.Length == 0)
                {
                    // Pass totalSize=0 and let StartDownloadHandler's
                    // autodetect path issue sequential 64 KB chunks
                    // until EOF.
                    // Small (160×160) profile photos are typically
                    // 5-40 KB so the first chunk usually carries
                    // the entire JPEG, and the second chunk returns
                    // empty bytes that the loop treats as EOF. The
                    // earlier short-circuit (totalSize==0 → 0-byte
                    // cache entry) is no longer in effect.
                    var dl = await _media.DownloadAsync(
                        location, FileType.Photo, 0L, ct).ConfigureAwait(false);
                    if (dl.IsFail)
                    {
                        if (_log != null && dl.Error != null)
                        {
                            _log.Info("avatar.fetch fail peer=" + peerKind + ":" + peerId +
                                " photoId=" + photoId + " err=" + dl.Error);
                        }
                        return null;
                    }
                    // Re-read the cache — DownloadAsync stores bytes via
                    // IMediaCache.PutAsync before completing.
                    try { hit = await _cache.TryGetAsync(location, ct).ConfigureAwait(false); }
                    catch { hit = null; }
                    bytes = hit != null ? hit.Payload : null;
                }

                if (bytes == null || bytes.Length == 0)
                {
                    if (_log != null) _log.Info(
                        "avatar.fetch empty payload peer=" + peerKind + ":" + peerId +
                        " photoId=" + photoId);
                    return null;
                }

                // Decode on the UI thread — BitmapImage has UI affinity.
                BitmapImage bmp = null;
                byte[] payload = bytes;
                await Dispatch.OnUiAsync(async () =>
                {
                    bmp = await DecodeOnUiThreadAsync(payload).ConfigureAwait(true);
                }).ConfigureAwait(false);

                produced = bmp;
                if (_log != null)
                {
                    string hexHead = string.Empty;
                    if (bmp == null && payload != null && payload.Length > 0)
                    {
                        // Diagnostic: when the bitmap decode fails despite
                        // having bytes, dump the first 16 bytes so we can
                        // tell whether they're a JPEG (ff d8 ff …), a TL
                        // error response, gzipped data, or random garbage.
                        int n = payload.Length < 16 ? payload.Length : 16;
                        var sb = new System.Text.StringBuilder(n * 2 + 5);
                        sb.Append(" hex=");
                        for (int i = 0; i < n; i++)
                            sb.Append(payload[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                        hexHead = sb.ToString();
                    }
                    _log.Info("avatar.fetch ok peer=" + peerKind + ":" + peerId +
                        " photoId=" + photoId + " bytes=" + payload.Length +
                        " bitmap=" + (bmp != null ? "yes" : "null") + hexHead);
                }
                return produced;
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn(
                    "avatar.fetch threw " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            finally
            {
                lock (_gate)
                {
                    _inFlight.Remove(photoId);
                    if (produced != null) _bitmapCache[photoId] = produced;
                }
            }
        }

        private static async Task<BitmapImage> DecodeOnUiThreadAsync(byte[] bytes)
        {
            using (var ms = new InMemoryRandomAccessStream())
            {
                using (var writer = new DataWriter(ms))
                {
                    writer.WriteBytes(bytes);
                    await writer.StoreAsync();
                    await writer.FlushAsync();
                    writer.DetachStream();
                }
                ms.Seek(0);
                var bmp = new BitmapImage();
                try
                {
                    await bmp.SetSourceAsync(ms);
                    return bmp;
                }
                catch
                {
                    return null;
                }
            }
        }
    }
}
