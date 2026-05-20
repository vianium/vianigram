// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Stickers.Domain.Events;
using Vianigram.Stickers.Domain.ValueObjects;

namespace Vianigram.Stickers.Domain.Entities
{
    /// <summary>
    /// Aggregate root for the user's sticker library.
    ///
    /// Owns three collections plus a sync timestamp:
    ///   * <c>installed</c> — keyed by <see cref="StickerSetId"/>, in the
    ///     server-supplied order (the order the user dragged them in the
    ///     stickers panel).
    ///   * <c>recent</c> — recently-used <see cref="StickerId"/> list, MRU at
    ///     index 0. Capped at <see cref="MaxRecent"/>.
    ///   * <c>favorites</c> — favorited <see cref="StickerId"/> list. Capped at
    ///     <see cref="MaxFavorites"/>.
    ///   * <c>lastSyncAt</c> — when the last successful
    ///     <c>messages.getAllStickers</c> completed; used to short-circuit
    ///     redundant syncs.
    ///
    /// Mutators stage <see cref="IDomainEvent"/> instances on a pending list so
    /// the handler / repository can drain them after the persistence write
    /// succeeds. This keeps the aggregate dependency-free and makes events
    /// transactional with the state change.
    /// </summary>
    public sealed class StickerLibrary
    {
        /// <summary>Recently-used cap (Telegram client convention).</summary>
        public const int MaxRecent = 20;

        /// <summary>Favorites cap (Telegram client convention).</summary>
        public const int MaxFavorites = 100;

        /// <summary>Maximum installed packs (Telegram server enforces).</summary>
        public const int MaxInstalledSets = 200;

        private readonly Dictionary<long, StickerSet> _installed;
        private readonly List<long> _installedOrder;
        private readonly List<StickerId> _recent;
        private readonly List<StickerId> _favorites;
        private readonly List<IDomainEvent> _pending;
        private DateTime _lastSyncAt;
        private long _lastHash;

        public StickerLibrary()
        {
            _installed = new Dictionary<long, StickerSet>();
            _installedOrder = new List<long>();
            _recent = new List<StickerId>(MaxRecent);
            _favorites = new List<StickerId>(MaxFavorites);
            _pending = new List<IDomainEvent>(8);
            _lastSyncAt = DateTime.MinValue;
            _lastHash = 0L;
        }

        public DateTime LastSyncAt { get { return _lastSyncAt; } }
        public long LastHash { get { return _lastHash; } }
        public int InstalledCount { get { return _installed.Count; } }
        public int RecentCount { get { return _recent.Count; } }
        public int FavoriteCount { get { return _favorites.Count; } }

        public IList<StickerSet> InstalledSnapshot()
        {
            var list = new List<StickerSet>(_installedOrder.Count);
            for (int i = 0; i < _installedOrder.Count; i++)
            {
                StickerSet s;
                if (_installed.TryGetValue(_installedOrder[i], out s) && s != null) list.Add(s);
            }
            return list;
        }

        public IList<StickerId> RecentSnapshot()
        {
            var list = new List<StickerId>(_recent.Count);
            for (int i = 0; i < _recent.Count; i++) list.Add(_recent[i]);
            return list;
        }

        public IList<StickerId> FavoritesSnapshot()
        {
            var list = new List<StickerId>(_favorites.Count);
            for (int i = 0; i < _favorites.Count; i++) list.Add(_favorites[i]);
            return list;
        }

        public StickerSet Find(StickerSetId id)
        {
            StickerSet set;
            _installed.TryGetValue(id.Value, out set);
            return set;
        }

        public bool IsInstalled(StickerSetId id)
        {
            return _installed.ContainsKey(id.Value);
        }

        public bool IsFavorited(StickerId id)
        {
            for (int i = 0; i < _favorites.Count; i++)
            {
                if (_favorites[i].Value == id.Value) return true;
            }
            return false;
        }

        /// <summary>
        /// Replace the installed-sets collection with the freshly synced list.
        /// Each row is matched against the current state and either upserted
        /// (with <c>StickerSetInstalled</c> emitted only for new entries) or
        /// metadata-refreshed in place. Existing entries that are absent from
        /// <paramref name="incoming"/> are removed and emit
        /// <see cref="StickerSetUninstalled"/>.
        /// </summary>
        public void ApplyServerSync(IList<StickerSet> incoming, long hash, DateTime at)
        {
            if (incoming == null) incoming = new StickerSet[0];

            var seen = new HashSet<long>();
            var nextOrder = new List<long>(incoming.Count);
            for (int i = 0; i < incoming.Count; i++)
            {
                StickerSet next = incoming[i];
                if (next == null) continue;
                seen.Add(next.Id.Value);
                nextOrder.Add(next.Id.Value);

                StickerSet existing;
                if (_installed.TryGetValue(next.Id.Value, out existing))
                {
                    existing.ApplyServerUpdate(
                        next.Title, next.ShortName, next.Count, next.Hash,
                        next.IsOfficial, next.IsAnimated, next.IsMasks, next.IsVideos);
                    // No event for in-place metadata refresh; subscribers re-read on Sync.
                }
                else
                {
                    _installed[next.Id.Value] = next;
                    Stage(new StickerSetInstalled(next.Id, at));
                }
            }

            // Remove dropped sets.
            var toRemove = new List<long>();
            foreach (var kv in _installed)
            {
                if (!seen.Contains(kv.Key)) toRemove.Add(kv.Key);
            }
            for (int i = 0; i < toRemove.Count; i++)
            {
                StickerSet dropped;
                _installed.TryGetValue(toRemove[i], out dropped);
                _installed.Remove(toRemove[i]);
                if (dropped != null)
                {
                    Stage(new StickerSetUninstalled(dropped.Id, at));
                }
            }

            // Detect order change.
            bool orderChanged = nextOrder.Count != _installedOrder.Count;
            if (!orderChanged)
            {
                for (int i = 0; i < nextOrder.Count; i++)
                {
                    if (nextOrder[i] != _installedOrder[i]) { orderChanged = true; break; }
                }
            }
            _installedOrder.Clear();
            _installedOrder.AddRange(nextOrder);
            if (orderChanged) Stage(new StickerSetReordered(at));

            _lastHash = hash;
            _lastSyncAt = at;
            Stage(new StickersSynced(_installed.Count, at));
        }

        /// <summary>
        /// Add or refresh a single set (used by install / get-set flows). If
        /// the set is new, stages a <see cref="StickerSetInstalled"/> event.
        /// </summary>
        public void AddOrUpdate(StickerSet set, DateTime at)
        {
            if (set == null) throw new ArgumentNullException("set");
            StickerSet existing;
            if (_installed.TryGetValue(set.Id.Value, out existing))
            {
                existing.ApplyServerUpdate(
                    set.Title, set.ShortName, set.Count, set.Hash,
                    set.IsOfficial, set.IsAnimated, set.IsMasks, set.IsVideos);
                if (set.IsContentLoaded)
                {
                    existing.ReplaceContent(set.Stickers);
                }
            }
            else
            {
                _installed[set.Id.Value] = set;
                _installedOrder.Add(set.Id.Value);
                Stage(new StickerSetInstalled(set.Id, at));
            }
        }

        /// <summary>
        /// Update the loaded content of an already-installed set. Returns
        /// false if the set is not installed (callers should treat that as a
        /// not-found error).
        /// </summary>
        public bool UpdateSetContent(StickerSetId id, IList<Sticker> stickers, DateTime at)
        {
            StickerSet existing;
            if (!_installed.TryGetValue(id.Value, out existing)) return false;
            existing.ReplaceContent(stickers);
            return true;
        }

        public void Uninstall(StickerSetId id, DateTime at)
        {
            StickerSet existing;
            if (!_installed.TryGetValue(id.Value, out existing)) return;
            _installed.Remove(id.Value);
            _installedOrder.Remove(id.Value);
            Stage(new StickerSetUninstalled(id, at));
        }

        /// <summary>
        /// Bump a sticker to the top of the MRU list. Existing entries are
        /// removed first (no duplicates). When the list exceeds
        /// <see cref="MaxRecent"/>, the oldest entry is dropped.
        /// </summary>
        public void BumpRecent(StickerId id, DateTime at)
        {
            for (int i = 0; i < _recent.Count; i++)
            {
                if (_recent[i].Value == id.Value)
                {
                    _recent.RemoveAt(i);
                    break;
                }
            }
            _recent.Insert(0, id);
            if (_recent.Count > MaxRecent)
            {
                _recent.RemoveAt(_recent.Count - 1);
            }
            Stage(new StickerUsedRecently(id, at));
        }

        /// <summary>
        /// Replace the recent list wholesale (used after a server-side fetch
        /// of <c>messages.getRecentStickers</c>). Does NOT stage individual
        /// per-entry events; the caller decides whether to publish a single
        /// summary event.
        /// </summary>
        public void ReplaceRecent(IList<StickerId> incoming)
        {
            _recent.Clear();
            if (incoming == null) return;
            for (int i = 0; i < incoming.Count && i < MaxRecent; i++)
            {
                _recent.Add(incoming[i]);
            }
        }

        /// <summary>
        /// Toggle favorite for a sticker. Returns true if the favorite state
        /// transitioned, false if it was already in the requested state. When
        /// the favorites cap is reached, returns false without mutating.
        /// </summary>
        public bool SetFavorite(StickerId id, bool favored, DateTime at)
        {
            int idx = -1;
            for (int i = 0; i < _favorites.Count; i++)
            {
                if (_favorites[i].Value == id.Value) { idx = i; break; }
            }

            if (favored)
            {
                if (idx >= 0) return false;
                if (_favorites.Count >= MaxFavorites) return false;
                _favorites.Insert(0, id);
                Stage(new StickerFavorited(id, true, at));
                return true;
            }
            else
            {
                if (idx < 0) return false;
                _favorites.RemoveAt(idx);
                Stage(new StickerFavorited(id, false, at));
                return true;
            }
        }

        /// <summary>Drain pending domain events for the handler to publish post-persistence.</summary>
        public IList<IDomainEvent> DequeuePendingEvents()
        {
            if (_pending.Count == 0) return new IDomainEvent[0];
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        private void Stage(IDomainEvent evt)
        {
            _pending.Add(evt);
        }
    }
}
