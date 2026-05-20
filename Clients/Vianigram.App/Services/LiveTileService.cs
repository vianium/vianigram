// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LiveTileService.cs — Vianigram.App.Services
//
// Full chat-list-style live tile mirroring the behaviour of the official
// Telegram for Windows Phone 8.1 / Win10 Mobile client. Three tile sizes
// are kept in sync:
//
//   * Small 71×71  — paper-plane glyph + numeric badge.
//   * Medium 150×150 — wordmark + numeric badge with tooltip text.
//   * Wide 310×150 — sender name + 2-line message excerpt; up to 5
//                    most-recent unread peers cycle when the OS
//                    advances the queue.
//
// Update sources:
//   * `MessageReceived` event — every incoming message bumps the
//     queue + re-paints the tile + bumps the badge.
//   * `MessageReadByMe` / `MessagesReadByPeer` — never decrement
//     unread (read marks for our outgoing messages don't change
//     our notification surface). `MessageReadByMe` for the SAME
//     account on another device DOES decrement (a follow-up
//     for when the bus exposes the multi-session signal).
//
// Persistence: the queue is mirrored into `LocalSettings` so the
// background tasks (Vianigram.App.BackgroundTasks) can repaint the
// tile from a suspended state without re-reading the message store.
//
// All user-visible text routes through `Strings.Get` /
// `LocalizedText.Resolve` so the tile honours the active locale
// (en-US, es-ES, future ones).

using System;
using System.Collections.Generic;
using System.Threading;
using Vianigram.Composition.Infrastructure;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Messages.Domain.Events;
using Vianigram.Sync.Domain.ValueObjects;
using Windows.Data.Xml.Dom;
using Windows.Storage;
using Windows.UI.Notifications;

namespace Vianigram.App.Services
{
    public sealed class LiveTileService : IDisposable
    {
        // The OS's tile notification queue holds up to 5 frames; we
        // post every frame so the user gets a true cycling chat list.
        private const int MaxQueueSize = 5;

        // LocalSettings keys mirroring the in-memory queue so the
        // background tasks can repaint the tile from a suspended state.
        private const string SettingsKeyUnread = "vianigram.tile.unread_total";
        private const string SettingsKeyQueueCount = "vianigram.tile.queue_count";
        private const string SettingsKeyQueuePrefix = "vianigram.tile.queue.";

        private readonly IEventBus _bus;
        private readonly IPeerCache _peerCache; // null-tolerant
        private readonly IComponentLogger _log;
        private readonly IDisposable[] _subs;
        private readonly LinkedList<TileEntry> _recent = new LinkedList<TileEntry>();
        private readonly object _gate = new object();
        private int _totalUnread;
        private int _disposed;

        private struct TileEntry
        {
            public string PeerName;
            public string Body;
            public DateTime At;
        }

        public LiveTileService(IEventBus bus, IPeerCache peerCache, IComponentLogger log)
        {
            if (bus == null) throw new ArgumentNullException("bus");
            _bus = bus;
            _peerCache = peerCache;
            _log = log;

            // The OS by default replaces the previous tile XML on each
            // Update call. EnableNotificationQueue lets us keep up to 5
            // and the OS auto-cycles between them — that's the cycling
            // chat behaviour the official Telegram WP client used.
            try
            {
                TileUpdateManager.CreateTileUpdaterForApplication().EnableNotificationQueue(true);
            }
            catch { /* already enabled / not supported */ }

            _subs = new IDisposable[]
            {
                bus.Subscribe<MessageReceived>(OnMessageReceived),
                bus.Subscribe<MessageReadByMe>(OnMessageReadByMe),
            };

            // Re-hydrate from LocalSettings so a cold start doesn't
            // wipe the live tile until the next message arrives.
            TryHydrateFromSettings();
            RepaintAll();
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (_subs == null) return;
            for (int i = 0; i < _subs.Length; i++)
            {
                try { if (_subs[i] != null) _subs[i].Dispose(); }
                catch { }
            }
        }

        /// <summary>
        /// Wipe the tile to its zero-state. Called on logout so the
        /// next account doesn't inherit the previous user's tile content.
        /// </summary>
        public void Clear()
        {
            lock (_gate)
            {
                _recent.Clear();
                _totalUnread = 0;
            }
            try
            {
                ApplicationData.Current.LocalSettings.Values.Remove(SettingsKeyUnread);
                ApplicationData.Current.LocalSettings.Values.Remove(SettingsKeyQueueCount);
                for (int i = 0; i < MaxQueueSize; i++)
                {
                    ApplicationData.Current.LocalSettings.Values.Remove(SettingsKeyQueuePrefix + i);
                }
            }
            catch { }
            try
            {
                TileUpdateManager.CreateTileUpdaterForApplication().Clear();
                BadgeUpdateManager.CreateBadgeUpdaterForApplication().Clear();
            }
            catch { }
        }

        // --------------------------------------------------------------
        // Bus handlers
        // --------------------------------------------------------------

        private void OnMessageReceived(MessageReceived e)
        {
            if (e == null || string.IsNullOrEmpty(e.PeerKey)) return;
            // We don't want our own outgoing messages to raise badges
            // or paint the tile.
            if (e.IsOutgoing) return;

            string peerName = ResolvePeerName(e.PeerKey, e.FromUserId);
            string body = LocalizedText.Resolve(e.Body);
            if (string.IsNullOrEmpty(body)) body = Strings.Get("Notif.Body.Generic");

            lock (_gate)
            {
                _recent.AddFirst(new TileEntry
                {
                    PeerName = peerName,
                    Body = body,
                    At = e.At == default(DateTime) ? DateTime.UtcNow : e.At
                });
                while (_recent.Count > MaxQueueSize) _recent.RemoveLast();
                _totalUnread++;
                if (_totalUnread > 999) _totalUnread = 999;
            }

            PersistQueue();
            RepaintAll();
        }

        private void OnMessageReadByMe(MessageReadByMe e)
        {
            if (e == null) return;
            // The official client clears the tile when the user reads
            // the conversation on this device. We can't know which
            // queued entries to drop without per-peer message-id
            // tracking, so we use a heuristic: any read event
            // decrements the unread total to zero (matches the WP UX
            // of "you opened the chat, the tile resets").
            bool changed;
            lock (_gate)
            {
                changed = _totalUnread > 0;
                _totalUnread = 0;
                _recent.Clear();
            }
            if (changed)
            {
                PersistQueue();
                RepaintAll();
            }
        }

        // --------------------------------------------------------------
        // Tile painting
        // --------------------------------------------------------------

        private void RepaintAll()
        {
            try
            {
                PaintTile();
                PaintBadge();
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn("tile.paint threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void PaintTile()
        {
            TileEntry[] frames;
            int unread;
            lock (_gate)
            {
                frames = new TileEntry[_recent.Count];
                int i = 0;
                foreach (var e in _recent) frames[i++] = e;
                unread = _totalUnread;
            }

            var updater = TileUpdateManager.CreateTileUpdaterForApplication();
            updater.Clear();
            if (frames.Length == 0)
            {
                updater.Update(new TileNotification(BuildZeroStateTileXml()));
                return;
            }

            // Up to MaxQueueSize frames, newest-first.
            for (int i = 0; i < frames.Length; i++)
            {
                updater.Update(new TileNotification(BuildFrameTileXml(frames[i], unread)));
            }
        }

        private void PaintBadge()
        {
            int unread;
            lock (_gate) unread = _totalUnread;
            var doc = new XmlDocument();
            if (unread <= 0)
            {
                doc.LoadXml("<badge value=\"none\"/>");
            }
            else
            {
                int clamped = unread > 99 ? 99 : unread;
                doc.LoadXml("<badge value=\"" +
                    clamped.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "\"/>");
            }
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(new BadgeNotification(doc));
        }

        private static XmlDocument BuildZeroStateTileXml()
        {
            // TileWide310x150Text03: header line + 4 wrap lines.
            // Square150x150Text04: 4 wrap lines.
            // Square71x71IconWithBadge has no text — just the badge.
            string header = Strings.Get("Tile.Header");
            string subtitle = Strings.Get("Tile.ZeroSubtitle");
            string xml =
                "<tile>" +
                  "<visual version='2'>" +
                    "<binding template='TileWide310x150Text03' fallback='TileWideText03'>" +
                      "<text id='1'>" + Escape(header) + "</text>" +
                      "<text id='2'>" + Escape(subtitle) + "</text>" +
                    "</binding>" +
                    "<binding template='TileSquare150x150Text04' fallback='TileSquareText04'>" +
                      "<text id='1'>" + Escape(subtitle) + "</text>" +
                    "</binding>" +
                  "</visual>" +
                "</tile>";
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }

        private static XmlDocument BuildFrameTileXml(TileEntry entry, int unread)
        {
            // TileWide310x150Text03: header (peer) + 4 lines for body.
            // TileSquare150x150Text04: just the body excerpt.
            string header = entry.PeerName ?? Strings.Get("Tile.Header");
            string body = entry.Body ?? string.Empty;
            string summary = unread <= 1
                ? Strings.Get("Tile.UnreadOne")
                : string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Strings.Get("Tile.UnreadMany"),
                    unread.ToString(System.Globalization.CultureInfo.InvariantCulture));

            string xml =
                "<tile>" +
                  "<visual version='2'>" +
                    "<binding template='TileWide310x150Text03' fallback='TileWideText03'>" +
                      "<text id='1'>" + Escape(header) + "</text>" +
                      "<text id='2'>" + Escape(body) + "</text>" +
                      "<text id='3'></text>" +
                      "<text id='4'>" + Escape(summary) + "</text>" +
                    "</binding>" +
                    "<binding template='TileSquare150x150Text04' fallback='TileSquareText04'>" +
                      "<text id='1'>" + Escape(header) + ": " + Escape(body) + "</text>" +
                    "</binding>" +
                  "</visual>" +
                "</tile>";
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc;
        }

        // --------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------

        private string ResolvePeerName(string peerKey, long fromUserId)
        {
            if (_peerCache == null) return peerKey ?? string.Empty;
            string kind;
            long id;
            if (!PeerKey.TryParse(peerKey ?? string.Empty, out kind, out id)) return peerKey;
            try
            {
                if (string.Equals(kind, "user", StringComparison.Ordinal))
                {
                    string name = _peerCache.GetUserDisplayName(id);
                    if (!string.IsNullOrEmpty(name)) return name;
                }
                else if (string.Equals(kind, "chat", StringComparison.Ordinal) ||
                         string.Equals(kind, "channel", StringComparison.Ordinal))
                {
                    string title = _peerCache.GetChatTitle(id);
                    if (!string.IsNullOrEmpty(title))
                    {
                        // For groups/channels add the sender if known
                        // so the wide tile reads "Group · Sender".
                        if (fromUserId != 0L)
                        {
                            string sender = _peerCache.GetUserDisplayName(fromUserId);
                            if (!string.IsNullOrEmpty(sender)) return title + " · " + sender;
                        }
                        return title;
                    }
                }
            }
            catch { }
            // Last-resort friendly fallback.
            if (string.Equals(kind, "channel", StringComparison.Ordinal)) return Strings.Get("Notif.Title.Channel");
            if (string.Equals(kind, "chat", StringComparison.Ordinal)) return Strings.Get("Notif.Title.GroupChat");
            return Strings.Get("Notif.Title.DirectMessage");
        }

        private static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // Strip control chars (ToastNotification XML rejects them)
            // and escape XML special characters.
            var sb = new System.Text.StringBuilder(s.Length + 8);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c < 0x20 && c != '\n' && c != '\r' && c != '\t') continue;
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '\'': sb.Append("&apos;"); break;
                    case '"': sb.Append("&quot;"); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        // --------------------------------------------------------------
        // LocalSettings persistence (background-task readable)
        // --------------------------------------------------------------

        private void TryHydrateFromSettings()
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                object raw;
                if (values.TryGetValue(SettingsKeyUnread, out raw))
                {
                    try { _totalUnread = Convert.ToInt32(raw); }
                    catch { _totalUnread = 0; }
                }
                if (values.TryGetValue(SettingsKeyQueueCount, out raw))
                {
                    int n;
                    try { n = Convert.ToInt32(raw); }
                    catch { n = 0; }
                    if (n > MaxQueueSize) n = MaxQueueSize;
                    for (int i = 0; i < n; i++)
                    {
                        object encoded;
                        if (!values.TryGetValue(SettingsKeyQueuePrefix + i, out encoded)) continue;
                        string blob = encoded as string;
                        if (string.IsNullOrEmpty(blob)) continue;
                        // Format: "PeerNameBodyTicks"
                        int s1 = blob.IndexOf('');
                        if (s1 < 0) continue;
                        int s2 = blob.IndexOf('', s1 + 1);
                        if (s2 < 0) continue;
                        long ticks;
                        if (!long.TryParse(blob.Substring(s2 + 1), out ticks)) ticks = DateTime.UtcNow.Ticks;
                        _recent.AddLast(new TileEntry
                        {
                            PeerName = blob.Substring(0, s1),
                            Body = blob.Substring(s1 + 1, s2 - s1 - 1),
                            At = new DateTime(ticks, DateTimeKind.Utc)
                        });
                    }
                }
            }
            catch { /* hydration is best-effort */ }
        }

        private void PersistQueue()
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                int unread; TileEntry[] snapshot;
                lock (_gate)
                {
                    unread = _totalUnread;
                    snapshot = new TileEntry[_recent.Count];
                    int i = 0;
                    foreach (var e in _recent) snapshot[i++] = e;
                }
                values[SettingsKeyUnread] = unread;
                values[SettingsKeyQueueCount] = snapshot.Length;
                for (int i = 0; i < snapshot.Length; i++)
                {
                    string encoded = (snapshot[i].PeerName ?? string.Empty) + "" +
                                      (snapshot[i].Body ?? string.Empty) + "" +
                                      snapshot[i].At.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    values[SettingsKeyQueuePrefix + i] = encoded;
                }
                // Clear stale slots beyond the new count.
                for (int i = snapshot.Length; i < MaxQueueSize; i++)
                {
                    values.Remove(SettingsKeyQueuePrefix + i);
                }
            }
            catch { /* persistence is best-effort */ }
        }
    }
}
