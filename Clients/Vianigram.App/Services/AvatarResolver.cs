// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AvatarResolver — shared bridge from a peerKey string (e.g. "user:123",
// "channel:456", "chat:789") to a BitmapImage suitable for binding into
// AvatarCircle.Image. Reuses the existing PeerAvatarFetcher (with its
// process-local + SQLite disk caches) and the shared InMemoryPeerCache
// (where photoId + dcId for every known peer live, populated by every
// typed RPC response). Lets ChatPage and ProfilePage paint the same
// avatar the ChatList already has — the dialog list, chat header, and
// peer profile are all addressing the same photoId and so should land
// on the same cached BitmapImage instance.
//
// Falls back gracefully: any missing dependency or unknown peer returns
// null and the caller keeps the initials-only placeholder visible.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Composition.Infrastructure;
using Vianigram.Kernel.Logging;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Media.Ports.Inbound;
using Vianigram.Media.Ports.Outbound;
using Vianigram.Storage.Ports.Stubs;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;

namespace Vianigram.App.Services
{
    /// <summary>
    /// Shared helper that turns a peer-key string into a ready-to-bind
    /// avatar bitmap. Single source of truth for "load the avatar bytes
    /// I might already have cached for this peer."
    /// </summary>
    public static class AvatarResolver
    {
        // Single process-wide fetcher — reuses the in-memory bitmap cache
        // so the same BitmapImage object is handed back to every caller
        // for the same photoId. Lazy-initialised because IMediaApi is a
        // lazy registration in the composition root.
        private static readonly object _gate = new object();
        private static PeerAvatarFetcher _fetcher;
        private static bool _resolveFailed;

        /// <summary>
        /// Resolves the small (160×160) avatar bitmap for the given
        /// <paramref name="peerKey"/>. Returns null when the peer is
        /// unknown, when no photo metadata has been hydrated yet, or
        /// when the bytes can't be fetched.
        ///
        /// <paramref name="peerKey"/> follows the same convention as
        /// DialogRow.PeerKey: "user:&lt;id&gt;", "chat:&lt;id&gt;",
        /// or "channel:&lt;id&gt;" (case-insensitive prefix).
        /// </summary>
        public static async Task<ImageSource> TryResolveSmallAsync(
            string peerKey,
            CancellationToken ct)
        {
            if (string.IsNullOrEmpty(peerKey)) return null;

            long peerId;
            PeerPhotoKind kind;
            if (!TryParsePeerKey(peerKey, out kind, out peerId)) return null;

            return await TryResolveSmallAsync(kind, peerId, ct).ConfigureAwait(true);
        }

        /// <summary>
        /// Typed overload — saves the parse step when the caller already
        /// has the (kind, id) pair on hand.
        /// </summary>
        public static async Task<ImageSource> TryResolveSmallAsync(
            PeerPhotoKind kind,
            long peerId,
            CancellationToken ct)
        {
            if (peerId <= 0) return null;

            if (App.Composition == null) return null;

            IPeerCache peerCache;
            if (!App.Composition.TryResolve<IPeerCache>(out peerCache) || peerCache == null) return null;

            // Resolve the photoId + dcId pair the cache last saw for
            // this peer. If we don't have it we can't issue the file
            // download — return null and let the caller fall back to
            // initials.
            long photoId;
            int dcId;
            bool hasRef = kind == PeerPhotoKind.User
                ? peerCache.TryGetUserPhotoRef(peerId, out photoId, out dcId)
                : peerCache.TryGetChatPhotoRef(peerId, out photoId, out dcId);
            if (!hasRef || photoId == 0L || dcId <= 0) return null;

            // Look up the access_hash for User and Channel — the file
            // download is signed against it. Chat (basic group) photos
            // don't carry an access_hash.
            long accessHash = 0L;
            if (kind == PeerPhotoKind.User)
            {
                long? maybe = peerCache.GetUserAccessHash(peerId);
                accessHash = maybe.HasValue ? maybe.Value : 0L;
            }
            else if (kind == PeerPhotoKind.Channel)
            {
                long? maybe = peerCache.GetChannelAccessHash(peerId);
                accessHash = maybe.HasValue ? maybe.Value : 0L;
            }

            PeerAvatarFetcher fetcher = ResolveFetcher();
            if (fetcher == null) return null;

            try
            {
                BitmapImage bmp = await fetcher
                    .FetchSmallAsync(kind, peerId, accessHash, dcId, photoId, ct)
                    .ConfigureAwait(true);
                return bmp;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                EarlyLog.Write("App.AvatarResolver",
                    "TryResolveSmallAsync threw peer=" + kind + ":" + peerId +
                    " " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Parses a "user:123" / "chat:456" / "channel:789" string into
        /// (PeerPhotoKind, peerId). Case-insensitive on the prefix;
        /// returns false on anything else.
        /// </summary>
        public static bool TryParsePeerKey(string peerKey, out PeerPhotoKind kind, out long peerId)
        {
            kind = PeerPhotoKind.User;
            peerId = 0L;
            if (string.IsNullOrEmpty(peerKey)) return false;
            int sep = peerKey.IndexOf(':');
            if (sep <= 0 || sep >= peerKey.Length - 1) return false;
            string prefix = peerKey.Substring(0, sep);
            string idText = peerKey.Substring(sep + 1);
            if (!long.TryParse(idText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out peerId)) return false;
            if (peerId <= 0) return false;
            if (string.Equals(prefix, "user", StringComparison.OrdinalIgnoreCase))
            {
                kind = PeerPhotoKind.User;
                return true;
            }
            if (string.Equals(prefix, "channel", StringComparison.OrdinalIgnoreCase))
            {
                kind = PeerPhotoKind.Channel;
                return true;
            }
            if (string.Equals(prefix, "chat", StringComparison.OrdinalIgnoreCase))
            {
                kind = PeerPhotoKind.Chat;
                return true;
            }
            return false;
        }

        // ---- internal ---------------------------------------------------

        /// <summary>
        /// Lazily build (and memoise) a single PeerAvatarFetcher for the
        /// whole process. Tries hard to share the same fetcher the
        /// ChatList uses so a photo downloaded for the chat row is
        /// already in the bitmap cache when the user opens the chat or
        /// the profile.
        /// </summary>
        private static PeerAvatarFetcher ResolveFetcher()
        {
            if (_fetcher != null) return _fetcher;
            if (_resolveFailed) return null;

            lock (_gate)
            {
                if (_fetcher != null) return _fetcher;
                if (_resolveFailed) return null;

                try
                {
                    if (App.Composition == null) { _resolveFailed = true; return null; }

                    IMediaApi media;
                    if (!App.Composition.TryResolve<IMediaApi>(out media) || media == null)
                    {
                        _resolveFailed = true;
                        return null;
                    }
                    IMediaCache cache;
                    if (!App.Composition.TryResolve<IMediaCache>(out cache) || cache == null)
                    {
                        _resolveFailed = true;
                        return null;
                    }

                    IAvatarCacheStore diskCache = null;
                    App.Composition.TryResolve<IAvatarCacheStore>(out diskCache);

                    _fetcher = new PeerAvatarFetcher(media, cache, AppLog.For("App.AvatarFetcher"), diskCache);
                    return _fetcher;
                }
                catch (Exception ex)
                {
                    EarlyLog.Write("App.AvatarResolver",
                        "ResolveFetcher threw: " + ex.GetType().Name + ": " + ex.Message);
                    _resolveFailed = true;
                    return null;
                }
            }
        }
    }
}
