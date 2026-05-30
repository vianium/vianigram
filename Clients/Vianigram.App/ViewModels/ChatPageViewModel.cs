// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ChatPageViewModel.cs
//
// Drives ChatPage. Owns the per-conversation message stream:
//
//   - LoadInitialAsync: paints cached rows immediately, then refreshes from MTProto.
//   - LoadOlderAsync:   pulls the next page when the user scrolls up.
//   - SendAsync:        optimistic-send; returns immediately with the temp id and
//                       lets MessagesChanged update the bubble's status.
//
// Subscribes to IMessagesApi.MessagesChanged (filtered to this peer) so
// optimistic sends, server-confirmed inserts, and edits flow into the
// ObservableCollection without polling. Handlers marshal to the UI thread.

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.App.Controls.Bubbles;
using Vianigram.App.Services;
using Vianigram.Kernel.Result;
using Vianigram.Media.Domain.ValueObjects;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.Entities;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Ports.Inbound;

namespace Vianigram.App.ViewModels
{
    public sealed class ChatPageViewModel : ObservableObject
    {
        private const int PageSize = 50;

        private readonly IMessagesApi _messages;
        private readonly string _peerKey;

        private string _peerTitle;
        private string _composerText;
        private string _errorMessage;
        private bool _isBusy;
        private bool _isLoadingOlder;
        private bool _hasMoreOlder;
        private bool _isSubscribed;
        private long? _oldestKnownMessageId;
        private CancellationTokenSource _refreshCts;

        public ChatPageViewModel(IMessagesApi messages, string peerKey, string peerTitle)
        {
            _messages = messages;
            _peerKey = peerKey ?? string.Empty;
            _peerTitle = peerTitle ?? _peerKey;
            Messages = new MessageRowCollection();
        }

        public string PeerKey { get { return _peerKey; } }

        public string PeerTitle
        {
            get { return _peerTitle; }
            set
            {
                if (SetProperty(ref _peerTitle, value))
                {
                    OnPropertyChanged("PeerInitials");
                    OnPropertyChanged("PeerColorSeed");
                }
            }
        }

        public string PeerInitials
        {
            get { return CreateInitials(_peerTitle); }
        }

        public long PeerColorSeed
        {
            get { return CreateColorSeed(_peerKey, _peerTitle); }
        }

        // Avatar bitmap shared with the dialog list. Resolved lazily on
        // LoadInitialAsync via AvatarResolver, which consults the same
        // PeerAvatarFetcher (process-local bitmap cache + SQLite disk
        // cache) the ChatList already populated. A user who scrolled
        // past this peer in the dialog list will see the chat header
        // photo paint synchronously instead of dropping to initials.
        // Nullable — initials remain visible while null.
        private Windows.UI.Xaml.Media.ImageSource _peerAvatarImage;
        public Windows.UI.Xaml.Media.ImageSource PeerAvatarImage
        {
            get { return _peerAvatarImage; }
            private set { SetProperty(ref _peerAvatarImage, value); }
        }

        // Live-updated status, fed by Sync's RemoteUserStatusChanged /
        // RemoteUserTypingChanged via the MessagesUpdatesProcessor bridge.
        // Falls back to a generic
        // "last seen recently" until the first push arrives or the
        // contact is enriched at boot.
        private bool _isTyping;
        private string _typingActionLabel;
        private bool _isOnline;
        private DateTime? _lastOnlineUtc;

        public bool IsTyping
        {
            get { return _isTyping; }
            private set
            {
                if (SetProperty(ref _isTyping, value)) OnPropertyChanged("StatusText");
            }
        }

        public string TypingActionLabel
        {
            get { return _typingActionLabel; }
            private set
            {
                if (SetProperty(ref _typingActionLabel, value)) OnPropertyChanged("StatusText");
            }
        }

        public bool IsOnline
        {
            get { return _isOnline; }
            private set
            {
                if (SetProperty(ref _isOnline, value)) OnPropertyChanged("StatusText");
            }
        }

        public DateTime? LastOnlineUtc
        {
            get { return _lastOnlineUtc; }
            private set
            {
                if (SetProperty(ref _lastOnlineUtc, value)) OnPropertyChanged("StatusText");
            }
        }

        public string StatusText
        {
            get
            {
                if (_isTyping)
                {
                    return ProjectTypingLabel(_typingActionLabel);
                }
                if (_isOnline) return "online";
                if (_lastOnlineUtc.HasValue)
                {
                    return "last seen " + FormatLastSeen(_lastOnlineUtc.Value);
                }
                return "last seen recently";
            }
        }

        private static string ProjectTypingLabel(string action)
        {
            if (string.IsNullOrEmpty(action)) return "typing…";
            switch (action)
            {
                case "Typing": return "typing…";
                case "RecordingVoice": return "recording voice…";
                case "UploadingVoice": return "sending voice…";
                case "RecordingVideo": return "recording video…";
                case "UploadingVideo": return "sending video…";
                case "UploadingPhoto": return "sending photo…";
                case "UploadingDocument": return "sending file…";
                case "ChoosingSticker": return "choosing sticker…";
                case "ChoosingLocation": return "choosing location…";
                default: return "typing…";
            }
        }

        private static string FormatLastSeen(DateTime utc)
        {
            var span = DateTime.UtcNow - utc;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return ((int)span.TotalMinutes).ToString() + " min ago";
            if (span.TotalHours < 24) return ((int)span.TotalHours).ToString() + " h ago";
            if (span.TotalDays < 7) return ((int)span.TotalDays).ToString() + " d ago";
            return utc.ToLocalTime().ToString("MMM d");
        }

        public string DayLabel
        {
            get { return "today"; }
        }

        /// <summary>
        /// Newest-last collection so the ListView renders top-old / bottom-new.
        /// </summary>
        public ObservableCollection<MessageRow> Messages { get; private set; }

        public string ComposerText
        {
            get { return _composerText; }
            set
            {
                if (SetProperty(ref _composerText, value))
                    OnPropertyChanged("CanSend");
            }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    OnPropertyChanged("HasError");
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_errorMessage); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    OnPropertyChanged("CanSend");
            }
        }

        public bool HasMoreOlder
        {
            get { return _hasMoreOlder; }
            private set { SetProperty(ref _hasMoreOlder, value); }
        }

        /// <summary>
        /// True while a paginated older-load request is in flight. Distinct
        /// from <see cref="IsBusy"/> so the composer / send button stay
        /// responsive while the scroll-up handler refills the top.
        /// </summary>
        public bool IsLoadingOlder
        {
            get { return _isLoadingOlder; }
            private set { SetProperty(ref _isLoadingOlder, value); }
        }

        public bool CanSend
        {
            get { return !_isBusy && !string.IsNullOrWhiteSpace(_composerText); }
        }

        // ---- Subscription lifecycle ---------------------------------

        public void Subscribe()
        {
            if (_messages == null) return;
            if (_isSubscribed) return;
            _messages.MessagesChanged += OnMessagesChanged;
            _isSubscribed = true;
        }

        public void Unsubscribe()
        {
            CancelRefresh();
            if (_messages == null) return;
            if (!_isSubscribed) return;
            _messages.MessagesChanged -= OnMessagesChanged;
            _isSubscribed = false;
        }

        private void OnMessagesChanged(object sender, MessagesChangedEventArgs args)
        {
            if (args == null) return;

            // Presence is keyed by user-peer; typing can be keyed by
            // chat/channel (match _peerKey) or by user
            // (synthesised by MessagesApplication as "user:<id>"). We
            // accept any of those for the active conversation.
            bool isThisPeer = string.Equals(args.PeerKey, _peerKey, StringComparison.Ordinal);
            bool isPresenceForActiveDm =
                args.Kind == MessagesChangeKind.PeerStatusChanged
                && args.PeerKey != null
                && string.Equals(args.PeerKey, _peerKey, StringComparison.Ordinal);
            if (!isThisPeer && !isPresenceForActiveDm) return;

            switch (args.Kind)
            {
                case MessagesChangeKind.Queued:
                    if (args.ClientTempId.HasValue && !string.IsNullOrEmpty(args.Body))
                    {
                        var ignore = Dispatch.OnUiAsync(() => AppendPending(args));
                    }
                    else
                    {
                        var ignore = Dispatch.OnUiAsync(() =>
                        {
                            var loadIgnore = ReloadAsync(CancellationToken.None);
                        });
                    }
                    return;

                case MessagesChangeKind.Sent:
                    if (args.ClientTempId.HasValue && args.MessageId.HasValue)
                    {
                        long tempId = args.ClientTempId.Value;
                        long serverId = args.MessageId.Value;
                        var ignore = Dispatch.OnUiAsync(() => ConfirmPending(tempId, serverId));
                    }
                    else
                    {
                        var ignore = Dispatch.OnUiAsync(() =>
                        {
                            var loadIgnore = ReloadAsync(CancellationToken.None);
                        });
                    }
                    return;

                case MessagesChangeKind.SendFailed:
                    if (args.ClientTempId.HasValue)
                    {
                        long tempId = args.ClientTempId.Value;
                        var ignore = Dispatch.OnUiAsync(() => MarkPendingFailed(tempId));
                    }
                    else
                    {
                        var ignore = Dispatch.OnUiAsync(() =>
                        {
                            var loadIgnore = ReloadAsync(CancellationToken.None);
                        });
                    }
                    return;

                case MessagesChangeKind.Received:
                    // Granular append. When the bridge surfaced a body we
                    // can synthesize the bubble in place — no extra
                    // round-trip, no flash. Without a
                    // body we fall back to the legacy reload path
                    // (legacy thin-pipeline source or service messages).
                    if (!string.IsNullOrEmpty(args.Body) && args.MessageId.HasValue)
                    {
                        var ignore = Dispatch.OnUiAsync(() => AppendIncoming(args));
                    }
                    else
                    {
                        var ignore = Dispatch.OnUiAsync(() =>
                        {
                            var loadIgnore = ReloadAsync(CancellationToken.None);
                        });
                    }
                    return;

                case MessagesChangeKind.PeerTypingChanged:
                {
                    string action = args.TypingAction ?? "Typing";
                    var ignore = Dispatch.OnUiAsync(() =>
                    {
                        if (string.Equals(action, "Cancel", StringComparison.Ordinal))
                        {
                            IsTyping = false;
                            TypingActionLabel = null;
                        }
                        else
                        {
                            TypingActionLabel = action;
                            IsTyping = true;
                            // Telegram typing actions auto-expire on the
                            // server side after ~6 s; we do the same client-
                            // side so the indicator clears even if the
                            // peer's "cancel" update never lands.
                            ScheduleTypingClear();
                        }
                    });
                    return;
                }

                case MessagesChangeKind.PeerStatusChanged:
                {
                    bool? online = args.IsOnline;
                    DateTime? lastOnline = args.LastOnlineUtc;
                    var ignore = Dispatch.OnUiAsync(() =>
                    {
                        IsOnline = online.GetValueOrDefault(false);
                        LastOnlineUtc = lastOnline;
                    });
                    return;
                }

                case MessagesChangeKind.PeerReadOurMessages:
                {
                    // Granular ✓✓ advance. The bridge passes the up-to
                    // message id via args.MessageId (overloaded for this
                    // Kind).
                    long? upToId = args.MessageId;
                    if (!upToId.HasValue)
                    {
                        var reloadIgnore = Dispatch.OnUiAsync(() =>
                        {
                            var loadIgnore = ReloadAsync(CancellationToken.None);
                        });
                        return;
                    }
                    long upTo = upToId.Value;
                    var advance = Dispatch.OnUiAsync(() => AdvanceReadSeal(upTo));
                    return;
                }

                case MessagesChangeKind.ReadCursorAdvanced:
                {
                    // We read on another session — for now just reload
                    // so unread bubbles drop their "new" highlighting.
                    // Granular handling is a separate follow-up: needs a
                    // ReadCursorUpToId property on MessageRow.
                    var ignore = Dispatch.OnUiAsync(() =>
                    {
                        var loadIgnore = ReloadAsync(CancellationToken.None);
                    });
                    return;
                }

                case MessagesChangeKind.Edited:
                {
                    // Granular edit. When the bridge surfaces a body we
                    // rewrite the bubble in place; otherwise (caption /
                    // service edit) we fall
                    // back to ReloadAsync.
                    if (args.MessageId.HasValue && !string.IsNullOrEmpty(args.Body))
                    {
                        long mid = args.MessageId.Value;
                        string newBody = args.Body;
                        var ignore = Dispatch.OnUiAsync(() => ApplyEdit(mid, newBody));
                    }
                    else
                    {
                        var ignore = Dispatch.OnUiAsync(() =>
                        {
                            var loadIgnore = ReloadAsync(CancellationToken.None);
                        });
                    }
                    return;
                }

                case MessagesChangeKind.Deleted:
                {
                    // Granular delete — drop the row from the visible
                    // collection without a history refetch. Multiple
                    // deletes for the same peer
                    // arrive as separate events (the Sync bridge fans
                    // out the message_ids vector).
                    if (args.MessageId.HasValue)
                    {
                        long mid = args.MessageId.Value;
                        var ignore = Dispatch.OnUiAsync(() => ApplyDelete(mid));
                    }
                    else
                    {
                        var ignore = Dispatch.OnUiAsync(() =>
                        {
                            var loadIgnore = ReloadAsync(CancellationToken.None);
                        });
                    }
                    return;
                }

                default:
                {
                    // Queued / Sent / SendFailed / HistoryPageLoaded —
                    // legacy reload path. Sent/Queued/SendFailed are
                    // handled by the optimistic-send pipeline already;
                    // we reload defensively for any change kinds we
                    // don't intercept.
                    var ignore = Dispatch.OnUiAsync(() =>
                    {
                        var loadIgnore = ReloadAsync(CancellationToken.None);
                    });
                    return;
                }
            }
        }

        // -----------------------------------------------------------------
        // Granular bubble mutations
        // -----------------------------------------------------------------

        /// <summary>
        /// Rewrite the bubble for <paramref name="messageId"/> with the new
        /// body. Replaces the row in the <see cref="Messages"/> collection
        /// (since <see cref="MessageRow"/> is a POCO without
        /// PropertyChanged) so the ListView re-renders just that bubble.
        /// </summary>
        private void ApplyEdit(long messageId, string newBody)
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                MessageRow row = Messages[i];
                if (row == null) continue;
                if (!row.ServerId.HasValue || row.ServerId.Value != messageId) continue;

                MessageRow updated = CloneRow(row);
                updated.Text = newBody;
                Messages[i] = updated;
                return;
            }
            // Bubble not in the visible window — older message edited.
            // Skip silently; on scroll-up the user will fetch it fresh
            // via getHistory.
        }

        /// <summary>
        /// Remove the bubble for <paramref name="messageId"/> from the
        /// visible collection. No-op if the row has scrolled out of the
        /// window (the next paginated fetch will reflect the deletion).
        /// </summary>
        private void ApplyDelete(long messageId)
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                MessageRow row = Messages[i];
                if (row == null) continue;
                if (!row.ServerId.HasValue || row.ServerId.Value != messageId) continue;
                Messages.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// Advance the "✓✓" read seal on every outgoing bubble whose
        /// server id is &lt;= <paramref name="upToMessageId"/>. Replaces
        /// rows in place so XAML re-renders the status text.
        /// </summary>
        private void AdvanceReadSeal(long upToMessageId)
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                MessageRow row = Messages[i];
                if (row == null) continue;
                if (!row.IsOutgoing) continue;
                if (!row.ServerId.HasValue) continue;
                if (row.ServerId.Value > upToMessageId) continue;
                // ✓ → ✓✓ when read. The "Delivered" / "Read" StatusLabel
                // characters are identical (both ✓✓); see
                // FormatBubbleStatus. We only need to upgrade single-✓
                // (Sent) into ✓✓ here.
                if (string.Equals(row.StatusLabel, "✓", StringComparison.Ordinal) ||
                    string.Equals(row.StatusLabel, "...", StringComparison.Ordinal))
                {
                    MessageRow updated = CloneRow(row);
                    updated.StatusLabel = "✓✓";
                    Messages[i] = updated;
                }
            }
        }

        /// <summary>
        /// Shallow clone of <see cref="MessageRow"/> for replace-in-place
        /// mutations. <see cref="MessageRow"/> is a POCO without
        /// <c>INotifyPropertyChanged</c> — we replace the slot in the
        /// <see cref="ObservableCollection{T}"/> so XAML re-renders the
        /// bubble. Kept inline (~30 fields) instead of reflection to stay
        /// .NET Native friendly.
        /// </summary>
        private static MessageRow CloneRow(MessageRow src)
        {
            return new MessageRow
            {
                ServerId = src.ServerId,
                ClientTempId = src.ClientTempId,
                Kind = src.Kind,
                Text = src.Text,
                IsOutgoing = src.IsOutgoing,
                TimeLabel = src.TimeLabel,
                StatusLabel = src.StatusLabel,
                HasReply = src.HasReply,
                ReplyAuthorLabel = src.ReplyAuthorLabel,
                ReplyPreviewText = src.ReplyPreviewText,
                TextEntities = src.TextEntities,
                CaptionEntities = src.CaptionEntities,
                ReactionSummary = src.ReactionSummary,
                AuthorLabel = src.AuthorLabel,
                ShowAuthor = src.ShowAuthor,
                MediaCaption = src.MediaCaption,
                MediaWidth = src.MediaWidth,
                MediaHeight = src.MediaHeight,
                MediaThumbPath = src.MediaThumbPath,
                MediaFullPath = src.MediaFullPath,
                MediaSource = src.MediaSource,
                MediaPreviewBytes = src.MediaPreviewBytes,
                MediaLocationKind = src.MediaLocationKind,
                MediaFileType = src.MediaFileType,
                MediaRemoteId = src.MediaRemoteId,
                MediaAccessHash = src.MediaAccessHash,
                MediaFileReference = src.MediaFileReference,
                MediaDcId = src.MediaDcId,
                MediaSizeBytes = src.MediaSizeBytes,
                MediaMime = src.MediaMime,
                MediaFileName = src.MediaFileName,
                MediaThumbSize = src.MediaThumbSize,
                MediaTypeBadge = src.MediaTypeBadge,
                IsMediaLoading = src.IsMediaLoading,
                HasMediaFailed = src.HasMediaFailed,
                MediaDownloadProgress = src.MediaDownloadProgress,
                DurationLabel = src.DurationLabel,
                DurationSeconds = src.DurationSeconds,
                ElapsedSeconds = src.ElapsedSeconds,
                IsPlaying = src.IsPlaying,
                AudioSource = src.AudioSource,
                WaveformData = src.WaveformData,
                AudioTitle = src.AudioTitle,
                AudioPerformer = src.AudioPerformer,
                FileName = src.FileName,
                FileSizeLabel = src.FileSizeLabel,
                FileSizeBytes = src.FileSizeBytes,
                FileMime = src.FileMime,
                FilePath = src.FilePath,
                FileRemoteId = src.FileRemoteId,
                FileAccessHash = src.FileAccessHash,
                FileReference = src.FileReference,
                FileDcId = src.FileDcId,
                DownloadedBytes = src.DownloadedBytes,
                TotalBytes = src.TotalBytes,
                IsDownloaded = src.IsDownloaded,
                IsDownloading = src.IsDownloading,
                HasDownloadFailed = src.HasDownloadFailed,
                DownloadProgress = src.DownloadProgress,
                FileIconGlyph = src.FileIconGlyph,
                StickerEmoji = src.StickerEmoji,
                StickerImageSource = src.StickerImageSource,
                ContactName = src.ContactName,
                ContactPhone = src.ContactPhone,
                ContactInitials = src.ContactInitials,
                ContactPhoneUri = src.ContactPhoneUri,
                LocationLabel = src.LocationLabel,
                LocationAddress = src.LocationAddress,
                LocationCoordinates = src.LocationCoordinates,
                LocationMapUri = src.LocationMapUri,
                PollQuestion = src.PollQuestion,
                PollSummary = src.PollSummary,
                PollOptions = src.PollOptions,
                PollTotalVoters = src.PollTotalVoters,
                PollIsClosed = src.PollIsClosed,
                PollCanVote = src.PollCanVote,
                PollShowResults = src.PollShowResults,
                PollVotedOptionIndex = src.PollVotedOptionIndex,
                WebPageSiteName = src.WebPageSiteName,
                WebPageTitle = src.WebPageTitle,
                WebPageDescription = src.WebPageDescription,
                WebPageUrl = src.WebPageUrl,
                WebPageDisplayUrl = src.WebPageDisplayUrl,
                WebPageThumbPath = src.WebPageThumbPath,
                WebPageUri = src.WebPageUri,
                UnsupportedHint = src.UnsupportedHint
            };
        }

        private DateTime _typingExpiresUtc;

        private void ScheduleTypingClear()
        {
            // Soft TTL: every typing event extends the deadline by 6 s.
            // A single Dispatcher-bound delay polls the deadline and
            // clears the indicator once it elapses. Cheap and avoids
            // wiring a dedicated DispatcherTimer.
            _typingExpiresUtc = DateTime.UtcNow.AddSeconds(6);
            var ignored = ClearTypingAfterDelayAsync();
            GC.KeepAlive(ignored);
        }

        private async Task ClearTypingAfterDelayAsync()
        {
            await Task.Delay(TimeSpan.FromSeconds(6)).ConfigureAwait(true);
            if (DateTime.UtcNow < _typingExpiresUtc) return; // a fresh typing extended the deadline
            await Dispatch.OnUiAsync(() =>
            {
                IsTyping = false;
                TypingActionLabel = null;
            }).ConfigureAwait(true);
        }

        // Append the bubble produced by the Sync bridge directly to the
        // visible collection so the user sees
        // the new message without a full reload. The bubble carries
        // only the body that the bridge surfaced — bubble decoration
        // (avatar, author label) reuses the existing EnrichWithAuthor
        // helper.
        private void AppendIncoming(MessagesChangedEventArgs args)
        {
            if (args == null || !args.MessageId.HasValue) return;
            string body = args.Body ?? string.Empty;
            long messageId = args.MessageId.Value;

            // Skip if the visible window already has this id (e.g.
            // ReloadAsync raced ahead). MessageRow exposes
            // ServerId / ClientTempId — confirmed bubbles set ServerId.
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                MessageRow existing = Messages[i];
                if (existing == null) continue;
                if (existing.ServerId.HasValue && existing.ServerId.Value == messageId) return;
            }

            // Synthesize a Message domain object for the row factory.
            // Date is the bridge's UTC at-time; FromUserId comes from
            // the sender slice the decoder surfaced.
            DateTime dateUtc = DateTime.UtcNow;
            long? fromUserId = args.FromUserId.HasValue && args.FromUserId.Value != 0L
                ? args.FromUserId
                : (long?)null;
            Message msg;
            try
            {
                msg = Message.FromServer(
                    _peerKey,
                    messageId,
                    fromUserId,
                    dateUtc,
                    new MessageContentText(body),
                    replyToMessageId: null,
                    isOutgoing: false);
            }
            catch
            {
                // Defensive: if any invariant trips (empty peerKey, etc.)
                // fall back to the legacy reload path so we still show
                // the message.
                var loadIgnore = ReloadAsync(CancellationToken.None);
                return;
            }

            MessageRow row = MessageRow.From(msg, _peerTitle);
            if (row == null) return;

            // Day-separator handling: if the previous bubble is from a
            // different local-day, append a day separator first.
            DateTime localDay = dateUtc.ToLocalTime().Date;
            DateTime? lastBubbleLocalDay = null;
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                MessageRow existing = Messages[i];
                if (existing == null) continue;
                if (existing.Kind == MessageRowKind.DaySeparator) break;
                if (existing.Kind == MessageRowKind.Service) break;
                // Heuristic: TimeLabel is "H:mm" — we can't recover the
                // full date from the row. We accept a possible duplicate
                // separator on day boundaries; pruning would require
                // touching the model. Cheap and visible-only on midnight.
                lastBubbleLocalDay = localDay; // assume same day
                break;
            }
            if (lastBubbleLocalDay == null)
            {
                Messages.Add(MessageRow.CreateDaySeparator(localDay, FormatDayLabel(localDay)));
            }

            // Author label for groups / channels.
            var peerCache = ResolvePeerCache();
            bool isGroup = IsGroupOrChannel(_peerKey);
            long lastFromId = 0L;
            // Walk back through bubbles to find the last author for
            // stacked-bubble rendering. AuthorLabel == "" means it was
            // suppressed already; we can't recover the underlying id, so
            // we play safe and start a fresh run by passing 0.
            EnrichWithAuthor(row, msg, peerCache, isGroup, ref lastFromId);
            Messages.Add(row);

            // A new bubble cancels any pending typing indicator from
            // this peer — they sent the thing they were typing.
            IsTyping = false;
            TypingActionLabel = null;
        }

        private void AppendPending(MessagesChangedEventArgs args)
        {
            if (args == null || !args.ClientTempId.HasValue) return;
            long tempId = args.ClientTempId.Value;
            for (int i = Messages.Count - 1; i >= 0; i--)
            {
                MessageRow existing = Messages[i];
                if (existing == null) continue;
                if (existing.ClientTempId.HasValue && existing.ClientTempId.Value == tempId) return;
            }

            Message msg;
            try
            {
                msg = Message.NewOptimistic(_peerKey, tempId, args.Body ?? string.Empty, null, DateTime.UtcNow);
            }
            catch
            {
                var loadIgnore = ReloadAsync(CancellationToken.None);
                return;
            }

            MessageRow row = MessageRow.From(msg, _peerTitle);
            if (row == null) return;

            if (Messages.Count == 0)
            {
                DateTime localDay = DateTime.UtcNow.ToLocalTime().Date;
                Messages.Add(MessageRow.CreateDaySeparator(localDay, FormatDayLabel(localDay)));
            }
            Messages.Add(row);
        }

        private void ConfirmPending(long clientTempId, long serverId)
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                MessageRow row = Messages[i];
                if (row == null) continue;
                if (row.ServerId.HasValue && row.ServerId.Value == serverId) return;
                if (!row.ClientTempId.HasValue || row.ClientTempId.Value != clientTempId) continue;

                MessageRow updated = CloneRow(row);
                updated.ServerId = serverId;
                updated.ClientTempId = null;
                updated.StatusLabel = "\u2713";
                Messages[i] = updated;
                return;
            }

            var loadIgnore = ReloadAsync(CancellationToken.None);
        }

        private void MarkPendingFailed(long clientTempId)
        {
            for (int i = 0; i < Messages.Count; i++)
            {
                MessageRow row = Messages[i];
                if (row == null) continue;
                if (!row.ClientTempId.HasValue || row.ClientTempId.Value != clientTempId) continue;

                MessageRow updated = CloneRow(row);
                updated.StatusLabel = "!";
                Messages[i] = updated;
                return;
            }
        }

        // ---- Data load ----------------------------------------------

        public async Task LoadInitialAsync(CancellationToken ct)
        {
            ErrorMessage = null;

            if (_messages == null)
            {
                ErrorMessage = "Messages service not available.";
                return;
            }

            // Avatar hydration fires in parallel with the history load.
            // The fetcher's bitmap cache is keyed by photoId, so the
            // common case (peer already seen in dialog list) is a
            // synchronous in-memory lookup and the header paints with
            // the photo before LoadInitial returns.
            var avatarIgnore = HydratePeerAvatarAsync(ct);

            if (ct.IsCancellationRequested) return;
            await TryApplyCachedPageAsync(ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested) return;
            StartLatestRefresh();
        }

        /// <summary>
        /// Resolves the small (160×160) avatar for this chat's peer via
        /// the shared <see cref="Vianigram.App.Services.AvatarResolver"/>.
        /// Best-effort: any failure leaves the initials placeholder
        /// alone.
        /// </summary>
        private async Task HydratePeerAvatarAsync(CancellationToken ct)
        {
            try
            {
                if (string.IsNullOrEmpty(_peerKey)) return;
                Windows.UI.Xaml.Media.ImageSource bmp =
                    await Vianigram.App.Services.AvatarResolver
                        .TryResolveSmallAsync(_peerKey, ct)
                        .ConfigureAwait(true);
                if (bmp != null) PeerAvatarImage = bmp;
            }
            catch (OperationCanceledException) { /* navigated away */ }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Warn(
                    "HydratePeerAvatarAsync threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        public async Task LoadOlderAsync(CancellationToken ct)
        {
            if (_messages == null || !_hasMoreOlder || _oldestKnownMessageId == null) return;
            // Re-entry guard. ScrollViewer.ViewChanged fires multiple times
            // per scroll gesture; we only want one paginated request in flight.
            if (_isLoadingOlder) return;

            ErrorMessage = null;
            IsLoadingOlder = true;
            try
            {
                Result<MessagePage, MessageError> result;
                try
                {
                    result = await _messages.LoadHistoryAsync(_peerKey, _oldestKnownMessageId, PageSize, ct)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    AppLog.For("App.ChatPage").Error("LoadOlderAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                PrependPage(result.Value);
            }
            finally
            {
                IsLoadingOlder = false;
            }
        }

        private Task ReloadAsync(CancellationToken ct)
        {
            return RefreshLatestAsync(ct);
        }

        private async Task<bool> TryApplyCachedPageAsync(CancellationToken ct)
        {
            if (_messages == null)
            {
                ErrorMessage = "Messages service not available.";
                return false;
            }

            Result<MessagePage, MessageError> result;
            try
            {
                result = await _messages.GetCachedHistoryAsync(_peerKey, null, PageSize, ct)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Error("GetCachedHistoryAsync threw: " + ex);
                return false;
            }

            if (result.IsFail) return false;
            if (result.Value == null || result.Value.Messages == null || result.Value.Messages.Count == 0) return false;

            ApplyPage(result.Value);
            return true;
        }

        private void StartLatestRefresh()
        {
            CancelRefresh();
            _refreshCts = new CancellationTokenSource();
            CancellationToken token = _refreshCts.Token;
            var ignored = RefreshLatestAsync(token);
            GC.KeepAlive(ignored);
        }

        private void CancelRefresh()
        {
            CancellationTokenSource cts = _refreshCts;
            if (cts == null) return;
            _refreshCts = null;
            try { cts.Cancel(); } catch { }
        }

        private async Task RefreshLatestAsync(CancellationToken ct)
        {
            ErrorMessage = null;

            if (_messages == null)
            {
                if (Messages.Count == 0) ErrorMessage = "Messages service not available.";
                return;
            }

            Result<MessagePage, MessageError> network;
            try
            {
                network = await _messages.LoadHistoryAsync(_peerKey, null, PageSize, ct)
                    .ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Error("LoadHistoryAsync threw: " + ex);
                if (Messages.Count == 0) ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (ct.IsCancellationRequested) return;

            if (network.IsFail)
            {
                if (Messages.Count == 0) ErrorMessage = FormatError(network.Error);
                return;
            }

            MessagePage page = network.Value;
            try
            {
                Result<MessagePage, MessageError> cached =
                    await _messages.GetCachedHistoryAsync(_peerKey, null, PageSize, ct).ConfigureAwait(true);
                if (!cached.IsFail && cached.Value != null && cached.Value.Messages != null && cached.Value.Messages.Count > 0)
                    page = cached.Value;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Warn("GetCachedHistoryAsync after refresh threw: " + ex.GetType().Name);
            }

            if (ct.IsCancellationRequested) return;
            ApplyFreshPage(page);
        }

        // ---- Send ---------------------------------------------------

        public async Task<bool> SendAsync(CancellationToken ct)
        {
            if (_messages == null)
            {
                ErrorMessage = "Messages service not available.";
                return false;
            }

            var text = _composerText;
            if (string.IsNullOrWhiteSpace(text)) return false;

            ErrorMessage = null;
            IsBusy = true;
            try
            {
                Result<long, MessageError> result;
                try
                {
                    // SendTextAsync returns immediately with the negative
                    // client-temp id (M1 budget). The MessagesChanged event
                    // delivers the optimistic insert + later confirmation.
                    result = await _messages.SendTextAsync(_peerKey, text, null, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.ChatPage").Error("SendTextAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return false;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return false;
                }

                ComposerText = string.Empty;
                return true;
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---- Projection ---------------------------------------------

        private void ApplyPage(MessagePage page)
        {
            IList<MessageRow> rows = BuildRows(page);
            ReplaceRows(rows);
            UpdatePageState(page);
        }

        private void ApplyFreshPage(MessagePage page)
        {
            IList<MessageRow> rows = BuildRows(page);
            UpdatePageState(page);
            if (TryUpdateSameShapeRows(rows)) return;
            if (TryAppendTail(rows)) return;
            ReplaceRows(rows);
        }

        private IList<MessageRow> BuildRows(MessagePage page)
        {
            var built = new List<MessageRow>();
            if (page == null || page.Messages == null) return built;

            var peerCache = ResolvePeerCache();
            bool isGroup = IsGroupOrChannel(_peerKey);

            // page.Messages is newest-first; flip to oldest-first for visual order.
            var snapshot = new List<Message>(page.Messages);
            snapshot.Reverse();
            DateTime? lastDay = null;
            long lastFromId = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var m = snapshot[i];
                if (m == null) continue;

                // Inject day separator when the day changes.
                DateTime localDay = m.Date.ToLocalTime().Date;
                if (lastDay == null || localDay != lastDay.Value)
                {
                    built.Add(MessageRow.CreateDaySeparator(localDay, FormatDayLabel(localDay)));
                    lastDay = localDay;
                    lastFromId = 0; // first row of a new day always shows author
                }

                var row = MessageRow.From(m, _peerTitle);
                if (row != null)
                {
                    EnrichWithAuthor(row, m, peerCache, isGroup, ref lastFromId);
                    built.Add(row);
                }
            }

            return built;
        }

        private void UpdatePageState(MessagePage page)
        {
            if (page == null || page.Messages == null)
            {
                HasMoreOlder = false;
                _oldestKnownMessageId = null;
                return;
            }

            HasMoreOlder = page.HasMoreOlder;
            _oldestKnownMessageId = page.OldestKnownMessageId;
        }

        private void ReplaceRows(IList<MessageRow> rows)
        {
            MessageRowCollection bulk = Messages as MessageRowCollection;
            if (bulk != null)
            {
                bulk.ReplaceWith(rows);
                return;
            }

            Messages.Clear();
            if (rows == null) return;
            for (int i = 0; i < rows.Count; i++)
                Messages.Add(rows[i]);
        }

        private bool TryUpdateSameShapeRows(IList<MessageRow> rows)
        {
            if (rows == null) return false;
            if (Messages.Count != rows.Count) return false;

            for (int i = 0; i < rows.Count; i++)
            {
                if (!string.Equals(RowKey(Messages[i]), RowKey(rows[i]), StringComparison.Ordinal))
                    return false;
            }

            for (int i = 0; i < rows.Count; i++)
            {
                if (!AreRowsEquivalent(Messages[i], rows[i]))
                    Messages[i] = rows[i];
            }
            return true;
        }

        private bool TryAppendTail(IList<MessageRow> rows)
        {
            if (rows == null) return false;
            int existingCount = Messages.Count;
            if (existingCount == 0 || existingCount >= rows.Count) return false;

            for (int i = 0; i < existingCount; i++)
            {
                if (!string.Equals(RowKey(Messages[i]), RowKey(rows[i]), StringComparison.Ordinal))
                    return false;
            }

            for (int i = 0; i < existingCount; i++)
            {
                if (!AreRowsEquivalent(Messages[i], rows[i]))
                    Messages[i] = rows[i];
            }

            for (int i = existingCount; i < rows.Count; i++)
                Messages.Add(rows[i]);
            return true;
        }

        private static string RowKey(MessageRow row)
        {
            if (row == null) return "null";
            if (row.ServerId.HasValue)
                return "s:" + row.ServerId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (row.ClientTempId.HasValue)
                return "c:" + row.ClientTempId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (row.Kind == MessageRowKind.DaySeparator)
                return "d:" + (row.Text ?? string.Empty);
            return "r:" + ((int)row.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":" + (row.TimeLabel ?? string.Empty)
                + ":" + (row.Text ?? string.Empty);
        }

        private static bool AreRowsEquivalent(MessageRow a, MessageRow b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return a.ServerId == b.ServerId
                && a.ClientTempId == b.ClientTempId
                && a.Kind == b.Kind
                && string.Equals(a.Text, b.Text, StringComparison.Ordinal)
                && a.IsOutgoing == b.IsOutgoing
                && string.Equals(a.TimeLabel, b.TimeLabel, StringComparison.Ordinal)
                && string.Equals(a.StatusLabel, b.StatusLabel, StringComparison.Ordinal)
                && a.HasReply == b.HasReply
                && string.Equals(a.ReplyAuthorLabel, b.ReplyAuthorLabel, StringComparison.Ordinal)
                && string.Equals(a.ReplyPreviewText, b.ReplyPreviewText, StringComparison.Ordinal)
                && SameEntities(a.TextEntities, b.TextEntities)
                && SameEntities(a.CaptionEntities, b.CaptionEntities)
                && string.Equals(a.ReactionSummary, b.ReactionSummary, StringComparison.Ordinal)
                && string.Equals(a.AuthorLabel, b.AuthorLabel, StringComparison.Ordinal)
                && a.ShowAuthor == b.ShowAuthor
                && string.Equals(a.MediaCaption, b.MediaCaption, StringComparison.Ordinal)
                && a.MediaWidth == b.MediaWidth
                && a.MediaHeight == b.MediaHeight
                && string.Equals(a.MediaThumbPath, b.MediaThumbPath, StringComparison.Ordinal)
                && string.Equals(a.MediaFullPath, b.MediaFullPath, StringComparison.Ordinal)
                && string.Equals(a.MediaSource, b.MediaSource, StringComparison.Ordinal)
                && SameBytes(a.MediaPreviewBytes, b.MediaPreviewBytes)
                && a.MediaLocationKind == b.MediaLocationKind
                && a.MediaFileType == b.MediaFileType
                && a.MediaRemoteId == b.MediaRemoteId
                && a.MediaAccessHash == b.MediaAccessHash
                && SameBytes(a.MediaFileReference, b.MediaFileReference)
                && a.MediaDcId == b.MediaDcId
                && a.MediaSizeBytes == b.MediaSizeBytes
                && string.Equals(a.MediaMime, b.MediaMime, StringComparison.Ordinal)
                && string.Equals(a.MediaFileName, b.MediaFileName, StringComparison.Ordinal)
                && string.Equals(a.MediaThumbSize, b.MediaThumbSize, StringComparison.Ordinal)
                && string.Equals(a.MediaTypeBadge, b.MediaTypeBadge, StringComparison.Ordinal)
                && a.IsMediaLoading == b.IsMediaLoading
                && a.HasMediaFailed == b.HasMediaFailed
                && Math.Abs(a.MediaDownloadProgress - b.MediaDownloadProgress) < 0.001
                && string.Equals(a.DurationLabel, b.DurationLabel, StringComparison.Ordinal)
                && a.DurationSeconds == b.DurationSeconds
                && a.ElapsedSeconds == b.ElapsedSeconds
                && a.IsPlaying == b.IsPlaying
                && string.Equals(a.AudioSource, b.AudioSource, StringComparison.Ordinal)
                && SameBytes(a.WaveformData, b.WaveformData)
                && string.Equals(a.AudioTitle, b.AudioTitle, StringComparison.Ordinal)
                && string.Equals(a.AudioPerformer, b.AudioPerformer, StringComparison.Ordinal)
                && string.Equals(a.FileName, b.FileName, StringComparison.Ordinal)
                && string.Equals(a.FileSizeLabel, b.FileSizeLabel, StringComparison.Ordinal)
                && a.FileSizeBytes == b.FileSizeBytes
                && string.Equals(a.FileMime, b.FileMime, StringComparison.Ordinal)
                && string.Equals(a.FilePath, b.FilePath, StringComparison.Ordinal)
                && a.FileRemoteId == b.FileRemoteId
                && a.FileAccessHash == b.FileAccessHash
                && SameBytes(a.FileReference, b.FileReference)
                && a.FileDcId == b.FileDcId
                && a.DownloadedBytes == b.DownloadedBytes
                && a.TotalBytes == b.TotalBytes
                && a.IsDownloaded == b.IsDownloaded
                && a.IsDownloading == b.IsDownloading
                && a.HasDownloadFailed == b.HasDownloadFailed
                && Math.Abs(a.DownloadProgress - b.DownloadProgress) < 0.001
                && string.Equals(a.FileIconGlyph, b.FileIconGlyph, StringComparison.Ordinal)
                && string.Equals(a.StickerEmoji, b.StickerEmoji, StringComparison.Ordinal)
                && string.Equals(a.StickerImageSource, b.StickerImageSource, StringComparison.Ordinal)
                && string.Equals(a.ContactName, b.ContactName, StringComparison.Ordinal)
                && string.Equals(a.ContactPhone, b.ContactPhone, StringComparison.Ordinal)
                && string.Equals(a.ContactInitials, b.ContactInitials, StringComparison.Ordinal)
                && SameUri(a.ContactPhoneUri, b.ContactPhoneUri)
                && string.Equals(a.LocationLabel, b.LocationLabel, StringComparison.Ordinal)
                && string.Equals(a.LocationAddress, b.LocationAddress, StringComparison.Ordinal)
                && string.Equals(a.LocationCoordinates, b.LocationCoordinates, StringComparison.Ordinal)
                && SameUri(a.LocationMapUri, b.LocationMapUri)
                && string.Equals(a.PollQuestion, b.PollQuestion, StringComparison.Ordinal)
                && string.Equals(a.PollSummary, b.PollSummary, StringComparison.Ordinal)
                && SamePollOptions(a.PollOptions, b.PollOptions)
                && a.PollTotalVoters == b.PollTotalVoters
                && a.PollIsClosed == b.PollIsClosed
                && a.PollCanVote == b.PollCanVote
                && a.PollShowResults == b.PollShowResults
                && a.PollVotedOptionIndex == b.PollVotedOptionIndex
                && string.Equals(a.WebPageSiteName, b.WebPageSiteName, StringComparison.Ordinal)
                && string.Equals(a.WebPageTitle, b.WebPageTitle, StringComparison.Ordinal)
                && string.Equals(a.WebPageDescription, b.WebPageDescription, StringComparison.Ordinal)
                && string.Equals(a.WebPageUrl, b.WebPageUrl, StringComparison.Ordinal)
                && string.Equals(a.WebPageDisplayUrl, b.WebPageDisplayUrl, StringComparison.Ordinal)
                && string.Equals(a.WebPageThumbPath, b.WebPageThumbPath, StringComparison.Ordinal)
                && SameUri(a.WebPageUri, b.WebPageUri)
                && string.Equals(a.UnsupportedHint, b.UnsupportedHint, StringComparison.Ordinal);
        }

        private static bool SameEntities(IList<MessageEntity> a, IList<MessageEntity> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                MessageEntity ea = a[i];
                MessageEntity eb = b[i];
                if (ReferenceEquals(ea, eb)) continue;
                if (ea == null || eb == null) return false;
                if (ea.Kind != eb.Kind || ea.Offset != eb.Offset || ea.Length != eb.Length) return false;
                if (!string.Equals(ea.Url, eb.Url, StringComparison.Ordinal)) return false;
                if (!string.Equals(ea.Language, eb.Language, StringComparison.Ordinal)) return false;
                if (!string.Equals(ea.CustomEmojiId, eb.CustomEmojiId, StringComparison.Ordinal)) return false;
            }
            return true;
        }

        private static bool SamePollOptions(IList<PollOptionView> a, IList<PollOptionView> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                PollOptionView oa = a[i];
                PollOptionView ob = b[i];
                if (ReferenceEquals(oa, ob)) continue;
                if (oa == null || ob == null) return false;
                if (!string.Equals(oa.Text, ob.Text, StringComparison.Ordinal)) return false;
                if (oa.Percent != ob.Percent || oa.Votes != ob.Votes || oa.Voted != ob.Voted) return false;
            }
            return true;
        }

        private static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] != b[i]) return false;
            return true;
        }

        private static bool SameUri(Uri a, Uri b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return string.Equals(a.OriginalString, b.OriginalString, StringComparison.Ordinal);
        }

        private void PrependPage(MessagePage page)
        {
            if (page == null || page.Messages == null) return;

            var peerCache = ResolvePeerCache();
            bool isGroup = IsGroupOrChannel(_peerKey);

            // The page is newest-first; we want oldest-first for visual order
            // and we insert at index 0 of the existing collection. To keep
            // separators consistent, we build a temporary list with the right
            // day separators between the new messages, then insert that list
            // before whatever was already on screen. We also re-evaluate the
            // separator at the boundary between the prepended page and the
            // first existing message (it might no longer be needed if the
            // last new message and the first existing one fall on the same
            // day, but for simplicity we accept the duplicate; pruning it
            // would require touching the existing collection at offset 0
            // which complicates the scroll-restore math).
            var snapshot = new List<Message>(page.Messages);
            snapshot.Reverse();
            var built = new List<MessageRow>();
            DateTime? lastDay = null;
            long lastFromId = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var m = snapshot[i];
                if (m == null) continue;

                DateTime localDay = m.Date.ToLocalTime().Date;
                if (lastDay == null || localDay != lastDay.Value)
                {
                    built.Add(MessageRow.CreateDaySeparator(localDay, FormatDayLabel(localDay)));
                    lastDay = localDay;
                    lastFromId = 0;
                }
                var row = MessageRow.From(m, _peerTitle);
                if (row != null)
                {
                    EnrichWithAuthor(row, m, peerCache, isGroup, ref lastFromId);
                    built.Add(row);
                }
            }
            for (int i = built.Count - 1; i >= 0; i--)
            {
                Messages.Insert(0, built[i]);
            }

            HasMoreOlder = page.HasMoreOlder;
            _oldestKnownMessageId = page.OldestKnownMessageId;
        }

        private static void EnrichWithAuthor(
            MessageRow row, Message m,
            Vianigram.Composition.Infrastructure.IPeerCache peerCache,
            bool isGroup, ref long lastFromId)
        {
            if (!isGroup || m.IsOutgoing || row.Kind == MessageRowKind.Service)
            {
                row.ShowAuthor = false;
                row.AuthorLabel = string.Empty;
                lastFromId = 0;
                return;
            }
            long fromId = m.FromUserId.GetValueOrDefault(0);
            if (fromId <= 0)
            {
                row.ShowAuthor = false;
                row.AuthorLabel = string.Empty;
                return;
            }
            // Only label the *first* message in a sender's run — Telegram-
            // style "stacked bubbles". When N consecutive messages come
            // from the same author, only the topmost one shows the name.
            if (fromId == lastFromId)
            {
                row.ShowAuthor = false;
                row.AuthorLabel = string.Empty;
                return;
            }
            string name = peerCache != null ? peerCache.GetUserDisplayName(fromId) : string.Empty;
            if (string.IsNullOrEmpty(name)) name = "user " + fromId;
            row.AuthorLabel = name;
            row.ShowAuthor = true;
            lastFromId = fromId;
        }

        private static Vianigram.Composition.Infrastructure.IPeerCache ResolvePeerCache()
        {
            if (App.Composition == null) return null;
            Vianigram.Composition.Infrastructure.IPeerCache cache;
            App.Composition.TryResolve<Vianigram.Composition.Infrastructure.IPeerCache>(out cache);
            return cache;
        }

        // Mirrors the PeerAvatarFetcher pattern in ChatListPageViewModel —
        private static Vianigram.App.Services.DocumentFileFetcher _documentFetcher;
        private static readonly object _documentFetcherGate = new object();
        private static Vianigram.App.Services.ProgressiveMediaFileFetcher _progressiveMediaFetcher;
        private static readonly object _progressiveMediaFetcherGate = new object();

        private static Vianigram.App.Services.DocumentFileFetcher ResolveDocumentFetcher()
        {
            if (_documentFetcher != null) return _documentFetcher;
            lock (_documentFetcherGate)
            {
                if (_documentFetcher != null) return _documentFetcher;
                if (App.Composition == null) return null;
                Vianigram.Media.Ports.Inbound.IMediaApi media;
                Vianigram.Media.Ports.Outbound.IMediaCache cache;
                if (!App.Composition.TryResolve<Vianigram.Media.Ports.Inbound.IMediaApi>(out media) || media == null) return null;
                if (!App.Composition.TryResolve<Vianigram.Media.Ports.Outbound.IMediaCache>(out cache) || cache == null) return null;
                var log = AppLog.For("App.DocumentFileFetcher");
                _documentFetcher = new Vianigram.App.Services.DocumentFileFetcher(media, cache, log);
                return _documentFetcher;
            }
        }

        private static Vianigram.App.Services.ProgressiveMediaFileFetcher ResolveProgressiveMediaFetcher()
        {
            if (_progressiveMediaFetcher != null) return _progressiveMediaFetcher;
            lock (_progressiveMediaFetcherGate)
            {
                if (_progressiveMediaFetcher != null) return _progressiveMediaFetcher;
                if (App.Composition == null) return null;
                Vianigram.Media.Ports.Inbound.IMediaApi media;
                Vianigram.Media.Ports.Outbound.IMediaCache cache;
                if (!App.Composition.TryResolve<Vianigram.Media.Ports.Inbound.IMediaApi>(out media) || media == null) return null;
                if (!App.Composition.TryResolve<Vianigram.Media.Ports.Outbound.IMediaCache>(out cache) || cache == null) return null;
                var log = AppLog.For("App.ProgressiveMedia");
                _progressiveMediaFetcher = new Vianigram.App.Services.ProgressiveMediaFileFetcher(media, cache, log);
                return _progressiveMediaFetcher;
            }
        }

        /// <summary>
        /// If <paramref name="row"/> is a photo bubble that ships only a
        /// stripped preview (no
        /// MediaSource / MediaFullPath), kick off a fire-and-forget
        /// download for a sharp ~800 px thumbnail via
        /// <see cref="MessageThumbnailFetcher"/>. When the download
        /// completes we look up the row in <see cref="Messages"/> by
        /// reference and replace its slot with a clone whose
        /// <c>MediaSource</c> points at the local cached JPEG —
        /// <c>PhotoBubble</c> swaps from the blurry stripped preview to
        /// the sharp thumb without further intervention.
        /// </summary>
        private void MaybeKickPhotoThumbFetch(MessageRow row, Message m)
        {
            // Message media downloads are explicit user actions only.
        }

        private static Task FetchThumbAndAssignAsync()
        {
            return Task.FromResult<object>(null);
#if false
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(lifecycleToken, timeout.Token))
                {
                    string path = await fetcher.FetchAsync(file, thumbs, linked.Token).ConfigureAwait(true);
                    if (linked.IsCancellationRequested) return;
                    if (string.IsNullOrEmpty(path)) return;
                    if (Messages == null) return;
                    for (int i = 0; i < Messages.Count; i++)
                    {
                        MessageRow existing = Messages[i];
                        if (!ReferenceEquals(existing, targetRow)) continue;
                        if (linked.IsCancellationRequested) return;
                        // Reference-equal slot — clone, mutate, replace
                        // so the ObservableCollection fires Replace and
                        // PhotoBubble re-evaluates ImageSource.
                        MessageRow updated = CloneRow(existing);
                        updated.MediaSource = path;
                        updated.MediaFullPath = path;
                        updated.IsMediaLoading = false;
                        updated.HasMediaFailed = false;
                        updated.MediaDownloadProgress = 100.0;
                        Messages[i] = updated;
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Info(
                    "thumb.fetch swallowed " + ex.GetType().Name + ": " + ex.Message);
            }
#endif
        }

        public async Task<string> DownloadDocumentAsync(MessageRow row, CancellationToken ct)
        {
            if (row == null) return null;
            if (!string.IsNullOrEmpty(row.FilePath)) return row.FilePath;

            Vianigram.App.Services.DocumentFileFetcher fetcher = ResolveDocumentFetcher();
            if (fetcher == null ||
                row.FileRemoteId == 0L ||
                row.FileAccessHash == 0L ||
                row.FileDcId <= 0)
            {
                ReplaceDocumentRow(row, null, false, false, true, 0.0);
                return null;
            }

            ReplaceDocumentRow(row, null, false, true, false, 0.0);

            string path = null;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
                {
                    path = await fetcher.FetchAsync(
                        row.FileRemoteId,
                        row.FileAccessHash,
                        row.FileReference,
                        row.FileDcId,
                        row.FileName,
                        row.FileMime,
                        row.TotalBytes > 0 ? row.TotalBytes : row.FileSizeBytes,
                        linked.Token).ConfigureAwait(true);
                }
            }
            catch (OperationCanceledException)
            {
                path = null;
            }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Info(
                    "doc.fetch swallowed " + ex.GetType().Name + ": " + ex.Message);
                path = null;
            }

            if (string.IsNullOrEmpty(path))
            {
                ReplaceDocumentRow(row, null, false, false, true, 0.0);
                return null;
            }

            ReplaceDocumentRow(row, path, true, false, false, 100.0);
            return path;
        }

        public async Task<string> BufferMediaAsync(MessageRow row, CancellationToken ct)
        {
            if (row == null) return null;
            if (IsAudioRow(row) && !string.IsNullOrEmpty(row.AudioSource)) return row.AudioSource;
            if (IsVisualMediaRow(row) && !string.IsNullOrEmpty(row.MediaFullPath)) return row.MediaFullPath;

            if (!CanProgressivelyBuffer(row))
                return await DownloadMediaAsync(row, ct).ConfigureAwait(true);

            Vianigram.App.Services.ProgressiveMediaFileFetcher fetcher = ResolveProgressiveMediaFetcher();
            if (fetcher == null)
                return await DownloadMediaAsync(row, ct).ConfigureAwait(true);

            ReplaceMediaRow(row, null, true, false, 0.0);

            Vianigram.App.Services.ProgressiveMediaFetchResult result = null;
            CancellationTokenSource timeout = null;
            CancellationTokenSource linked = null;
            try
            {
                timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
                result = await fetcher.FetchDocumentMediaAsync(
                    row.MediaRemoteId,
                    row.MediaAccessHash,
                    row.MediaFileReference,
                    row.MediaDcId,
                    row.MediaFileName,
                    row.MediaMime,
                    row.MediaSizeBytes,
                    ToFileType(row.MediaFileType),
                    p => ApplyProgressiveMediaProgress(row, p),
                    linked.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                result = null;
            }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Info(
                    "media.buffer swallowed " + ex.GetType().Name + ": " + ex.Message);
                result = null;
            }
            finally
            {
                if (result == null || result.Completion == null || result.IsComplete)
                {
                    if (linked != null) linked.Dispose();
                    if (timeout != null) timeout.Dispose();
                }
                else
                {
                    DisposeWhenComplete(result.Completion, linked, timeout);
                }
            }

            if (result == null || string.IsNullOrEmpty(result.LocalPath))
            {
                ReplaceMediaRow(row, null, false, true, 0.0);
                return null;
            }

            // Once a playable buffer exists, make the row feel playable.
            // Background range fetching continues through result.Completion.
            ReplaceMediaRow(row, result.LocalPath, false, false, 100.0);
            TrackProgressiveCompletion(row, result);
            return result.LocalPath;
        }

        public async Task<string> DownloadMediaAsync(MessageRow row, CancellationToken ct)
        {
            if (row == null) return null;
            if (IsAudioRow(row) && !string.IsNullOrEmpty(row.AudioSource)) return row.AudioSource;
            if (IsVisualMediaRow(row) && !string.IsNullOrEmpty(row.MediaFullPath)) return row.MediaFullPath;

            Vianigram.App.Services.DocumentFileFetcher fetcher = ResolveDocumentFetcher();
            if (fetcher == null ||
                row.MediaLocationKind == MessageMediaLocationKind.None ||
                row.MediaRemoteId == 0L ||
                row.MediaAccessHash == 0L ||
                row.MediaDcId <= 0)
            {
                ReplaceMediaRow(row, null, false, true, 0.0);
                return null;
            }

            ReplaceMediaRow(row, null, true, false, 0.0);

            string path = null;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(8)))
                using (var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token))
                {
                    if (row.MediaLocationKind == MessageMediaLocationKind.Photo)
                    {
                        path = await fetcher.FetchPhotoAsync(
                            row.MediaRemoteId,
                            row.MediaAccessHash,
                            row.MediaFileReference,
                            row.MediaDcId,
                            row.MediaThumbSize,
                            row.MediaSizeBytes,
                            linked.Token).ConfigureAwait(true);
                    }
                    else
                    {
                        FileType type = ToFileType(row.MediaFileType);
                        path = await fetcher.FetchDocumentMediaAsync(
                            row.MediaRemoteId,
                            row.MediaAccessHash,
                            row.MediaFileReference,
                            row.MediaDcId,
                            row.MediaFileName,
                            row.MediaMime,
                            row.MediaSizeBytes,
                            type,
                            linked.Token).ConfigureAwait(true);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                path = null;
            }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Info(
                    "media.fetch swallowed " + ex.GetType().Name + ": " + ex.Message);
                path = null;
            }

            if (string.IsNullOrEmpty(path))
            {
                ReplaceMediaRow(row, null, false, true, 0.0);
                return null;
            }

            ReplaceMediaRow(row, path, false, false, 100.0);
            return path;
        }

        private void ApplyProgressiveMediaProgress(
            MessageRow row,
            Vianigram.App.Services.ProgressiveMediaProgress progress)
        {
            if (row == null || progress == null) return;

            double percent = progress.Percent;
            if (progress.IsPlayable)
            {
                // Playable means we should show the normal play affordance,
                // not an endless download spinner.
                percent = 100.0;
            }

            var ignored = Dispatch.OnUiAsync(() =>
            {
                string path = progress.IsPlayable ? progress.LocalPath : null;
                ReplaceMediaRow(row, path, !progress.IsPlayable, false, percent);
            });
            GC.KeepAlive(ignored);
        }

        private void TrackProgressiveCompletion(
            MessageRow row,
            Vianigram.App.Services.ProgressiveMediaFetchResult result)
        {
            if (row == null || result == null || result.Completion == null || result.IsComplete)
                return;

            var ignored = CompleteProgressiveMediaAsync(row, result);
            GC.KeepAlive(ignored);
        }

        private static void DisposeWhenComplete(
            Task completion,
            CancellationTokenSource linked,
            CancellationTokenSource timeout)
        {
            if (completion == null)
            {
                if (linked != null) linked.Dispose();
                if (timeout != null) timeout.Dispose();
                return;
            }

            completion.ContinueWith(delegate
            {
                if (linked != null) linked.Dispose();
                if (timeout != null) timeout.Dispose();
            }, TaskContinuationOptions.ExecuteSynchronously);
        }

        private async Task CompleteProgressiveMediaAsync(
            MessageRow row,
            Vianigram.App.Services.ProgressiveMediaFetchResult result)
        {
            try
            {
                await result.Completion.ConfigureAwait(false);
                await Dispatch.OnUiAsync(() =>
                {
                    ReplaceMediaRow(row, result.LocalPath, false, false, 100.0);
                }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.For("App.ChatPage").Info(
                    "media.buffer completion swallowed " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void ReplaceDocumentRow(
            MessageRow targetRow,
            string filePath,
            bool isDownloaded,
            bool isDownloading,
            bool hasFailed,
            double progress)
        {
            if (targetRow == null || Messages == null) return;
            for (int i = 0; i < Messages.Count; i++)
            {
                MessageRow existing = Messages[i];
                if (!ReferenceEquals(existing, targetRow) &&
                    !string.Equals(RowKey(existing), RowKey(targetRow), StringComparison.Ordinal))
                    continue;

                MessageRow updated = CloneRow(existing);
                if (!string.IsNullOrEmpty(filePath)) updated.FilePath = filePath;
                updated.IsDownloaded = isDownloaded;
                updated.IsDownloading = isDownloading;
                updated.HasDownloadFailed = hasFailed;
                updated.DownloadProgress = progress;
                updated.DownloadedBytes = isDownloaded
                    ? (updated.TotalBytes > 0 ? updated.TotalBytes : updated.FileSizeBytes)
                    : 0L;
                Messages[i] = updated;
                return;
            }
        }

        private void ReplaceMediaRow(
            MessageRow targetRow,
            string filePath,
            bool isDownloading,
            bool hasFailed,
            double progress)
        {
            if (targetRow == null || Messages == null) return;
            for (int i = 0; i < Messages.Count; i++)
            {
                MessageRow existing = Messages[i];
                if (!ReferenceEquals(existing, targetRow) &&
                    !string.Equals(RowKey(existing), RowKey(targetRow), StringComparison.Ordinal))
                    continue;

                MessageRow updated = CloneRow(existing);
                if (IsAudioRow(updated))
                {
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        updated.AudioSource = filePath;
                        updated.FilePath = filePath;
                        updated.IsDownloaded = true;
                    }
                    updated.IsDownloading = isDownloading;
                    updated.HasDownloadFailed = hasFailed;
                    updated.DownloadProgress = progress;
                    updated.DownloadedBytes = !string.IsNullOrEmpty(filePath)
                        ? (updated.MediaSizeBytes > 0 ? updated.MediaSizeBytes : updated.TotalBytes)
                        : 0L;
                }
                else
                {
                    if (!string.IsNullOrEmpty(filePath))
                    {
                        updated.MediaFullPath = filePath;
                        if (updated.Kind == MessageRowKind.Photo)
                            updated.MediaSource = filePath;
                    }
                    updated.IsMediaLoading = isDownloading;
                    updated.HasMediaFailed = hasFailed;
                    updated.MediaDownloadProgress = progress;
                }

                Messages[i] = updated;
                return;
            }
        }

        private static bool IsVisualMediaRow(MessageRow row)
        {
            return row != null &&
                (row.Kind == MessageRowKind.Photo ||
                 row.Kind == MessageRowKind.Video ||
                 row.Kind == MessageRowKind.VideoNote ||
                 row.Kind == MessageRowKind.Animation);
        }

        private static bool IsAudioRow(MessageRow row)
        {
            return row != null &&
                (row.Kind == MessageRowKind.Voice ||
                 row.Kind == MessageRowKind.Audio);
        }

        private static bool CanProgressivelyBuffer(MessageRow row)
        {
            if (row == null) return false;
            if (row.MediaLocationKind != MessageMediaLocationKind.Document) return false;
            if (row.MediaRemoteId == 0L || row.MediaAccessHash == 0L || row.MediaDcId <= 0) return false;
            return row.Kind == MessageRowKind.Video ||
                   row.Kind == MessageRowKind.VideoNote ||
                   row.Kind == MessageRowKind.Animation ||
                   row.Kind == MessageRowKind.Voice ||
                   row.Kind == MessageRowKind.Audio;
        }

        private static FileType ToFileType(int value)
        {
            if (value == (int)FileType.Photo) return FileType.Photo;
            if (value == (int)FileType.Video) return FileType.Video;
            if (value == (int)FileType.Voice) return FileType.Voice;
            if (value == (int)FileType.Sticker) return FileType.Sticker;
            if (value == (int)FileType.Document) return FileType.Document;
            return FileType.Document;
        }

        private static bool IsGroupOrChannel(string peerKey)
        {
            return !string.IsNullOrEmpty(peerKey)
                && (peerKey.StartsWith("chat:", StringComparison.Ordinal)
                    || peerKey.StartsWith("channel:", StringComparison.Ordinal));
        }

        private static string FormatDayLabel(DateTime localDay)
        {
            DateTime today = DateTime.Now.Date;
            if (localDay == today) return "today";
            if (localDay == today.AddDays(-1)) return "yesterday";
            // Within the last week → weekday name (e.g. "Mon").
            if ((today - localDay).TotalDays < 7) return localDay.ToString("dddd, MMM d", System.Globalization.CultureInfo.CurrentCulture);
            // Same year → "Mar 14".
            if (localDay.Year == today.Year) return localDay.ToString("MMM d", System.Globalization.CultureInfo.CurrentCulture);
            // Older → "2024-03-14".
            return localDay.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string FormatError(MessageError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Code)
            {
                case MessageErrorCode.NetworkFailed:
                    return "Network error: " + error.Message;
                case MessageErrorCode.Unauthorized:
                    return "Not signed in.";
                case MessageErrorCode.FloodWait:
                    return "Too many requests; " + error.Message + ".";
                default:
                    return string.IsNullOrEmpty(error.Message) ? error.Code.ToString() : error.Message;
            }
        }

        private static string CreateInitials(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "?";

            string[] parts = title.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";

            if (parts.Length == 1)
            {
                string single = parts[0];
                if (single.Length <= 1) return single.ToUpperInvariant();
                return single.Substring(0, 2).ToUpperInvariant();
            }

            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        private static long CreateColorSeed(string peerKey, string title)
        {
            string source = !string.IsNullOrEmpty(peerKey) ? peerKey : (title ?? string.Empty);
            long seed = 17;
            for (int i = 0; i < source.Length; i++)
                seed = (seed * 31) + source[i];
            return seed;
        }

        private sealed class MessageRowCollection : ObservableCollection<MessageRow>
        {
            public void ReplaceWith(IList<MessageRow> rows)
            {
                Items.Clear();
                if (rows != null)
                {
                    for (int i = 0; i < rows.Count; i++)
                        Items.Add(rows[i]);
                }

                OnPropertyChanged(new PropertyChangedEventArgs("Count"));
                OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }
    }

    /// <summary>
    /// Discriminator the ChatPage's <c>MessageBubbleTemplateSelector</c>
    /// reads to pick which DataTemplate to instantiate for a row. Mirrors
    /// the <see cref="MessageContent"/> hierarchy without forcing the UI
    /// layer to inspect domain types directly.
    /// </summary>
    public enum MessageRowKind
    {
        Text = 0,
        Photo = 1,
        Voice = 2,
        Audio = 3,
        Video = 4,
        VideoNote = 5,
        Animation = 6,
        Document = 7,
        Sticker = 8,
        Contact = 9,
        Location = 10,
        Poll = 11,
        WebPage = 12,
        Service = 13,
        Unsupported = 14,
        /// <summary>
        /// Synthetic row injected by the VM between message groups whose
        /// dates fall on different days. Renders as a centered chip with
        /// a relative label (today / yesterday / Mon Apr 14 / 2024-12-03).
        /// </summary>
        DaySeparator = 15
    }

    public enum MessageMediaLocationKind
    {
        None = 0,
        Photo = 1,
        Document = 2
    }

    /// <summary>
    /// Single bubble in ChatPage. Public + sealed so .NET Native can reflect.
    /// Carries every property the bubble templates bind to. Properties not
    /// applicable to the row's <see cref="Kind"/> are left at sensible
    /// defaults (empty string / 0).
    /// </summary>
    public sealed class MessageRow
    {
        public long? ServerId { get; set; }
        public long? ClientTempId { get; set; }
        public MessageRowKind Kind { get; set; }
        public string Text { get; set; }
        public bool IsOutgoing { get; set; }
        public string TimeLabel { get; set; }
        public string StatusLabel { get; set; }
        public bool HasReply { get; set; }
        public string ReplyAuthorLabel { get; set; }
        public string ReplyPreviewText { get; set; }
        public IList<MessageEntity> TextEntities { get; set; }
        public IList<MessageEntity> CaptionEntities { get; set; }
        public string ReactionSummary { get; set; }

        // Author label (groups / channels). Empty for 1-1 DMs and outgoing
        // messages so the UI doesn't show "me · ..." above own bubbles.
        public string AuthorLabel { get; set; }
        public bool ShowAuthor { get; set; }

        // Day-separator rows carry their relative label here. The row's
        // Text field doubles as the chip's caption when Kind=DaySeparator.

        // Photo / Video / VideoNote / Animation
        public string MediaCaption { get; set; }
        public int MediaWidth { get; set; }
        public int MediaHeight { get; set; }
        public string MediaThumbPath { get; set; }
        public string MediaFullPath { get; set; }
        public string MediaSource { get; set; }
        public byte[] MediaPreviewBytes { get; set; }
        public MessageMediaLocationKind MediaLocationKind { get; set; }
        public int MediaFileType { get; set; }
        public long MediaRemoteId { get; set; }
        public long MediaAccessHash { get; set; }
        public byte[] MediaFileReference { get; set; }
        public int MediaDcId { get; set; }
        public long MediaSizeBytes { get; set; }
        public string MediaMime { get; set; }
        public string MediaFileName { get; set; }
        public string MediaThumbSize { get; set; }
        public string MediaTypeBadge { get; set; }    // e.g. "\ud83d\udcf7 photo", "\u25b6 video 0:42", "\ud83c\udf9e GIF"
        public bool IsMediaLoading { get; set; }
        public bool HasMediaFailed { get; set; }
        public double MediaDownloadProgress { get; set; }

        // Voice / Audio
        public string DurationLabel { get; set; }     // "0:42"
        public int DurationSeconds { get; set; }
        public int ElapsedSeconds { get; set; }
        public bool IsPlaying { get; set; }
        public string AudioSource { get; set; }
        public byte[] WaveformData { get; set; }
        public string AudioTitle { get; set; }
        public string AudioPerformer { get; set; }

        // Document
        public string FileName { get; set; }
        public string FileSizeLabel { get; set; }     // "2.4 MB"
        public long FileSizeBytes { get; set; }
        public string FileMime { get; set; }
        public string FilePath { get; set; }
        public long FileRemoteId { get; set; }
        public long FileAccessHash { get; set; }
        public byte[] FileReference { get; set; }
        public int FileDcId { get; set; }
        public long DownloadedBytes { get; set; }
        public long TotalBytes { get; set; }
        public bool IsDownloaded { get; set; }
        public bool IsDownloading { get; set; }
        public bool HasDownloadFailed { get; set; }
        public double DownloadProgress { get; set; }
        public string FileIconGlyph { get; set; }     // single-char or short string for icon

        // Sticker
        public string StickerEmoji { get; set; }
        public string StickerImageSource { get; set; }

        // Contact
        public string ContactName { get; set; }
        public string ContactPhone { get; set; }
        public string ContactInitials { get; set; }
        public Uri ContactPhoneUri { get; set; }

        // Location
        public string LocationLabel { get; set; }     // "venue title" or "lat, lon"
        public string LocationAddress { get; set; }
        public string LocationCoordinates { get; set; }
        public Uri LocationMapUri { get; set; }

        // Poll
        public string PollQuestion { get; set; }
        public string PollSummary { get; set; }       // "3 answers \u00b7 12 voters"
        public ObservableCollection<PollOptionView> PollOptions { get; set; }
        public int PollTotalVoters { get; set; }
        public bool PollIsClosed { get; set; }
        public bool PollCanVote { get; set; }
        public bool PollShowResults { get; set; }
        public int PollVotedOptionIndex { get; set; }

        // WebPage
        public string WebPageSiteName { get; set; }
        public string WebPageTitle { get; set; }
        public string WebPageDescription { get; set; }
        public string WebPageUrl { get; set; }
        public string WebPageDisplayUrl { get; set; }
        public string WebPageThumbPath { get; set; }
        public Uri WebPageUri { get; set; }

        // Unsupported
        public string UnsupportedHint { get; set; }   // ctor id like "0xfdb19008"

        /// <summary>
        /// Builds a synthetic day-separator row. The <paramref name="label"/>
        /// is rendered as a centered chip; the bubble template ignores
        /// IsOutgoing / status / reply fields for this kind.
        /// </summary>
        public static MessageRow CreateDaySeparator(DateTime dayUtc, string label)
        {
            return new MessageRow
            {
                Kind = MessageRowKind.DaySeparator,
                Text = label ?? string.Empty,
                IsOutgoing = false,
                TimeLabel = string.Empty,
                StatusLabel = string.Empty,
                HasReply = false,
                ReplyAuthorLabel = string.Empty,
                ReplyPreviewText = string.Empty,
                TextEntities = EmptyEntities(),
                CaptionEntities = EmptyEntities(),
                ReactionSummary = string.Empty,
                AuthorLabel = string.Empty,
                ShowAuthor = false,
                MediaCaption = string.Empty,
                MediaThumbPath = string.Empty,
                MediaFullPath = string.Empty,
                MediaSource = string.Empty,
                MediaPreviewBytes = null,
                MediaLocationKind = MessageMediaLocationKind.None,
                MediaFileType = (int)FileType.Unknown,
                MediaRemoteId = 0,
                MediaAccessHash = 0,
                MediaFileReference = null,
                MediaDcId = 0,
                MediaSizeBytes = 0,
                MediaMime = string.Empty,
                MediaFileName = string.Empty,
                MediaThumbSize = string.Empty,
                MediaTypeBadge = string.Empty,
                IsMediaLoading = false,
                HasMediaFailed = false,
                MediaDownloadProgress = 0.0,
                DurationLabel = string.Empty,
                DurationSeconds = 0,
                ElapsedSeconds = 0,
                IsPlaying = false,
                AudioSource = string.Empty,
                WaveformData = EmptyWaveform(),
                AudioTitle = string.Empty,
                AudioPerformer = string.Empty,
                FileName = string.Empty,
                FileSizeLabel = string.Empty,
                FileSizeBytes = 0,
                FileMime = string.Empty,
                FilePath = string.Empty,
                FileRemoteId = 0,
                FileAccessHash = 0,
                FileReference = null,
                FileDcId = 0,
                DownloadedBytes = 0,
                TotalBytes = 0,
                IsDownloaded = false,
                IsDownloading = false,
                HasDownloadFailed = false,
                DownloadProgress = 0.0,
                FileIconGlyph = string.Empty,
                StickerEmoji = string.Empty,
                StickerImageSource = string.Empty,
                ContactName = string.Empty,
                ContactPhone = string.Empty,
                ContactInitials = string.Empty,
                ContactPhoneUri = null,
                LocationLabel = string.Empty,
                LocationAddress = string.Empty,
                LocationCoordinates = string.Empty,
                LocationMapUri = null,
                PollQuestion = string.Empty,
                PollSummary = string.Empty,
                PollOptions = new ObservableCollection<PollOptionView>(),
                PollTotalVoters = 0,
                PollIsClosed = false,
                PollCanVote = false,
                PollShowResults = false,
                PollVotedOptionIndex = -1,
                WebPageSiteName = string.Empty,
                WebPageTitle = string.Empty,
                WebPageDescription = string.Empty,
                WebPageUrl = string.Empty,
                WebPageDisplayUrl = string.Empty,
                WebPageThumbPath = string.Empty,
                WebPageUri = null,
                UnsupportedHint = string.Empty
            };
        }

        public static MessageRow From(Message m, string peerTitle)
        {
            if (m == null) return null;
            bool hasReply = m.ReplyToMessageId.HasValue;
            var row = new MessageRow
            {
                ServerId = m.Id != null && m.Id.IsConfirmed ? (long?)m.Id.ServerId : null,
                ClientTempId = m.Id != null && m.Id.IsPending ? m.Id.ClientTempId : null,
                Kind = MessageRowKind.Text,
                Text = string.Empty,
                IsOutgoing = m.IsOutgoing,
                TimeLabel = m.Date.ToLocalTime().ToString("H:mm"),
                StatusLabel = FormatBubbleStatus(m.DeliveryState),
                HasReply = hasReply,
                ReplyAuthorLabel = m.IsOutgoing ? (peerTitle ?? "reply") : "me",
                ReplyPreviewText = hasReply
                    ? ("message #" + m.ReplyToMessageId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    : string.Empty,
                TextEntities = EmptyEntities(),
                CaptionEntities = EmptyEntities(),
                ReactionSummary = string.Empty,
                MediaCaption = string.Empty,
                MediaThumbPath = string.Empty,
                MediaFullPath = string.Empty,
                MediaSource = string.Empty,
                MediaPreviewBytes = null,
                MediaLocationKind = MessageMediaLocationKind.None,
                MediaFileType = (int)FileType.Unknown,
                MediaRemoteId = 0,
                MediaAccessHash = 0,
                MediaFileReference = null,
                MediaDcId = 0,
                MediaSizeBytes = 0,
                MediaMime = string.Empty,
                MediaFileName = string.Empty,
                MediaThumbSize = string.Empty,
                MediaTypeBadge = string.Empty,
                IsMediaLoading = false,
                HasMediaFailed = false,
                MediaDownloadProgress = 0.0,
                DurationLabel = string.Empty,
                DurationSeconds = 0,
                ElapsedSeconds = 0,
                IsPlaying = false,
                AudioSource = string.Empty,
                WaveformData = EmptyWaveform(),
                AudioTitle = string.Empty,
                AudioPerformer = string.Empty,
                FileName = string.Empty,
                FileSizeLabel = string.Empty,
                FileSizeBytes = 0,
                FileMime = string.Empty,
                FilePath = string.Empty,
                FileRemoteId = 0,
                FileAccessHash = 0,
                FileReference = null,
                FileDcId = 0,
                DownloadedBytes = 0,
                TotalBytes = 0,
                IsDownloaded = false,
                IsDownloading = false,
                HasDownloadFailed = false,
                DownloadProgress = 0.0,
                FileIconGlyph = string.Empty,
                StickerEmoji = string.Empty,
                StickerImageSource = string.Empty,
                ContactName = string.Empty,
                ContactPhone = string.Empty,
                ContactInitials = string.Empty,
                ContactPhoneUri = null,
                LocationLabel = string.Empty,
                LocationAddress = string.Empty,
                LocationCoordinates = string.Empty,
                LocationMapUri = null,
                PollQuestion = string.Empty,
                PollSummary = string.Empty,
                PollOptions = new ObservableCollection<PollOptionView>(),
                PollTotalVoters = 0,
                PollIsClosed = false,
                PollCanVote = false,
                PollShowResults = false,
                PollVotedOptionIndex = -1,
                WebPageSiteName = string.Empty,
                WebPageTitle = string.Empty,
                WebPageDescription = string.Empty,
                WebPageUrl = string.Empty,
                WebPageDisplayUrl = string.Empty,
                WebPageThumbPath = string.Empty,
                WebPageUri = null,
                UnsupportedHint = string.Empty
            };
            ProjectContent(m.Content, row);
            return row;
        }

        private static void ProjectContent(MessageContent content, MessageRow row)
        {
            ApplyContentMetadata(content, row);

            var text = content as MessageContentText;
            if (text != null)
            {
                row.Kind = MessageRowKind.Text;
                row.Text = text.Body ?? string.Empty;
                row.TextEntities = text.Entities ?? EmptyEntities();
                return;
            }

            var photo = content as MessageContentPhoto;
            if (photo != null)
            {
                row.Kind = MessageRowKind.Photo;
                row.MediaCaption = photo.Caption ?? string.Empty;
                row.MediaWidth = photo.Width;
                row.MediaHeight = photo.Height;
                row.MediaThumbPath = photo.LocalThumbPath ?? string.Empty;
                row.MediaFullPath = ChooseMediaFullPath(photo.LocalFullPath, photo.File);
                row.MediaSource = ChooseMediaSource(row.MediaFullPath, row.MediaThumbPath, photo.Thumbnails);
                row.MediaPreviewBytes = ChoosePreviewBytes(photo.Thumbnails);
                MediaThumbnail photoDownloadThumb = ChooseDownloadThumb(photo.Thumbnails);
                ApplyMediaDownloadMetadata(row, photo.File, MessageMediaLocationKind.Photo,
                    FileType.Photo, "photo.jpg", "image/jpeg",
                    photoDownloadThumb != null ? photoDownloadThumb.Size : 0L,
                    photoDownloadThumb != null ? photoDownloadThumb.SizeType : string.Empty);
                row.CaptionEntities = photo.CaptionEntities ?? EmptyEntities();
                row.IsMediaLoading = false;
                row.MediaDownloadProgress = string.IsNullOrEmpty(row.MediaSource) && !HasBytes(row.MediaPreviewBytes) ? 0.0 : 100.0;
                row.MediaTypeBadge = "photo";
                row.Text = string.IsNullOrEmpty(photo.Caption) ? "\ud83d\udcf7 photo" : photo.Caption;
                return;
            }

            var video = content as MessageContentVideo;
            if (video != null)
            {
                row.Kind = video.IsVideoNote ? MessageRowKind.VideoNote
                         : video.IsAnimation ? MessageRowKind.Animation
                         : MessageRowKind.Video;
                row.MediaCaption = video.Caption ?? string.Empty;
                row.MediaWidth = video.Width;
                row.MediaHeight = video.Height;
                row.MediaThumbPath = video.LocalThumbPath ?? string.Empty;
                row.MediaFullPath = ChooseMediaFullPath(video.LocalFullPath, video.File);
                row.MediaSource = ChooseMediaSource(string.Empty, row.MediaThumbPath, video.Thumbnails);
                row.MediaPreviewBytes = ChoosePreviewBytes(video.Thumbnails);
                ApplyMediaDownloadMetadata(row, video.File, MessageMediaLocationKind.Document,
                    FileType.Video, "video.mp4", "video/mp4", video.Size, string.Empty);
                row.CaptionEntities = video.CaptionEntities ?? EmptyEntities();
                bool hasVideoFile = !string.IsNullOrEmpty(row.MediaFullPath);
                row.IsMediaLoading = false;
                row.MediaDownloadProgress = hasVideoFile || !string.IsNullOrEmpty(row.MediaSource) || HasBytes(row.MediaPreviewBytes) ? 100.0 : 0.0;
                row.DurationLabel = FormatDuration(video.Duration);
                row.DurationSeconds = ToSeconds(video.Duration);
                row.MediaTypeBadge = video.IsAnimation ? "GIF"
                                   : video.IsVideoNote ? "video note"
                                   : ("\u25b6 video " + row.DurationLabel);
                row.Text = string.IsNullOrEmpty(video.Caption) ? row.MediaTypeBadge : video.Caption;
                return;
            }

            var voice = content as MessageContentVoice;
            if (voice != null)
            {
                row.Kind = MessageRowKind.Voice;
                row.DurationLabel = FormatDuration(voice.Duration);
                row.DurationSeconds = ToSeconds(voice.Duration);
                row.AudioSource = ChooseMediaFullPath(voice.LocalPath, voice.File);
                row.WaveformData = voice.Waveform ?? EmptyWaveform();
                ApplyMediaDownloadMetadata(row, voice.File, MessageMediaLocationKind.Document,
                    FileType.Voice, "voice.ogg", "audio/ogg", 0L, string.Empty);
                row.FilePath = row.AudioSource;
                row.IsDownloaded = !string.IsNullOrEmpty(row.AudioSource);
                row.DownloadProgress = row.IsDownloaded ? 100.0 : 0.0;
                row.Text = "\ud83c\udf99 voice " + row.DurationLabel;
                return;
            }

            var audio = content as MessageContentAudio;
            if (audio != null)
            {
                row.Kind = MessageRowKind.Audio;
                row.AudioTitle = audio.Title ?? string.Empty;
                row.AudioPerformer = audio.Performer ?? string.Empty;
                row.DurationLabel = FormatDuration(audio.Duration);
                row.DurationSeconds = ToSeconds(audio.Duration);
                row.AudioSource = ChooseMediaFullPath(audio.LocalFullPath, audio.File);
                ApplyMediaDownloadMetadata(row, audio.File, MessageMediaLocationKind.Document,
                    FileType.Document, string.IsNullOrEmpty(audio.Title) ? "audio.mp3" : audio.Title + ".mp3",
                    "audio/mpeg", audio.Size, string.Empty);
                row.FilePath = row.AudioSource;
                row.IsDownloaded = !string.IsNullOrEmpty(row.AudioSource);
                row.DownloadProgress = row.IsDownloaded ? 100.0 : 0.0;
                row.MediaCaption = audio.Caption ?? string.Empty;
                row.CaptionEntities = audio.CaptionEntities ?? EmptyEntities();
                row.Text = string.IsNullOrEmpty(audio.Title)
                    ? ("\ud83c\udfb5 audio " + row.DurationLabel)
                    : (audio.Title + " \u00b7 " + row.DurationLabel);
                return;
            }

            var doc = content as MessageContentDocument;
            if (doc != null)
            {
                row.Kind = MessageRowKind.Document;
                row.FileName = string.IsNullOrEmpty(doc.FileName) ? "file" : doc.FileName;
                row.FileSizeBytes = doc.Size;
                row.FileMime = doc.MimeType ?? string.Empty;
                row.FilePath = ChooseMediaFullPath(doc.LocalFullPath, doc.File);
                row.TotalBytes = doc.Size;
                TelegramMediaFile file = doc.File;
                if (file != null)
                {
                    row.FileRemoteId = ParseFileId(file.FileId);
                    row.FileAccessHash = file.AccessHash;
                    row.FileReference = file.FileReference;
                    row.FileDcId = file.DcId;
                    if (row.FileSizeBytes <= 0 && file.Size > 0) row.FileSizeBytes = file.Size;
                    if (row.TotalBytes <= 0 && file.Size > 0) row.TotalBytes = file.Size;
                    if (string.IsNullOrEmpty(row.FileMime)) row.FileMime = file.MimeType ?? string.Empty;
                    if ((string.IsNullOrEmpty(row.FileName) || string.Equals(row.FileName, "file", StringComparison.Ordinal)) &&
                        !string.IsNullOrEmpty(file.FileName))
                    {
                        row.FileName = file.FileName;
                    }
                }
                row.FileSizeLabel = FormatFileSize(row.FileSizeBytes);
                row.IsDownloaded = !string.IsNullOrEmpty(row.FilePath);
                row.DownloadProgress = row.IsDownloaded ? 100.0 : 0.0;
                row.DownloadedBytes = row.IsDownloaded
                    ? (row.TotalBytes > 0 ? row.TotalBytes : row.FileSizeBytes)
                    : 0L;
                row.FileIconGlyph = PickFileGlyph(row.FileMime, row.FileName);
                row.MediaCaption = doc.Caption ?? string.Empty;
                row.CaptionEntities = doc.CaptionEntities ?? EmptyEntities();
                row.Text = string.IsNullOrEmpty(doc.Caption)
                    ? (row.FileIconGlyph + " " + row.FileName)
                    : doc.Caption;
                return;
            }

            var sticker = content as MessageContentSticker;
            if (sticker != null)
            {
                row.Kind = MessageRowKind.Sticker;
                row.StickerEmoji = string.IsNullOrEmpty(sticker.Emoji) ? "\ud83c\udf1f" : sticker.Emoji;
                row.StickerImageSource = ChooseMediaSource(ChooseMediaFullPath(sticker.LocalPath, sticker.File), string.Empty, sticker.Thumbnails);
                row.Text = row.StickerEmoji;
                return;
            }

            var contact = content as MessageContentContact;
            if (contact != null)
            {
                row.Kind = MessageRowKind.Contact;
                row.ContactName = contact.DisplayName;
                row.ContactPhone = contact.PhoneNumber ?? string.Empty;
                row.ContactInitials = BuildInitials(row.ContactName, row.ContactPhone);
                row.ContactPhoneUri = BuildTelUri(row.ContactPhone);
                row.Text = FirstNonEmpty(row.ContactName, row.ContactPhone);
                return;
            }

            var loc = content as MessageContentLocation;
            if (loc != null)
            {
                row.Kind = MessageRowKind.Location;
                string coordinates = loc.Latitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture) + ", " +
                                     loc.Longitude.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
                row.LocationLabel = loc.IsVenue
                    ? loc.VenueTitle
                    : coordinates;
                row.LocationAddress = loc.VenueAddress ?? string.Empty;
                row.LocationCoordinates = coordinates;
                row.LocationMapUri = BuildMapUri(loc.Latitude, loc.Longitude);
                row.Text = row.LocationLabel;
                return;
            }

            var poll = content as MessageContentPoll;
            if (poll != null)
            {
                row.Kind = MessageRowKind.Poll;
                row.PollQuestion = string.IsNullOrEmpty(poll.Question) ? "Poll" : poll.Question;
                row.PollOptions = BuildPollOptions(poll);
                row.PollTotalVoters = poll.TotalVoters;
                row.PollIsClosed = poll.IsClosed;
                row.PollCanVote = !poll.IsClosed;
                row.PollShowResults = poll.TotalVoters > 0 || HasChosenPollOption(poll);
                row.PollVotedOptionIndex = FindChosenPollOptionIndex(poll);
                int answerCount = row.PollOptions != null ? row.PollOptions.Count : 0;
                row.PollSummary = answerCount + " options \u00b7 " + poll.TotalVoters + " voters" +
                                  (poll.IsClosed ? " \u00b7 closed" : string.Empty);
                row.Text = "\ud83d\udcca " + row.PollQuestion;
                return;
            }

            var web = content as MessageContentWebPage;
            if (web != null)
            {
                row.Kind = MessageRowKind.WebPage;
                row.WebPageSiteName = web.SiteName ?? string.Empty;
                row.WebPageTitle = web.Title ?? string.Empty;
                row.WebPageDescription = web.Description ?? string.Empty;
                row.WebPageUrl = web.Url ?? string.Empty;
                row.WebPageDisplayUrl = string.IsNullOrEmpty(web.DisplayUrl) ? web.Url : web.DisplayUrl;
                row.WebPageThumbPath = FirstNonEmpty(web.ThumbPath, web.Thumb != null ? web.Thumb.LocalPath : string.Empty);
                row.WebPageUri = BuildWebUri(row.WebPageUrl);
                row.Text = string.IsNullOrEmpty(web.Body) ? web.Url : web.Body;
                return;
            }

            var service = content as MessageContentService;
            if (service != null)
            {
                row.Kind = MessageRowKind.Service;
                row.Text = string.IsNullOrEmpty(service.DisplayText) ? "service message" : service.DisplayText;
                return;
            }

            var unsupported = content as MessageContentUnsupported;
            if (unsupported != null)
            {
                row.Kind = MessageRowKind.Unsupported;
                row.UnsupportedHint = FirstNonEmpty(unsupported.UnsupportedSummary, unsupported.MediaKindHint);
                row.Text = string.IsNullOrEmpty(row.UnsupportedHint)
                    ? "[unsupported]"
                    : ("[unsupported " + row.UnsupportedHint + "]");
                return;
            }

            // Truly unknown \u2014 keep as plain text fallback.
            row.Kind = MessageRowKind.Unsupported;
            row.Text = "[unsupported]";
        }

        private static void ApplyContentMetadata(MessageContent content, MessageRow row)
        {
            if (content == null || row == null) return;

            row.ReactionSummary = BuildReactionSummary(content.Reactions);
        }

        private static string BuildReactionSummary(IList<MessageReaction> reactions)
        {
            if (reactions == null || reactions.Count == 0) return string.Empty;

            var parts = new List<string>();
            for (int i = 0; i < reactions.Count && i < 4; i++)
            {
                MessageReaction r = reactions[i];
                if (r == null) continue;
                string glyph = !string.IsNullOrEmpty(r.Emoticon) ? r.Emoticon : "\u2605";
                string count = r.Count > 1 ? r.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
                parts.Add(glyph + count);
            }
            return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
        }

        private static string ChooseMediaFullPath(string preferred, TelegramMediaFile file)
        {
            if (!string.IsNullOrEmpty(preferred)) return preferred;
            if (file == null) return string.Empty;
            return FirstNonEmpty(file.LocalFullPath, file.LocalPath);
        }

        private static void ApplyMediaDownloadMetadata(
            MessageRow row,
            TelegramMediaFile file,
            MessageMediaLocationKind locationKind,
            FileType fileType,
            string fallbackName,
            string fallbackMime,
            long fallbackSize,
            string thumbSize)
        {
            if (row == null || file == null) return;

            row.MediaLocationKind = locationKind;
            row.MediaFileType = (int)fileType;
            row.MediaRemoteId = ParseFileId(file.FileId);
            row.MediaAccessHash = file.AccessHash;
            row.MediaFileReference = file.FileReference;
            row.MediaDcId = file.DcId;
            row.MediaSizeBytes = file.Size > 0 ? file.Size : fallbackSize;
            row.MediaMime = FirstNonEmpty(file.MimeType, fallbackMime);
            row.MediaFileName = FirstNonEmpty(file.FileName, fallbackName);
            row.MediaThumbSize = thumbSize ?? string.Empty;
        }

        private static MediaThumbnail ChooseDownloadThumb(IList<MediaThumbnail> thumbnails)
        {
            if (thumbnails == null || thumbnails.Count == 0) return null;

            MediaThumbnail best = null;
            long bestArea = -1;
            int bestRank = int.MaxValue;
            for (int i = 0; i < thumbnails.Count; i++)
            {
                MediaThumbnail t = thumbnails[i];
                if (t == null) continue;
                if (t.Bytes != null && t.Bytes.Length > 0) continue;
                if (string.IsNullOrEmpty(t.SizeType)) continue;

                long area = (long)Math.Max(0, t.Width) * (long)Math.Max(0, t.Height);
                int rank = RankPhotoSizeType(t.SizeType);
                if (best == null || area > bestArea || (area == bestArea && rank < bestRank))
                {
                    best = t;
                    bestArea = area;
                    bestRank = rank;
                }
            }
            return best;
        }

        private static int RankPhotoSizeType(string sizeType)
        {
            switch (sizeType)
            {
                case "w": return 0;
                case "y": return 1;
                case "x": return 2;
                case "m": return 3;
                case "s": return 4;
                default: return 100;
            }
        }

        private static string ChooseMediaSource(string fullPath, string thumbPath, IList<MediaThumbnail> thumbnails)
        {
            string source = FirstNonEmpty(fullPath, thumbPath);
            if (!string.IsNullOrEmpty(source)) return source;

            if (thumbnails == null) return string.Empty;
            for (int i = thumbnails.Count - 1; i >= 0; i--)
            {
                MediaThumbnail t = thumbnails[i];
                if (t == null) continue;
                source = t.LocalPath;
                if (!string.IsNullOrEmpty(source)) return source;
            }

            return string.Empty;
        }

        private static byte[] ChoosePreviewBytes(IList<MediaThumbnail> thumbnails)
        {
            if (thumbnails == null || thumbnails.Count == 0) return null;

            MediaThumbnail best = null;
            int bestScore = -1;
            for (int i = 0; i < thumbnails.Count; i++)
            {
                MediaThumbnail t = thumbnails[i];
                if (t == null || !HasBytes(t.Bytes)) continue;

                int score = t.Width > 0 && t.Height > 0
                    ? t.Width * t.Height
                    : t.Bytes.Length;
                if (score >= bestScore)
                {
                    best = t;
                    bestScore = score;
                }
            }

            return best != null ? best.Bytes : null;
        }

        private static bool HasBytes(byte[] value)
        {
            return value != null && value.Length > 0;
        }

        private static ObservableCollection<PollOptionView> BuildPollOptions(MessageContentPoll poll)
        {
            var rows = new ObservableCollection<PollOptionView>();
            if (poll == null) return rows;

            IList<PollOption> options = poll.Options;
            if (options != null && options.Count > 0)
            {
                int denominator = poll.TotalVoters;
                if (denominator <= 0)
                {
                    for (int i = 0; i < options.Count; i++)
                        if (options[i] != null) denominator += Math.Max(0, options[i].VoteCount);
                }

                for (int i = 0; i < options.Count; i++)
                {
                    PollOption option = options[i];
                    if (option == null) continue;
                    int votes = option.VoteCount;
                    int percent = denominator > 0 ? (int)Math.Round((votes * 100.0) / denominator) : 0;
                    rows.Add(new PollOptionView
                    {
                        Text = option.Text ?? string.Empty,
                        Votes = votes,
                        Percent = percent,
                        Voted = option.IsChosen
                    });
                }
                return rows;
            }

            IList<string> answers = poll.Answers;
            if (answers != null)
            {
                for (int i = 0; i < answers.Count; i++)
                {
                    rows.Add(new PollOptionView
                    {
                        Text = answers[i] ?? string.Empty,
                        Votes = 0,
                        Percent = 0,
                        Voted = false
                    });
                }
            }
            return rows;
        }

        private static bool HasChosenPollOption(MessageContentPoll poll)
        {
            return FindChosenPollOptionIndex(poll) >= 0;
        }

        private static int FindChosenPollOptionIndex(MessageContentPoll poll)
        {
            if (poll == null || poll.Options == null) return -1;
            for (int i = 0; i < poll.Options.Count; i++)
            {
                PollOption option = poll.Options[i];
                if (option != null && option.IsChosen) return i;
            }
            return -1;
        }

        private static int ToSeconds(TimeSpan value)
        {
            if (value.TotalSeconds <= 0) return 0;
            if (value.TotalSeconds > int.MaxValue) return int.MaxValue;
            return (int)Math.Round(value.TotalSeconds);
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null) return string.Empty;
            for (int i = 0; i < values.Length; i++)
            {
                if (!string.IsNullOrEmpty(values[i])) return values[i];
            }
            return string.Empty;
        }

        private static string BuildInitials(string name, string fallback)
        {
            string value = FirstNonEmpty(name, fallback).Trim();
            if (string.IsNullOrEmpty(value)) return "?";

            var chars = new List<char>(2);
            bool atWordStart = true;
            for (int i = 0; i < value.Length && chars.Count < 2; i++)
            {
                char c = value[i];
                if (char.IsWhiteSpace(c) || c == '-' || c == '_' || c == '.')
                {
                    atWordStart = true;
                    continue;
                }

                if (atWordStart && char.IsLetterOrDigit(c))
                {
                    chars.Add(char.ToUpperInvariant(c));
                    atWordStart = false;
                }
            }

            if (chars.Count == 0)
            {
                for (int i = 0; i < value.Length && chars.Count < 2; i++)
                {
                    char c = value[i];
                    if (char.IsLetterOrDigit(c)) chars.Add(char.ToUpperInvariant(c));
                }
            }

            return chars.Count == 0 ? "?" : new string(chars.ToArray());
        }

        private static Uri BuildTelUri(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return null;

            var cleaned = new System.Text.StringBuilder();
            for (int i = 0; i < phone.Length; i++)
            {
                char c = phone[i];
                if (char.IsDigit(c) || (c == '+' && cleaned.Length == 0))
                    cleaned.Append(c);
            }

            if (cleaned.Length == 0) return null;
            Uri uri;
            return Uri.TryCreate("tel:" + cleaned.ToString(), UriKind.Absolute, out uri) ? uri : null;
        }

        private static Uri BuildMapUri(double latitude, double longitude)
        {
            string lat = latitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string lon = longitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            Uri uri;
            if (Uri.TryCreate("bingmaps:?cp=" + lat + "~" + lon + "&lvl=16", UriKind.Absolute, out uri))
                return uri;
            return null;
        }

        private static Uri BuildWebUri(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            string value = url.Trim();

            Uri uri;
            if (Uri.TryCreate(value, UriKind.Absolute, out uri))
                return uri;

            if (value.IndexOf("://", StringComparison.Ordinal) < 0 &&
                Uri.TryCreate("https://" + value, UriKind.Absolute, out uri))
                return uri;

            return null;
        }

        private static IList<MessageEntity> EmptyEntities()
        {
            return new MessageEntity[0];
        }

        private static byte[] EmptyWaveform()
        {
            return new byte[0];
        }

        private static string FormatBubbleStatus(DeliveryState state)
        {
            switch (state)
            {
                case DeliveryState.Sending: return "...";
                case DeliveryState.Sent: return "\u2713";
                case DeliveryState.Delivered: return "\u2713\u2713";
                case DeliveryState.Read: return "\u2713\u2713";
                case DeliveryState.Failed: return "!";
                default: return string.Empty;
            }
        }

        private static string FormatDuration(TimeSpan d)
        {
            if (d.TotalHours >= 1.0)
                return ((int)d.TotalHours).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ":" + d.Minutes.ToString("D2") + ":" + d.Seconds.ToString("D2");
            return d.Minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ":" + d.Seconds.ToString("D2");
        }

        private static long ParseFileId(string fileId)
        {
            long value;
            if (long.TryParse(fileId, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return 0L;
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes <= 0) return string.Empty;
            const double KB = 1024.0;
            const double MB = 1024.0 * 1024.0;
            const double GB = 1024.0 * 1024.0 * 1024.0;
            if (bytes < KB) return bytes + " B";
            if (bytes < MB) return (bytes / KB).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " KB";
            if (bytes < GB) return (bytes / MB).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " MB";
            return (bytes / GB).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }

        private static string PickFileGlyph(string mime, string fileName)
        {
            mime = mime ?? string.Empty;
            string ext = string.Empty;
            if (!string.IsNullOrEmpty(fileName))
            {
                int dot = fileName.LastIndexOf('.');
                if (dot >= 0) ext = fileName.Substring(dot + 1).ToLowerInvariant();
            }
            if (mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return "\ud83d\uddbc";
            if (mime.StartsWith("video/", StringComparison.OrdinalIgnoreCase)) return "\ud83c\udfac";
            if (mime.StartsWith("audio/", StringComparison.OrdinalIgnoreCase)) return "\ud83c\udfb5";
            if (mime.StartsWith("application/pdf", StringComparison.OrdinalIgnoreCase) || ext == "pdf") return "\ud83d\udcd5";
            if (mime.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase) ||
                ext == "zip" || ext == "rar" || ext == "7z" || ext == "tar" || ext == "gz") return "\ud83d\udddc";
            if (ext == "doc" || ext == "docx" || ext == "rtf") return "\ud83d\udcdd";
            if (ext == "xls" || ext == "xlsx" || ext == "csv") return "\ud83d\udcca";
            if (ext == "ppt" || ext == "pptx") return "\ud83d\udcd1";
            return "\ud83d\udcc4";
        }
    }
}
