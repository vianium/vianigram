// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Domain.Entities
{
    /// <summary>
    /// One sticker (document) within a <see cref="StickerSet"/>. Identity is by
    /// <see cref="DocumentId"/>.
    ///
    /// Carries the canonical TL surface for an individual sticker:
    ///   * <see cref="DocumentId"/> + <see cref="AccessHash"/> — TL InputDocument identity.
    ///   * <see cref="FileReference"/> — short-lived blob the server requires
    ///     for upload.getFile; refreshed by re-fetching the parent set.
    ///   * <see cref="Emoji"/> — primary emoji shortcode, used by typed-emoji
    ///     suggestions.
    ///   * <see cref="OwnerSetId"/> — back-reference to the parent set so
    ///     callers can locate the cache key for the blob.
    ///   * <see cref="Width"/> / <see cref="Height"/> — pixel dimensions for
    ///     thumbnail layout (zero when the server omitted them).
    ///
    /// Mutable in narrow ways (see <see cref="ApplyServerUpdate"/>) so the
    /// owning set can refresh metadata without re-allocating the entity. The
    /// document id and access hash are NOT mutated post-construction; the file
    /// reference is the only short-lived field.
    /// </summary>
    public sealed class Sticker
    {
        private readonly StickerId _id;
        private readonly StickerSetId _ownerSetId;
        private byte[] _fileReference;
        private StickerEmoji _emoji;
        private int _width;
        private int _height;

        public Sticker(
            StickerId id,
            StickerSetId ownerSetId,
            byte[] fileReference,
            StickerEmoji emoji,
            int width,
            int height)
        {
            _id = id;
            _ownerSetId = ownerSetId;
            _fileReference = fileReference ?? new byte[0];
            _emoji = emoji ?? StickerEmoji.Empty;
            _width = width < 0 ? 0 : width;
            _height = height < 0 ? 0 : height;
        }

        public StickerId Id { get { return _id; } }
        public long DocumentId { get { return _id.Value; } }
        public long AccessHash { get { return _id.AccessHash; } }
        public StickerSetId OwnerSetId { get { return _ownerSetId; } }
        public byte[] FileReference { get { return _fileReference; } }
        public StickerEmoji Emoji { get { return _emoji; } }
        public int Width { get { return _width; } }
        public int Height { get { return _height; } }

        /// <summary>
        /// Cache key for the decoded blob owned by this sticker. Composed from
        /// the (set, document) pair so eviction on uninstall can use a single
        /// pack-wide call.
        /// </summary>
        public StickerCacheKey CacheKey
        {
            get { return new StickerCacheKey(_ownerSetId, _id); }
        }

        /// <summary>
        /// In-place refresh from a server fetch of the parent set. Returns
        /// true iff any observable field changed. The aggregate uses the
        /// return value to decide whether to stage a domain event.
        /// </summary>
        public bool ApplyServerUpdate(byte[] fileReference, StickerEmoji emoji, int width, int height)
        {
            bool changed = false;
            byte[] fr = fileReference ?? new byte[0];
            StickerEmoji em = emoji ?? StickerEmoji.Empty;
            int w = width < 0 ? 0 : width;
            int h = height < 0 ? 0 : height;

            if (!ByteArrayEquals(_fileReference, fr)) { _fileReference = fr; changed = true; }
            if (!_emoji.Equals(em)) { _emoji = em; changed = true; }
            if (_width != w) { _width = w; changed = true; }
            if (_height != h) { _height = h; changed = true; }
            return changed;
        }

        private static bool ByteArrayEquals(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i]) return false;
            }
            return true;
        }
    }
}
