// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PushNotificationsService.cs — Vianigram.App.Services
//
// Wires the WP 8.1 MPNS push channel + Telegram's account.registerDevice
// + a foreground toast surface for incoming messages. Two cooperating
// layers:
//
//   1) Channel registration. On login we obtain a
//      PushNotificationChannel via
//      PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync()
//      and POST its URI to Telegram via IAccountApi.RegisterPushDeviceAsync
//      (token_type=8 = MPNS). The server then routes raw push payloads to
//      that URI when our app is suspended.
//
//   2) Foreground toast surface. While the app is running we already
//      receive MessageReceived events on the IEventBus (via the new
//      Sync→Messages bridge from Tasks 6/2). For peers that are NOT the
//      currently-displayed conversation we fire a Windows.UI.Notifications
//      toast so the user gets visual feedback even when on another page
//      (Settings, ChatList of other contacts, etc.). This handles the
//      "app running but not actively viewing this peer" case without
//      needing a background task.
//
// A real RawNotificationTask background-task project (separate WinRT
// component) is the next wave for the truly-suspended case; the wiring
// here surfaces foreground toasts and registers the channel so the
// server is already pushing by the time that task lands.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Ports.Inbound;
using Vianigram.Calls.Domain.Events;
using Vianigram.Composition.Infrastructure;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Messages.Domain.Events;
using Vianigram.Sync.Domain.ValueObjects;
using Windows.Networking.PushNotifications;
using Windows.UI.Notifications;
using Windows.Data.Xml.Dom;

namespace Vianigram.App.Services
{
    public sealed class PushNotificationsService : IDisposable
    {
        private const int MpnsTokenType = 8; // Telegram TL: token_type for MPNS / Windows Phone

        // Toast body truncation budget — matches Android Telegram's
        // NotificationsController.java limit. Bodies longer than this
        // get shortened with an ellipsis so the toast template doesn't
        // wrap weirdly on small screens.
        private const int ToastBodyMaxChars = 100;

        private readonly IAccountApi _account;
        private readonly IEventBus _bus;
        private readonly IComponentLogger _log;
        // Optional: when present we resolve PeerKey → display name so the
        // toast title says "Alice" / "Family group" instead of
        // "user:5308636445". Null-tolerant: if the cache hasn't observed
        // the peer yet (no dialogs.getDialogs slice yet) we fall back to
        // the raw key so the toast still surfaces.
        private readonly IPeerCache _peerCache;
        // Optional: per-peer mute store. When present, IsMuted-true peers
        // are filtered out before showing the toast.
        private readonly MutedPeersStore _muted;

        private IDisposable _messageSub;
        private IDisposable _callSub;
        private PushNotificationChannel _channel;
        private string _registeredToken;
        private string _activePeerKeyText;
        private int _disposed;

        public PushNotificationsService(IAccountApi account, IEventBus bus, IComponentLogger log)
            : this(account, bus, log, peerCache: null, muted: null)
        {
        }

        public PushNotificationsService(
            IAccountApi account,
            IEventBus bus,
            IComponentLogger log,
            IPeerCache peerCache)
            : this(account, bus, log, peerCache, muted: null)
        {
        }

        /// <summary>
        /// Preferred ctor with the
        /// peer cache + mute store injected so the toast title resolves
        /// to a real display name and muted peers stay quiet. Both are
        /// null-tolerant for tests / transient bring-up paths.
        /// </summary>
        public PushNotificationsService(
            IAccountApi account,
            IEventBus bus,
            IComponentLogger log,
            IPeerCache peerCache,
            MutedPeersStore muted)
        {
            if (account == null) throw new ArgumentNullException("account");
            if (bus == null) throw new ArgumentNullException("bus");
            if (log == null) throw new ArgumentNullException("log");
            _account = account;
            _bus = bus;
            _log = log;
            _peerCache = peerCache; // null-tolerant
            _muted = muted; // null-tolerant
        }

        /// <summary>
        /// Track which peer the user is currently looking at so we don't
        /// double-toast a message they're already reading. Set to null /
        /// empty when navigating away from a chat. Idempotent.
        /// </summary>
        public void SetActivePeerKey(string peerKey)
        {
            _activePeerKeyText = peerKey ?? string.Empty;
        }

        /// <summary>
        /// Acquire (or refresh) the MPNS channel and register its URI
        /// with Telegram. Safe to call multiple times — the channel is
        /// re-acquired (the URI may have rotated) and re-registered.
        /// Always wires the foreground toast surface on first call so
        /// new messages produce toasts even if MPNS itself fails.
        /// </summary>
        public async Task RegisterAsync(CancellationToken ct)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            // Foreground toast surface: subscribed before we attempt
            // MPNS so the user benefits even if registration fails.
            EnsureForegroundToastSubscription();

            try
            {
                _log.Info("push.register: requesting MPNS channel");
                _channel = await PushNotificationChannelManager
                    .CreatePushNotificationChannelForApplicationAsync()
                    .AsTask(ct)
                    .ConfigureAwait(false);

                if (_channel == null)
                {
                    _log.Warn("push.register: PushNotificationChannelManager returned null");
                    return;
                }

                string uri = _channel.Uri ?? string.Empty;
                if (uri.Length == 0)
                {
                    _log.Warn("push.register: channel URI is empty");
                    return;
                }

                // Forward incoming raw notifications to a single
                // dispatcher inside this service. RawNotification
                // delivers payload bytes directly — Telegram encrypts
                // them with the session secret, which we don't decrypt
                // here yet (background task work). For now we simply
                // log and emit a generic toast so the user sees that
                // something arrived even when the app is backgrounded
                // (but not suspended).
                _channel.PushNotificationReceived += OnPushNotificationReceived;

                _log.Info("push.register: MPNS uri len=" + uri.Length +
                    " — POSTing to Telegram");
                var registerResult = await _account.RegisterPushDeviceAsync(
                    MpnsTokenType, uri, new byte[0], ct).ConfigureAwait(false);

                if (registerResult.IsFail)
                {
                    _log.Warn("push.register: account.registerDevice failed: " + registerResult.Error);
                    return;
                }

                _registeredToken = uri;
                _log.Info("push.register: registered with Telegram successfully");
            }
            catch (OperationCanceledException)
            {
                _log.Info("push.register: cancelled");
            }
            catch (Exception ex)
            {
                _log.Warn("push.register: threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Drop the MPNS registration on logout. Fire-and-forget on the
        /// transport — best-effort; if the server doesn't ack we still
        /// scrub local state.
        /// </summary>
        public async Task UnregisterAsync(CancellationToken ct)
        {
            string token = _registeredToken;
            _registeredToken = null;

            try { if (_channel != null) _channel.Close(); }
            catch { }
            _channel = null;

            if (string.IsNullOrEmpty(token)) return;

            try
            {
                var r = await _account.UnregisterPushDeviceAsync(MpnsTokenType, token, ct).ConfigureAwait(false);
                if (r.IsFail) _log.Warn("push.unregister: " + r.Error);
                else _log.Info("push.unregister: ok");
            }
            catch (Exception ex)
            {
                _log.Warn("push.unregister: threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try { if (_messageSub != null) _messageSub.Dispose(); }
            catch { }
            try { if (_callSub != null) _callSub.Dispose(); }
            catch { }
            try { if (_channel != null) _channel.Close(); }
            catch { }
        }

        // -----------------------------------------------------------------
        // Foreground toast surface
        // -----------------------------------------------------------------

        private void EnsureForegroundToastSubscription()
        {
            if (_messageSub == null)
            {
                _messageSub = _bus.Subscribe<MessageReceived>(OnMessageReceived);
                _log.Info("push.foreground: subscribed to MessageReceived");
            }
            if (_callSub == null)
            {
                _callSub = _bus.Subscribe<CallReceived>(OnCallReceived);
                _log.Info("push.foreground: subscribed to CallReceived");
            }
        }

        private void OnMessageReceived(MessageReceived e)
        {
            if (e == null)
            {
                _log.Info("push.foreground: drop null event");
                return;
            }
            // Unconditional entry log so a "no channel notification"
            // report can be
            // grepped for "push.foreground: enter peer=channel:".
            // Cheap (one log line per inbound message).
            _log.Info("push.foreground: enter peer=" + (e.PeerKey ?? "?") +
                " id=" + e.MessageId +
                " out=" + e.IsOutgoing +
                " body_len=" + (e.Body == null ? 0 : e.Body.Length));
            if (e.IsOutgoing)
            {
                _log.Info("push.foreground: drop outgoing peer=" + (e.PeerKey ?? "?") + " id=" + e.MessageId);
                return;
            }
            if (string.IsNullOrEmpty(e.PeerKey))
            {
                _log.Info("push.foreground: drop empty peerKey id=" + e.MessageId);
                return;
            }
            // Skip if user is actively viewing this peer.
            if (string.Equals(e.PeerKey, _activePeerKeyText, StringComparison.Ordinal))
            {
                _log.Info("push.foreground: drop active-peer peer=" + e.PeerKey + " id=" + e.MessageId);
                return;
            }

            // Respect per-peer mute.
            if (_muted != null && _muted.IsMuted(e.PeerKey))
            {
                _log.Info("push.foreground: drop muted peer=" + e.PeerKey + " id=" + e.MessageId);
                return;
            }

            try
            {
                string title = ResolvePeerTitle(e.PeerKey);
                string body = BuildMessageBody(e);
                bool silent = _muted != null && _muted.IsSilent(e.PeerKey);
                _log.Info("push.foreground: SHOW toast peer=" + e.PeerKey +
                    " title=\"" + title + "\"" +
                    " id=" + e.MessageId +
                    " body_len=" + (e.Body == null ? 0 : e.Body.Length) +
                    " silent=" + silent);
                ShowToast(title, body, silent);
            }
            catch (Exception ex)
            {
                _log.Warn("push.foreground: toast threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // VoIP incoming-call surface
        // -----------------------------------------------------------------

        private void OnCallReceived(CallReceived e)
        {
            if (e == null)
            {
                _log.Info("push.call: drop null event");
                return;
            }
            try
            {
                string callerName = ResolveUserDisplayName(e.FromUserId);
                if (string.IsNullOrEmpty(callerName))
                {
                    callerName = Strings.Get("Notif.Call.UnknownCaller");
                }
                string title = e.Video
                    ? Strings.Get("Notif.Call.IncomingVideoCall")
                    : Strings.Get("Notif.Call.IncomingCall");
                _log.Info("push.call: SHOW call-toast caller=\"" + callerName +
                    "\" video=" + e.Video +
                    " callId=" + e.CallId);
                // Incoming calls bypass the silent-toast path — a call
                // toast must be loud enough to ring even on muted peers
                // (the user's "do not disturb" governs that, not the
                // chat's mute setting).
                ShowToast(title, callerName, silent: false);
            }
            catch (Exception ex)
            {
                _log.Warn("push.call: toast threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        // -----------------------------------------------------------------
        // Title / body composition
        // -----------------------------------------------------------------

        /// <summary>
        /// Resolve a PeerKey (e.g. <c>user:5308636445</c> or
        /// <c>chat:42</c>) to the display name observed in the peer
        /// cache. Friendlier fallbacks when the cache hasn't observed
        /// the peer yet ("Chat" / "Channel" / "Direct message" instead
        /// of the raw <c>user:N</c> key) so the toast never leaks the
        /// internal coordinate format. Never throws — runs on the
        /// toast hot path.
        /// </summary>
        private string ResolvePeerTitle(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return Strings.Get("Notif.Title.NewMessage");

            string kind;
            long id;
            if (!PeerKey.TryParse(peerKey, out kind, out id))
            {
                return Strings.Get("Notif.Title.NewMessage");
            }

            if (_peerCache != null)
            {
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
                        if (!string.IsNullOrEmpty(title)) return title;
                    }
                }
                catch
                {
                    // Cache lookup is best-effort.
                }
            }

            // Cache miss: produce a friendly fallback that doesn't
            // leak the internal coordinate format. The user sees "New
            // message" / "Group chat" / "Channel" — clean enough that
            // they tap to investigate, and the next dialogs.getDialogs
            // / users.getUsers slice will populate the cache for the
            // next notification from the same peer.
            if (string.Equals(kind, "channel", StringComparison.Ordinal)) return Strings.Get("Notif.Title.Channel");
            if (string.Equals(kind, "chat", StringComparison.Ordinal)) return Strings.Get("Notif.Title.GroupChat");
            return Strings.Get("Notif.Title.DirectMessage");
        }

        private string ResolveUserDisplayName(long userId)
        {
            if (_peerCache == null || userId == 0L) return string.Empty;
            try { return _peerCache.GetUserDisplayName(userId) ?? string.Empty; }
            catch { return string.Empty; }
        }

        /// <summary>
        /// Build the toast body with a layout matching the official
        /// Telegram clients:
        ///
        ///   * 1-on-1 DM, regular text: just the body text.
        ///   * 1-on-1 DM, media-only: the media-type description
        ///     ("📷 Photo", "🎤 Voice", etc.) coming from the wire
        ///     decoder.
        ///   * Group/channel, regular: "Sender: text".
        ///   * Group/channel, service message: "Sender action description"
        ///     (e.g. "Bob joined the group", "Alice pinned a message").
        ///     The action descriptions are written without a subject
        ///     so the sender prefix reads naturally without a trailing
        ///     colon.
        ///   * Anything where the body is empty AND we don't know
        ///     better: "(message)" as a last-resort placeholder.
        /// </summary>
        private string BuildMessageBody(MessageReceived e)
        {
            // Wire body may be a keyed-format token ("~Service.JoinedGroup"
            // / "~Media.Voice") emitted by Vianigram.Sync.TlDecoder.
            // Translate against the resw catalogue so the user sees their
            // chosen locale rather than the wire key.
            string text = LocalizedText.Resolve(e.Body);
            if (string.IsNullOrEmpty(text)) text = Strings.Get("Notif.Body.Generic");

            // Determine whether this peer is a group/channel — they
            // benefit from the sender prefix; DMs don't (the title
            // already names the sender).
            string kind;
            long id;
            bool isGroup = false;
            if (PeerKey.TryParse(e.PeerKey ?? string.Empty, out kind, out id))
            {
                isGroup = string.Equals(kind, "chat", StringComparison.Ordinal) ||
                          string.Equals(kind, "channel", StringComparison.Ordinal);
            }

            if (!isGroup || e.FromUserId == 0L) return text;

            string sender = ResolveUserDisplayName(e.FromUserId);
            if (string.IsNullOrEmpty(sender))
            {
                // The user hasn't been observed yet — fallback to a
                // generic "Someone" so the user sees activity rather
                // than the raw user id, mirroring iOS Telegram's
                // "Someone in [Group]" pattern when slices haven't
                // resolved.
                sender = Strings.Get("Notif.Sender.Someone");
            }

            // Action descriptions ("joined the group", "pinned a
            // message") read naturally with a SPACE separator. Regular
            // text reads naturally with a COLON. Heuristic: if the
            // text starts with a verb / emoji / non-letter, treat as
            // action; else colon.
            bool isActionLike = LooksLikeServiceMessage(text);
            return isActionLike ? sender + " " + text : sender + ": " + text;
        }

        /// <summary>
        /// Heuristic: true when <paramref name="text"/> looks like a
        /// service-message description rather than user-typed text.
        /// We match the action vocabulary written by
        /// <c>TlDecoder.DescribeMessageAction</c>: lowercase verbs
        /// ("joined", "pinned", "removed"), emoji prefixes ("📞", "🎁"),
        /// and a few stable phrases. Used to decide whether the sender
        /// prefix takes a colon or a space.
        /// </summary>
        private static bool LooksLikeServiceMessage(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            // Emoji prefix → service-style.
            char first = text[0];
            if (first > 127) return true;
            // Common action verbs in the description vocabulary.
            string[] prefixes = new[]
            {
                "joined", "added", "removed", "left", "created",
                "changed", "pinned", "cleared", "took", "set",
                "scheduled", "invited", "edited", "allowed",
                "sent", "received", "shared", "service",
                "migrated"
            };
            for (int i = 0; i < prefixes.Length; i++)
            {
                if (text.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private void ShowToast(string title, string body, bool silent)
        {
            if (string.IsNullOrEmpty(title)) title = Strings.Get("Notif.Title.Vianigram");
            if (string.IsNullOrEmpty(body)) body = string.Empty;

            // Truncate to match the Android client
            // (NotificationsController.java limit of
            // 100 UTF-16 chars) and replace newlines with spaces so a
            // long pasted message doesn't blow up the toast template.
            body = TruncateForToast(body, ToastBodyMaxChars);

            // ToastTemplateType.ToastText02: title + wrapping body.
            var template = ToastTemplateType.ToastText02;
            XmlDocument xml = ToastNotificationManager.GetTemplateContent(template);
            var textNodes = xml.GetElementsByTagName("text");
            if (textNodes.Length >= 1) textNodes[0].AppendChild(xml.CreateTextNode(title));
            if (textNodes.Length >= 2) textNodes[1].AppendChild(xml.CreateTextNode(body));

            // Silent toast: the WP 8.1 toast XML supports a `silent="true"`
            // attribute on the audio element. We patch the template (which
            // omits audio by default) to add it when the peer requested
            // silent notifications via peerNotifySettings.silent. SuppressPopup
            // separately suppresses the visual popup — we leave that off so
            // the user still sees activity, just without a sound.
            if (silent)
            {
                try
                {
                    var audio = xml.CreateElement("audio");
                    audio.SetAttribute("silent", "true");
                    var toastEl = xml.GetElementsByTagName("toast");
                    if (toastEl.Length > 0) toastEl[0].AppendChild(audio);
                }
                catch
                {
                    // Best-effort; if the XML mutation fails we still
                    // produce a normal toast.
                }
            }

            var toast = new ToastNotification(xml);
            ToastNotificationManager.CreateToastNotifier().Show(toast);
        }

        private static string TruncateForToast(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            // Replace newlines first so the truncation happens against
            // the visible-character count, not against multi-line text
            // that would render with awkward gaps.
            string flat = text.Replace('\n', ' ').Replace('\r', ' ');
            if (flat.Length <= max) return flat;
            // Trim to max-1 chars and append ellipsis so total ≤ max.
            return flat.Substring(0, max - 1).TrimEnd() + "…";
        }

        // -----------------------------------------------------------------
        // MPNS raw payload handler (foreground / background-but-running)
        // -----------------------------------------------------------------

        private void OnPushNotificationReceived(
            PushNotificationChannel sender,
            PushNotificationReceivedEventArgs args)
        {
            // We mark the event handled because the OS would otherwise
            // try to show a default toast from the payload — Telegram's
            // payload is encrypted JSON, not a toast template.
            try
            {
                if (args == null) return;

                if (args.NotificationType == PushNotificationType.Raw)
                {
                    var raw = args.RawNotification;
                    if (raw != null)
                    {
                        string content = raw.Content ?? string.Empty;
                        _log.Info("push.raw: content_len=" + content.Length);
                        // Decryption + targeted toast emission lives in
                        // the future RawNotificationTask. For now log
                        // only so the integration is observable.
                    }
                    args.Cancel = true;
                }
                else if (args.NotificationType == PushNotificationType.Toast)
                {
                    // If Telegram ever sends a pre-formatted toast we
                    // let the OS render it untouched.
                    _log.Info("push.toast: passthrough");
                }
                else
                {
                    _log.Info("push.other: type=" + args.NotificationType);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("push.raw: handler threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }
}
