// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BackgroundTaskRunner.cs — Vianigram.App.BackgroundTasks
//
// Shared logic the two task classes (MtprotoMaintenanceTask,
// VoipKeepAliveTask) need:
//
//   1) Coordination check via BackgroundTaskHeartbeat.
//   2) Lightweight log line (LocalSettings ring buffer) so we can audit
//      what the OS scheduler did with our tasks.
//   3) Toast emission — the user-visible side effect both tasks
//      ultimately produce.
//   4) Tile badge / count update.
//
// Why no MtProto socket logic here in v1: opening the native channel
// from a separate process requires reproducing the storage + auth_key
// + DC bootstrap that lives in Vianigram.Composition. Replicating that
// machinery cleanly is a v2 follow-up. The v1 in this file:
//
//   - Reads the persisted "unread summary" the foreground app already
//     maintains in LocalSettings (peer name + last message excerpt +
//     count). The foreground updates it on every MessageReceived; on
//     a stale wakeup the background can show "you have N unread
//     messages from <last peer>" without re-querying the server.
//   - Emits a toast and a tile update from that summary.
//
// The MtProto-channel-from-task path lands when the v2 background
// engine is wired (it shares Vianigram.Storage with the UI process).

using System;
using System.Collections.Generic;
using Windows.Data.Xml.Dom;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Notifications;

namespace Vianigram.App.BackgroundTasks
{
    public sealed class BackgroundTaskRunner
    {
        // LocalSettings keys the foreground writes to so the background
        // task can summarise without an MTProto round-trip. See
        // Clients/Vianigram.App/Services/BackgroundCoordination.cs for
        // the writer.
        private const string KeyUnreadCount = "vianigram.unread_total";
        private const string KeyLastPeerName = "vianigram.last_peer_name";
        private const string KeyLastBodyExcerpt = "vianigram.last_body_excerpt";
        private const string KeyLastEventTicks = "vianigram.last_event_ticks_utc";
        private const string KeyLastTaskRunTicks = "vianigram.last_task_run_ticks_utc";
        private const string KeyLastTaskRunMode = "vianigram.last_task_run_mode";

        private const int MaxToastBodyLength = 80;

        private readonly BackgroundTaskHeartbeat _heartbeat = new BackgroundTaskHeartbeat();

        /// <summary>
        /// Run a single background-task tick. Returns true when the task
        /// did real work (showed a toast / updated the tile); false when
        /// the foreground was alive or there was nothing new to report.
        /// </summary>
        public bool RunTick(string mode)
        {
            StampLastRun(mode);

            if (_heartbeat.IsForegroundAlive())
            {
                // Foreground owns the socket; quiet exit.
                return false;
            }

            UnreadSnapshot snapshot = ReadUnreadSnapshot();
            if (snapshot.UnreadCount <= 0)
            {
                // Nothing accumulated since the last tick.
                UpdateTile(0);
                return false;
            }

            try { ShowToast(snapshot); }
            catch { /* toast surface is best-effort */ }

            try { UpdateTile(snapshot.UnreadCount); }
            catch { /* tile surface is best-effort */ }

            return true;
        }

        // ------------------------------------------------------------
        // Toast + tile helpers
        // ------------------------------------------------------------

        private static void ShowToast(UnreadSnapshot snapshot)
        {
            string title = string.IsNullOrEmpty(snapshot.LastPeerName)
                ? "Vianigram"
                : snapshot.LastPeerName;

            string body;
            if (snapshot.UnreadCount == 1)
            {
                body = string.IsNullOrEmpty(snapshot.LastBodyExcerpt)
                    ? "You have 1 new message"
                    : Truncate(snapshot.LastBodyExcerpt, MaxToastBodyLength);
            }
            else
            {
                body = "You have " + snapshot.UnreadCount + " new messages";
                if (!string.IsNullOrEmpty(snapshot.LastBodyExcerpt))
                {
                    body += " — " + Truncate(snapshot.LastBodyExcerpt, MaxToastBodyLength);
                }
            }

            XmlDocument xml = ToastNotificationManager.GetTemplateContent(ToastTemplateType.ToastText02);
            var nodes = xml.GetElementsByTagName("text");
            if (nodes.Length >= 1) nodes[0].AppendChild(xml.CreateTextNode(title));
            if (nodes.Length >= 2) nodes[1].AppendChild(xml.CreateTextNode(body));

            var toast = new ToastNotification(xml);
            toast.Tag = "vianigram-bg";
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        private static void UpdateTile(int unreadCount)
        {
            // Square tile badge (unread count). 0 clears the badge.
            string badgeXml;
            if (unreadCount <= 0)
            {
                badgeXml = "<badge value=\"none\"/>";
            }
            else
            {
                badgeXml = "<badge value=\"" + (unreadCount > 99 ? 99 : unreadCount) + "\"/>";
            }
            XmlDocument doc = new XmlDocument();
            doc.LoadXml(badgeXml);
            var badge = new BadgeNotification(doc);
            BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(badge);
        }

        // ------------------------------------------------------------
        // Snapshot read (foreground populates these LocalSettings keys
        // from BackgroundCoordination.cs every time MessageReceived fires
        // for a peer the user is NOT currently viewing).
        // ------------------------------------------------------------

        private static UnreadSnapshot ReadUnreadSnapshot()
        {
            UnreadSnapshot snap = new UnreadSnapshot();
            try
            {
                IPropertySet vals = ApplicationData.Current.LocalSettings.Values;

                object raw;
                if (vals.TryGetValue(KeyUnreadCount, out raw))
                {
                    try { snap.UnreadCount = Convert.ToInt32(raw); }
                    catch { snap.UnreadCount = 0; }
                }

                if (vals.TryGetValue(KeyLastPeerName, out raw))
                {
                    snap.LastPeerName = raw as string ?? string.Empty;
                }

                if (vals.TryGetValue(KeyLastBodyExcerpt, out raw))
                {
                    snap.LastBodyExcerpt = raw as string ?? string.Empty;
                }
            }
            catch
            {
                // LocalSettings unavailable (extremely rare on WP 8.1);
                // return an empty snapshot — no toast.
            }
            return snap;
        }

        private static void StampLastRun(string mode)
        {
            try
            {
                IPropertySet vals = ApplicationData.Current.LocalSettings.Values;
                vals[KeyLastTaskRunTicks] = DateTime.UtcNow.Ticks;
                vals[KeyLastTaskRunMode] = mode ?? string.Empty;
            }
            catch { }
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return s ?? string.Empty;
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…"; // ellipsis
        }

        // Internal struct returned by ReadUnreadSnapshot.
        private sealed class UnreadSnapshot
        {
            public int UnreadCount;
            public string LastPeerName = string.Empty;
            public string LastBodyExcerpt = string.Empty;
        }
    }
}
