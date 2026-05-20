// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Domain.Entities
{
    /// <summary>
    /// One installed sticker pack (set). Identity is by <see cref="Id"/>.
    ///
    /// Carries the metadata Telegram exposes via the messages.stickerSet TL
    /// envelope:
    ///   * <see cref="Title"/>, <see cref="ShortName"/> — display + canonical name.
    ///   * <see cref="Count"/> — sticker count reported by the server.
    ///   * <see cref="Hash"/> — set-content hash used by getStickerSet not_modified.
    ///   * <see cref="IsOfficial"/> — server flag for editorial packs.
    ///   * <see cref="IsAnimated"/> — TGS pack (Lottie animation).
    ///   * <see cref="IsMasks"/> — face-mask pack.
    ///   * <see cref="IsVideos"/> — WebM video pack.
    ///   * <see cref="Stickers"/> — empty until the pack content is loaded
    ///     (lazy).
    ///
    /// Mutable in narrow ways: <see cref="ApplyServerUpdate"/> refreshes
    /// metadata, and <see cref="ReplaceContent"/> swaps the sticker collection
    /// when the parent aggregate fetches the pack body.
    /// </summary>
    public sealed class StickerSet
    {
        private readonly StickerSetId _id;
        private string _title;
        private string _shortName;
        private int _count;
        private long _hash;
        private bool _isOfficial;
        private bool _isAnimated;
        private bool _isMasks;
        private bool _isVideos;
        private readonly List<Sticker> _stickers;

        public StickerSet(
            StickerSetId id,
            string title,
            string shortName,
            int count,
            long hash,
            bool isOfficial,
            bool isAnimated,
            bool isMasks,
            bool isVideos)
        {
            _id = id;
            _title = title ?? string.Empty;
            _shortName = shortName ?? string.Empty;
            _count = count < 0 ? 0 : count;
            _hash = hash;
            _isOfficial = isOfficial;
            _isAnimated = isAnimated;
            _isMasks = isMasks;
            _isVideos = isVideos;
            _stickers = new List<Sticker>(count > 0 ? count : 4);
        }

        public StickerSetId Id { get { return _id; } }
        public string Title { get { return _title; } }
        public string ShortName { get { return _shortName; } }
        public int Count { get { return _count; } }
        public long Hash { get { return _hash; } }
        public bool IsOfficial { get { return _isOfficial; } }
        public bool IsAnimated { get { return _isAnimated; } }
        public bool IsMasks { get { return _isMasks; } }
        public bool IsVideos { get { return _isVideos; } }

        /// <summary>
        /// Loaded sticker entities. Empty until the first
        /// <c>messages.getStickerSet</c> populates the body. Callers should
        /// treat the returned list as read-only — mutation goes through the
        /// aggregate root.
        /// </summary>
        public IList<Sticker> Stickers
        {
            get
            {
                var copy = new List<Sticker>(_stickers.Count);
                for (int i = 0; i < _stickers.Count; i++) copy.Add(_stickers[i]);
                return copy;
            }
        }

        /// <summary>True iff the body has been loaded at least once.</summary>
        public bool IsContentLoaded { get { return _stickers.Count > 0; } }

        /// <summary>
        /// Bulk in-place refresh of metadata from a server response. Returns
        /// true iff any observable field changed.
        /// </summary>
        public bool ApplyServerUpdate(
            string title,
            string shortName,
            int count,
            long hash,
            bool isOfficial,
            bool isAnimated,
            bool isMasks,
            bool isVideos)
        {
            bool changed = false;
            string t = title ?? string.Empty;
            string sn = shortName ?? string.Empty;
            int c = count < 0 ? 0 : count;

            if (!string.Equals(_title, t, StringComparison.Ordinal)) { _title = t; changed = true; }
            if (!string.Equals(_shortName, sn, StringComparison.Ordinal)) { _shortName = sn; changed = true; }
            if (_count != c) { _count = c; changed = true; }
            if (_hash != hash) { _hash = hash; changed = true; }
            if (_isOfficial != isOfficial) { _isOfficial = isOfficial; changed = true; }
            if (_isAnimated != isAnimated) { _isAnimated = isAnimated; changed = true; }
            if (_isMasks != isMasks) { _isMasks = isMasks; changed = true; }
            if (_isVideos != isVideos) { _isVideos = isVideos; changed = true; }
            return changed;
        }

        /// <summary>
        /// Replace the loaded sticker collection wholesale (used after a
        /// successful <c>messages.getStickerSet</c>). The aggregate is
        /// responsible for staging the corresponding domain event.
        /// </summary>
        internal void ReplaceContent(IList<Sticker> stickers)
        {
            _stickers.Clear();
            if (stickers == null) return;
            for (int i = 0; i < stickers.Count; i++)
            {
                Sticker s = stickers[i];
                if (s != null) _stickers.Add(s);
            }
        }
    }
}
