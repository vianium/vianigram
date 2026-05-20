// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MutedPeersStore.cs — Vianigram.App.Services
//
// Per-peer mute state.
//
// Telegram's `peerNotifySettings#99622c0c` carries two flags relevant
// to whether a notification should fire:
//
//   * `silent:flags.1?Bool` — suppress the toast sound but still show
//      the badge / unread bump. Used for "Important Alerts only" style
//      preferences.
//
//   * `mute_until:flags.2?int` — a Unix timestamp; while now < mute_until
//      the peer is fully muted (no toast at all). 0 means not muted;
//      far-future values (Telegram uses 2147483647 / INT32_MAX) mean
//      "muted forever".
//
// We don't model the scope-default cascade (notifyUsers / notifyChats /
// notifyBroadcasts) yet — only explicit per-peer overrides are honoured.
// A scope-default mute would require a separate data layer to track the
// three default settings; deferred until a user actually configures one.
//
// Membership is updated by listening to `RemoteNotifySettingsChanged`
// from the kernel bus. The decoder upstream now actually populates
// the `Silent` and `MuteUntil` fields (previously they were null/0
// — see Sync.TlDecoder.TryDecodePeerNotifySettings).
//
// Lookup is O(1) via a ConcurrentDictionary keyed by PeerKey.

using System;
using System.Collections.Concurrent;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Sync.Domain.Events;

namespace Vianigram.App.Services
{
    public sealed class MutedPeersStore : IDisposable
    {
        private readonly IComponentLogger _log;
        private readonly IDisposable _sub;
        private readonly ConcurrentDictionary<string, MuteRecord> _records =
            new ConcurrentDictionary<string, MuteRecord>(StringComparer.Ordinal);

        private struct MuteRecord
        {
            public bool Silent;          // suppress sound only
            public int MuteUntilUnix;    // 0 = not muted; >0 = muted until this Unix timestamp
        }

        public MutedPeersStore(IEventBus bus, IComponentLogger log)
        {
            if (bus == null) throw new ArgumentNullException("bus");
            _log = log; // null-tolerant
            _sub = bus.Subscribe<RemoteNotifySettingsChanged>(OnSettingsChanged);
        }

        public void Dispose()
        {
            try { if (_sub != null) _sub.Dispose(); }
            catch { }
        }

        /// <summary>
        /// True when toasts for <paramref name="peerKey"/> should be
        /// fully suppressed. Cascade order matches TDLib's
        /// NotificationSettingsManager:
        ///
        ///   1. Per-peer override: if the peer has its own
        ///      <c>peerNotifySettings</c> with a non-zero <c>mute_until</c>,
        ///      that wins.
        ///   2. Scope default: otherwise look up the peer's scope
        ///      (notifyUsers / notifyChats / notifyBroadcasts) and use
        ///      its <c>mute_until</c>. Setting "Mute all groups" in
        ///      Telegram Settings → Notifications fires
        ///      <c>updateNotifySettings(notifyChats, ...)</c> which
        ///      lands here as <c>scope:chats</c>.
        ///
        /// Returns false when no record matches (default = show).
        /// </summary>
        public bool IsMuted(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return false;
            int nowUnix = NowUnix();
            MuteRecord r;
            // 1. Explicit per-peer.
            if (_records.TryGetValue(peerKey, out r) && r.MuteUntilUnix > 0)
            {
                bool muted = r.MuteUntilUnix > nowUnix;
                if (muted && _log != null)
                {
                    _log.Info("mute.decision peer=" + peerKey +
                        " result=true source=per-peer until=" + r.MuteUntilUnix);
                }
                return muted;
            }
            // 2. Scope default for this peer kind.
            string scopeKey = ScopeKeyFor(peerKey);
            if (!string.IsNullOrEmpty(scopeKey)
                && _records.TryGetValue(scopeKey, out r)
                && r.MuteUntilUnix > 0)
            {
                bool muted = r.MuteUntilUnix > nowUnix;
                if (muted && _log != null)
                {
                    _log.Info("mute.decision peer=" + peerKey +
                        " result=true source=" + scopeKey +
                        " until=" + r.MuteUntilUnix);
                }
                return muted;
            }
            return false;
        }

        /// <summary>
        /// True when the peer requested silent notifications (no sound
        /// even when the toast shows). Cascade per <see cref="IsMuted"/>:
        /// per-peer wins, then scope default. Honoured by
        /// PushNotificationsService via ToastNotification.SuppressPopup.
        /// </summary>
        public bool IsSilent(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return false;
            MuteRecord r;
            if (_records.TryGetValue(peerKey, out r) && r.Silent) return true;
            string scopeKey = ScopeKeyFor(peerKey);
            if (!string.IsNullOrEmpty(scopeKey)
                && _records.TryGetValue(scopeKey, out r)
                && r.Silent)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Map a per-peer key to its notification scope:
        ///   user:N    → scope:users      (notifyUsers)
        ///   chat:N    → scope:chats      (notifyChats — basic groups + supergroups)
        ///   channel:N → scope:broadcasts (notifyBroadcasts)
        /// Channels that are megagroups should ideally route to scope:chats,
        /// but PeerKey doesn't carry the megagroup flag — we accept the
        /// approximation and route channels to broadcasts. A follow-up
        /// can resolve via IPeerCache.IsMegagroup if needed.
        /// </summary>
        private static string ScopeKeyFor(string peerKey)
        {
            if (peerKey.StartsWith("user:", StringComparison.Ordinal)) return "scope:users";
            if (peerKey.StartsWith("chat:", StringComparison.Ordinal)) return "scope:chats";
            if (peerKey.StartsWith("channel:", StringComparison.Ordinal)) return "scope:broadcasts";
            return string.Empty;
        }

        private static int NowUnix()
        {
            return (int)((DateTime.UtcNow - UnixEpoch).TotalSeconds);
        }

        private void OnSettingsChanged(RemoteNotifySettingsChanged e)
        {
            if (e == null || string.IsNullOrEmpty(e.PeerKey)) return;
            try
            {
                MuteRecord r = new MuteRecord
                {
                    Silent = e.Silent.GetValueOrDefault(false),
                    MuteUntilUnix = e.MuteUntil
                };
                _records[e.PeerKey] = r;
                if (_log != null)
                {
                    _log.Info("mute.update peer=" + e.PeerKey +
                        " silent=" + r.Silent +
                        " until=" + r.MuteUntilUnix);
                }
            }
            catch (Exception ex)
            {
                if (_log != null) _log.Warn(
                    "mute.update threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    }
}
