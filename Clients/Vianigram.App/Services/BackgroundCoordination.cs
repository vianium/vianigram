// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BackgroundCoordination.cs — Vianigram.App.Services
//
// Foreground side of the foreground/background coordination contract.
// Writes two pieces of state into LocalSettings that the
// Vianigram.App.BackgroundTasks WinRT component reads on every
// background tick:
//
//   1) A heartbeat timestamp updated every 5 seconds while the app is
//      foregrounded. The background task uses this to detect "the UI
//      is alive and owns the socket — quiet exit".
//
//   2) An unread-summary cache (peer name + body excerpt + total count)
//      updated every time a MessageReceived event fires for a peer the
//      user is NOT currently looking at. The background task uses this
//      to render a meaningful toast even when it can't open its own
//      MtProto channel — the cached summary is the user's last view of
//      "what arrived in the background".
//
// Both writes are best-effort; LocalSettings can theoretically throw on
// disk-full but in practice on WP 8.1 this never matters. Failures are
// silently swallowed.

using System;
using System.Threading;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Messages.Domain.Events;
using Windows.Foundation.Collections;
using Windows.Storage;
using Windows.UI.Xaml;

namespace Vianigram.App.Services
{
    public sealed class BackgroundCoordination : IDisposable
    {
        private const string KeyHeartbeatTicks = "vianigram.foreground_heartbeat_ticks_utc";
        private const string KeyUnreadCount = "vianigram.unread_total";
        private const string KeyLastPeerName = "vianigram.last_peer_name";
        private const string KeyLastBodyExcerpt = "vianigram.last_body_excerpt";
        private const string KeyLastEventTicks = "vianigram.last_event_ticks_utc";

        private const int BodyExcerptMaxLength = 80;

        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        private readonly DispatcherTimer _heartbeatTimer;
        private readonly IDisposable _messageSub;
        private string _activePeerKey;
        private int _disposed;

        public BackgroundCoordination(IEventBus bus, IComponentLogger log)
        {
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            _bus = bus;
            _log = log;

            _heartbeatTimer = new DispatcherTimer();
            _heartbeatTimer.Interval = TimeSpan.FromSeconds(5);
            _heartbeatTimer.Tick += OnHeartbeatTick;
            _heartbeatTimer.Start();
            // Stamp once immediately so the first 5 s of foreground life
            // also keeps background tasks quiet.
            WriteHeartbeat();

            _messageSub = bus.Subscribe<MessageReceived>(OnMessageReceived);

            _log.Info("background-coordination: heartbeat + summary writer started");
        }

        /// <summary>
        /// The currently-active conversation peer (set by ChatPage when
        /// the user navigates in/out). MessageReceived events for THIS
        /// peer don't update the unread summary — the user is already
        /// looking at the message.
        /// </summary>
        public void SetActivePeerKey(string peerKey)
        {
            _activePeerKey = peerKey ?? string.Empty;
        }

        /// <summary>
        /// Foreground call on logout: clear the unread cache so the next
        /// account doesn't inherit stale toasts.
        /// </summary>
        public void ResetUnreadCache()
        {
            try
            {
                IPropertySet vals = ApplicationData.Current.LocalSettings.Values;
                vals.Remove(KeyUnreadCount);
                vals.Remove(KeyLastPeerName);
                vals.Remove(KeyLastBodyExcerpt);
                vals.Remove(KeyLastEventTicks);
            }
            catch { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { _heartbeatTimer.Stop(); } catch { }
            try { _heartbeatTimer.Tick -= OnHeartbeatTick; } catch { }
            try { if (_messageSub != null) _messageSub.Dispose(); } catch { }
        }

        // ----------------------------------------------------------------
        // Heartbeat: stamp every 5 s.
        // ----------------------------------------------------------------

        private void OnHeartbeatTick(object sender, object e)
        {
            WriteHeartbeat();
        }

        private void WriteHeartbeat()
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[KeyHeartbeatTicks] =
                    DateTime.UtcNow.Ticks;
            }
            catch
            {
                // LocalSettings can throw under exotic disk-full conditions
                // — a missed heartbeat just risks an extra background
                // wakeup, never data loss.
            }
        }

        // ----------------------------------------------------------------
        // Unread summary writer.
        // ----------------------------------------------------------------

        private void OnMessageReceived(MessageReceived e)
        {
            if (e == null) return;
            if (e.IsOutgoing) return; // never count our own
            if (string.IsNullOrEmpty(e.PeerKey)) return;
            // If the user is actively viewing this peer they're already
            // seeing the message; don't surface it to the background
            // unread cache.
            if (string.Equals(e.PeerKey, _activePeerKey, StringComparison.Ordinal)) return;

            try
            {
                IPropertySet vals = ApplicationData.Current.LocalSettings.Values;

                int prior = 0;
                object raw;
                if (vals.TryGetValue(KeyUnreadCount, out raw))
                {
                    try { prior = Convert.ToInt32(raw); }
                    catch { prior = 0; }
                }
                int next = prior + 1;
                if (next > 999) next = 999;

                string excerpt = e.Body ?? string.Empty;
                if (excerpt.Length > BodyExcerptMaxLength)
                {
                    excerpt = excerpt.Substring(0, BodyExcerptMaxLength - 1) + "…";
                }

                vals[KeyUnreadCount] = next;
                vals[KeyLastPeerName] = e.PeerKey ?? string.Empty;
                vals[KeyLastBodyExcerpt] = excerpt;
                vals[KeyLastEventTicks] = DateTime.UtcNow.Ticks;
            }
            catch
            {
                // Silent — see WriteHeartbeat rationale.
            }
        }
    }
}
