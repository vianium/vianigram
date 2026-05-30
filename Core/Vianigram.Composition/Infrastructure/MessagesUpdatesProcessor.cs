// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MessagesUpdatesProcessor.cs — Vianigram.Composition.Infrastructure
//
// Single-subscription model.
//
// Previously this class subscribed directly to IUpdatesPort and decoded
// TL inline, duplicating Sync.Infrastructure.TlDecoder badly (it only
// extracted peer + message_id, dropping body / typing / status / read
// state entirely). That violated principle M6 ("only Sync subscribes to
// IUpdatesPort"), produced visible reordering on getDifference gaps, and
// forced the chat page to reload the entire conversation on every push.
//
// Now: this class subscribes to the IEventBus events Sync emits AFTER
// it has applied the cursor (RemoteMessageReceived, RemoteMessageDeleted,
// RemoteMessageRead, RemoteUserStatusChanged, RemoteUserTypingChanged).
// It translates each Sync DTO into the corresponding Messages /
// Notifications domain event so the existing MessagesApplication +
// ChatPage / ChatList wiring keeps working — but with full payload
// (body, isOnline, action label) so the UI can append a bubble in place
// or flip the typing indicator without an extra round-trip.
//
// The class name "UpdatesProcessor" stays for log-grep continuity even
// though it no longer touches raw TL bytes; semantically it's now the
// "Sync→Messages bridge".

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Messages.Domain.Events;
using Vianigram.Sync.Domain.Events;
using Vianigram.Sync.Domain.ValueObjects;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Bridges Sync's typed Remote* events to Messages domain events so
    /// the existing IMessagesApi.MessagesChanged surface lights up with
    /// full payloads. One instance per app lifetime; held by the
    /// composition root.
    /// </summary>
    public sealed class MessagesUpdatesProcessor : IDisposable
    {
        private static readonly TimeSpan RecentReceivedTtl = TimeSpan.FromMinutes(2);
        private const int RecentReceivedMaxEntries = 512;

        private readonly IEventBus _bus;
        private readonly IDisposable[] _subs;
        private readonly object _recentReceivedGate = new object();
        private readonly Dictionary<string, DateTime> _recentReceived =
            new Dictionary<string, DateTime>(StringComparer.Ordinal);
        private int _disposed;

        public MessagesUpdatesProcessor(IEventBus bus)
        {
            if (bus == null) throw new ArgumentNullException("bus");
            _bus = bus;

            _subs = new IDisposable[]
            {
                bus.Subscribe<RemoteMessageReceived>(OnRemoteMessageReceived),
                bus.Subscribe<RemoteMessageEdited>(OnRemoteMessageEdited),
                bus.Subscribe<RemoteMessageDeleted>(OnRemoteMessageDeleted),
                bus.Subscribe<RemoteMessageRead>(OnRemoteMessageRead),
                bus.Subscribe<RemoteUserStatusChanged>(OnRemoteUserStatus),
                bus.Subscribe<RemoteUserTypingChanged>(OnRemoteUserTyping),
                // A reaction set changed on a message.
                // We don't decode the full MessageReactions sub-tree (it
                // requires the entire schema), so we just project this
                // as a "message changed" event that triggers a partial
                // reload in ChatPage. When bubble templates eventually
                // render reactions natively, this bridge can be upgraded
                // to surface the emoji aggregation directly.
                bus.Subscribe<RemoteMessageReactionsChanged>(OnRemoteReactionsChanged)
            };
            EarlyLog.Write("MessagesUpdates",
                "subscribed to Sync.Remote* events (single-subscription model)");
        }

        public void Dispose()
        {
            if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0) return;
            for (int i = 0; i < _subs.Length; i++)
            {
                try { if (_subs[i] != null) _subs[i].Dispose(); }
                catch { }
            }
        }

        // -----------------------------------------------------------------
        // Sync → Messages bridge handlers
        // -----------------------------------------------------------------

        private void OnRemoteMessageReceived(RemoteMessageReceived e)
        {
            if (e == null || e.Message == null)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteMessageReceived dropped (null event/message)");
                return;
            }
            try
            {
                MessageDto m = e.Message;
                // Convert Telegram's int32-seconds-since-epoch to UTC.
                DateTime at = FromUnixSeconds(m.Date);
                if (ShouldDropDuplicateReceived(e.PeerKey, m.Id, e.TimestampUtc))
                {
                    EarlyLog.Write("MessagesUpdates",
                        "bridge duplicate MessageReceived dropped peer=" +
                        (e.PeerKey ?? "?") + " id=" + m.Id);
                    return;
                }

                EarlyLog.Write("MessagesUpdates",
                    "bridge MessageReceived peer=" + (e.PeerKey ?? "?") +
                    " id=" + m.Id +
                    " out=" + m.IsOutgoing +
                    " body_len=" + (m.Message == null ? 0 : m.Message.Length));
                _bus.Publish(new MessageReceived(
                    e.PeerKey,
                    m.Id,
                    at,
                    m.IsOutgoing,
                    e.TimestampUtc,
                    m.FromUserId,
                    m.Message));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteMessageReceived bridge threw " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private bool ShouldDropDuplicateReceived(string peerKey, int messageId, DateTime timestampUtc)
        {
            if (messageId <= 0) return false;

            string key = (peerKey ?? string.Empty) + "#" +
                messageId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            DateTime now = timestampUtc.Kind == DateTimeKind.Utc
                ? timestampUtc
                : DateTime.UtcNow;

            lock (_recentReceivedGate)
            {
                DateTime previous;
                if (_recentReceived.TryGetValue(key, out previous) &&
                    now - previous <= RecentReceivedTtl)
                {
                    return true;
                }

                _recentReceived[key] = now;
                if (_recentReceived.Count > RecentReceivedMaxEntries)
                {
                    PruneRecentReceived(now);
                }
            }

            return false;
        }

        private void PruneRecentReceived(DateTime nowUtc)
        {
            var expired = new List<string>();
            foreach (var kv in _recentReceived)
            {
                if (nowUtc - kv.Value > RecentReceivedTtl)
                {
                    expired.Add(kv.Key);
                }
            }

            for (int i = 0; i < expired.Count; i++)
            {
                _recentReceived.Remove(expired[i]);
            }

            if (_recentReceived.Count <= RecentReceivedMaxEntries) return;

            int excess = _recentReceived.Count - RecentReceivedMaxEntries;
            expired.Clear();
            foreach (var kv in _recentReceived)
            {
                expired.Add(kv.Key);
                if (expired.Count >= excess) break;
            }

            for (int i = 0; i < expired.Count; i++)
            {
                _recentReceived.Remove(expired[i]);
            }
        }

        private void OnRemoteMessageEdited(RemoteMessageEdited e)
        {
            if (e == null || e.Message == null) return;
            try
            {
                MessageDto m = e.Message;
                DateTime at = FromUnixSeconds(m.Date);
                // Surface the new body so ChatPage can rewrite the bubble
                // in place without a ReloadAsync. Empty body falls through
                // to the legacy reload path (caption edits, service messages).
                _bus.Publish(new MessageEdited(e.PeerKey, m.Id, at, e.TimestampUtc, m.Message));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteMessageEdited bridge threw " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnRemoteMessageDeleted(RemoteMessageDeleted e)
        {
            if (e == null || e.MessageIds == null) return;
            try
            {
                for (int i = 0; i < e.MessageIds.Count; i++)
                {
                    int id = e.MessageIds[i];
                    if (id <= 0) continue;
                    _bus.Publish(new MessageDeleted(e.PeerKey, id, e.TimestampUtc));
                }
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteMessageDeleted bridge threw " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnRemoteMessageRead(RemoteMessageRead e)
        {
            if (e == null) return;
            try
            {
                if (e.ByMe)
                {
                    // We read the peer's messages on another logged-in
                    // session of ours — advance the local read cursor.
                    _bus.Publish(new MessageReadByMe(
                        e.PeerKey, e.UpToMessageId, e.TimestampUtc));
                }
                else
                {
                    // The peer read OUR messages — advance the "✓✓" seal.
                    _bus.Publish(new MessagesReadByPeer(
                        e.PeerKey, e.UpToMessageId, e.TimestampUtc));
                }
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteMessageRead bridge threw " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnRemoteUserStatus(RemoteUserStatusChanged e)
        {
            if (e == null) return;
            try
            {
                bool isOnline = e.Status == UserStatusKind.Online;
                _bus.Publish(new PeerStatusChanged(
                    e.UserId, isOnline, e.WasOnlineUtc, e.TimestampUtc));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteUserStatusChanged bridge threw " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnRemoteReactionsChanged(RemoteMessageReactionsChanged e)
        {
            if (e == null) return;
            try
            {
                // Project as a body-less MessageEdited — the ChatPage VM
                // falls back to ReloadAsync when the body is empty, which
                // is exactly what we want here (refetch the message so
                // any future reactions surface eats new server state).
                _bus.Publish(new MessageEdited(
                    e.PeerKey, e.MessageId, e.TimestampUtc, e.TimestampUtc));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteMessageReactionsChanged bridge threw " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnRemoteUserTyping(RemoteUserTypingChanged e)
        {
            if (e == null) return;
            try
            {
                _bus.Publish(new PeerTypingChanged(
                    e.PeerKey, e.UserId, e.Action.ToString(), e.TimestampUtc));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("MessagesUpdates",
                    "RemoteUserTypingChanged bridge threw " +
                    ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static DateTime FromUnixSeconds(int unixSeconds)
        {
            if (unixSeconds <= 0) return DateTime.UtcNow;
            return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
                .AddSeconds(unixSeconds);
        }
    }
}
