// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ChatListPageViewModel.cs
//
// Drives ChatListPage. On Load() it asks IChatsApi for the dialog list and
// projects each DialogPreview into a DialogRow that XAML can bind. It also
// subscribes to IChatsApi.DialogChanged so the list updates live as new
// dialogs arrive — handlers marshal to the UI dispatcher via Dispatch.OnUiAsync.
//
// Tab filtering: the page renders four pivot tabs (all / unread / personal /
// groups). The VM keeps the full list in <c>_allDialogs</c> as the source of
// truth and exposes <c>Dialogs</c> as the *filtered* view bound to the
// current pivot. Calling <see cref="SetFilter"/> re-populates the bound
// collection in place — no extra ObservableCollections per tab, so the
// memory footprint stays the same as the legacy single-list page.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.App.Services;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain.Events;
using Windows.UI.Xaml;

namespace Vianigram.App.ViewModels
{
    /// <summary>
    /// Filter applied to the bound dialog list. Mirrors the four pivot tabs
    /// surfaced by <c>ChatListPage.xaml</c>: <c>all</c>, <c>unread</c>,
    /// <c>personal</c> (1:1 DMs only), <c>groups</c> (basic chats + channels).
    /// </summary>
    public enum DialogTab
    {
        All = 0,
        Unread = 1,
        Personal = 2,
        Groups = 3
    }

    public sealed class ChatListPageViewModel : ObservableObject, IDisposable
    {
        // Tuned for the WP 8.1 viewport. ListView shows ~7 dialog rows
        // per screen at 480p. Loading 30
        // covers ~4 viewports — enough that the user has to scroll
        // deliberately before the next page is needed, while keeping
        // the initial getDialogs round-trip small. The prefetch
        // threshold (PrefetchTrigger) triggers a follow-up page load
        // when the user enters the last N rows of the current set.
        private const int PageSize = 30;
        private const int PrefetchTrigger = 8;

        // Debounce window for Chats.DialogChanged → LoadAsync. Several
        // updates often arrive back-to-back during a sync sweep (e.g.
        // pinned reorder + last-message text + unread count for the
        // same dialog). Coalescing them into a single refetch avoids
        // hammering messages.getDialogs and tripping FLOOD_WAIT.
        private const int DialogChangedDebounceMs = 250;

        private readonly IChatsApi _chats;
        private readonly IEventBus _bus; // null-tolerant — VM still works without

        // Source of truth: every dialog the VM has ever observed (this
        // session). Tab views are projections over this list. Handlers
        // mutate the row instance in place — the same DialogRow ref
        // appears in every applicable tab collection so a single
        // PropertyChanged update is reflected everywhere.
        private readonly List<DialogRow> _allDialogs = new List<DialogRow>();

        // Subscriptions held so we can drop them on Unsubscribe without
        // leaking handlers across page
        // navigations. Sized to the four event types we listen to.
        private IDisposable[] _busSubs;

        // Pivot-tab views — each one is what the matching ListView's
        // ItemsSource binds to. Items are inserted in the order they
        // appear in _allDialogs (pinned-first ordering is enforced
        // by InsertSorted in Ola C). Ola A: positional add only.
        // Sharing DialogRow refs across the four collections means a
        // mutation on a row fires PropertyChanged once and every
        // visible tab updates simultaneously without extra work.
        private DispatcherTimer _typingSweepTimer;
        private DispatcherTimer _dialogChangedDebounceTimer;
        private bool _dialogChangedRefreshPending;

        private string _statusText;
        private string _errorMessage;
        private bool _isRefreshing;
        private bool _isLoadingMore;
        private DialogTab _currentTab;
        private int _allCount;
        private int _unreadCount;
        private int _personalCount;
        private int _groupsCount;

        // Pagination cursor — advances on every successful LoadMoreAsync.
        // Reset to DialogCursor.Empty + HasMore=true on LoadAsync.
        private DialogCursor _nextCursor = DialogCursor.Empty;
        private bool _hasMore = true;
        private int _loadMoreInFlight; // Interlocked guard to prevent overlapping fetches

        public ChatListPageViewModel(IChatsApi chats)
            : this(chats, bus: null)
        {
        }

        /// <summary>
        /// Preferred ctor. The VM needs the kernel <see cref="IEventBus"/>
        /// to react to live updates
        /// (MessageReceived / PeerStatusChanged / PeerTypingChanged /
        /// MessageReadByMe) emitted by the Sync→Messages bridge. The
        /// legacy single-arg ctor stays for callers (tests, transient
        /// bring-up paths) that can't resolve the bus.
        /// </summary>
        public ChatListPageViewModel(IChatsApi chats, IEventBus bus)
        {
            _chats = chats;
            _bus = bus;
            DialogsAll = new ObservableCollection<DialogRow>();
            DialogsUnread = new ObservableCollection<DialogRow>();
            DialogsPersonal = new ObservableCollection<DialogRow>();
            DialogsGroups = new ObservableCollection<DialogRow>();
            _statusText = "idle";
            _currentTab = DialogTab.All;
        }

        // Four tab-specific projections backed by the same DialogRow
        // instances. Each ListView in
        // ChatListPage.xaml binds to the matching one — switching tabs
        // is a no-op for the VM (no Clear/Add cycle).
        public ObservableCollection<DialogRow> DialogsAll { get; private set; }
        public ObservableCollection<DialogRow> DialogsUnread { get; private set; }
        public ObservableCollection<DialogRow> DialogsPersonal { get; private set; }
        public ObservableCollection<DialogRow> DialogsGroups { get; private set; }

        /// <summary>
        /// Legacy alias kept for code paths that still bind the generic
        /// "Dialogs" collection. Returns the all-tab projection — the
        /// same items the previous single-collection design exposed.
        /// New code should bind the tab-specific properties directly.
        /// </summary>
        public ObservableCollection<DialogRow> Dialogs
        {
            get { return DialogsAll; }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
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

        public bool IsRefreshing
        {
            get { return _isRefreshing; }
            private set { SetProperty(ref _isRefreshing, value); }
        }

        /// <summary>
        /// True while the next paginated page is in flight. Bound to a
        /// footer indicator in <c>ChatListPage.xaml</c> so the user gets
        /// feedback while older dialogs stream in. Distinct from
        /// <see cref="IsRefreshing"/> so a manual refresh and the
        /// background paginate don't collide on the visual.
        /// </summary>
        public bool IsLoadingMore
        {
            get { return _isLoadingMore; }
            private set { SetProperty(ref _isLoadingMore, value); }
        }

        /// <summary>
        /// True when the server has more dialogs beyond the ones already
        /// loaded (the most recent <c>messages.getDialogs</c> page came
        /// back as a slice with <c>HasMore</c> set). Used by the page
        /// to decide whether to even attempt a paginated fetch.
        /// </summary>
        public bool HasMore
        {
            get { return _hasMore; }
            private set { SetProperty(ref _hasMore, value); }
        }

        // Per-tab counts the page can display next to each pivot header
        // (e.g. "all 33", "unread 5"). Recomputed in RebuildVisible().
        public int AllCount       { get { return _allCount; }      private set { SetProperty(ref _allCount, value); } }
        public int UnreadCount    { get { return _unreadCount; }   private set { SetProperty(ref _unreadCount, value); } }
        public int PersonalCount  { get { return _personalCount; } private set { SetProperty(ref _personalCount, value); } }
        public int GroupsCount    { get { return _groupsCount; }   private set { SetProperty(ref _groupsCount, value); } }

        public DialogTab CurrentTab { get { return _currentTab; } }

        /// <summary>
        /// Records the active pivot tab. With the new tab-specific
        /// projections (<see cref="DialogsAll"/> / <see cref="DialogsUnread"/>
        /// / <see cref="DialogsPersonal"/> / <see cref="DialogsGroups"/>)
        /// the VM no longer rebuilds a shared collection on tab switch —
        /// the ListView in each PivotItem binds to its dedicated source
        /// and the swap is "free" (no Clear/Add cycle, no virtualization
        /// reset). We still track the active tab for analytics / logging
        /// and so the page can scroll-to-top on tab switch externally.
        /// </summary>
        public void SetFilter(DialogTab tab)
        {
            if (_currentTab == tab) return;
            _currentTab = tab;
            OnPropertyChanged("CurrentTab");
        }

        public void SetFilter(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return;
            switch (tag)
            {
                case "all":      SetFilter(DialogTab.All); break;
                case "unread":   SetFilter(DialogTab.Unread); break;
                case "personal": SetFilter(DialogTab.Personal); break;
                case "groups":   SetFilter(DialogTab.Groups); break;
            }
        }

        private static bool BelongsToTab(DialogRow row, DialogTab tab)
        {
            if (row == null) return false;
            string key = row.PeerKey ?? string.Empty;
            switch (tab)
            {
                case DialogTab.Unread:
                    return row.UnreadCount > 0;
                case DialogTab.Personal:
                    return key.StartsWith("user:", StringComparison.Ordinal);
                case DialogTab.Groups:
                    return key.StartsWith("chat:", StringComparison.Ordinal)
                        || key.StartsWith("channel:", StringComparison.Ordinal);
                case DialogTab.All:
                default:
                    return true;
            }
        }

        private ObservableCollection<DialogRow> ProjectionFor(DialogTab tab)
        {
            switch (tab)
            {
                case DialogTab.Unread: return DialogsUnread;
                case DialogTab.Personal: return DialogsPersonal;
                case DialogTab.Groups: return DialogsGroups;
                case DialogTab.All:
                default: return DialogsAll;
            }
        }

        /// <summary>
        /// Insert a row into every applicable tab projection at the
        /// position dictated by <paramref name="visualIndex"/> in
        /// <see cref="_allDialogs"/>. Tab projections preserve relative
        /// order: a row's index inside DialogsAll matches its index in
        /// _allDialogs, and tab-filtered projections keep the same
        /// relative ordering minus the filtered-out rows.
        /// </summary>
        private void InsertIntoProjections(DialogRow row, int visualIndex)
        {
            for (int t = 0; t < 4; t++)
            {
                DialogTab tab = (DialogTab)t;
                if (!BelongsToTab(row, tab)) continue;
                var coll = ProjectionFor(tab);
                int target = ProjectionIndexFor(tab, visualIndex);
                if (target < 0 || target > coll.Count) target = coll.Count;
                coll.Insert(target, row);
            }
        }

        /// <summary>
        /// Translate an index in <see cref="_allDialogs"/> to the matching
        /// index in <paramref name="tab"/>'s projection — i.e. count how
        /// many rows preceding <paramref name="allIndex"/> also belong
        /// to that tab.
        /// </summary>
        private int ProjectionIndexFor(DialogTab tab, int allIndex)
        {
            if (tab == DialogTab.All) return allIndex;
            int count = 0;
            for (int i = 0; i < allIndex && i < _allDialogs.Count; i++)
            {
                if (BelongsToTab(_allDialogs[i], tab)) count++;
            }
            return count;
        }

        private void RemoveFromAllProjections(DialogRow row)
        {
            DialogsAll.Remove(row);
            DialogsUnread.Remove(row);
            DialogsPersonal.Remove(row);
            DialogsGroups.Remove(row);
        }

        private void RebuildVisible()
        {
            DialogsAll.Clear();
            DialogsUnread.Clear();
            DialogsPersonal.Clear();
            DialogsGroups.Clear();
            for (int i = 0; i < _allDialogs.Count; i++)
            {
                InsertIntoProjections(_allDialogs[i], i);
            }
        }

        /// <summary>
        /// Sync row membership across tab projections after a property
        /// change that might flip its tab eligibility (e.g. UnreadCount
        /// went from 0 to 1 → now belongs to Unread). Called by
        /// granular handlers; cheap (4 contains+add/remove checks).
        /// </summary>
        private void SyncTabMembership(DialogRow row)
        {
            if (row == null) return;
            int allIdx = _allDialogs.IndexOf(row);
            if (allIdx < 0) return;
            for (int t = 0; t < 4; t++)
            {
                DialogTab tab = (DialogTab)t;
                if (tab == DialogTab.All) continue; // always present
                var coll = ProjectionFor(tab);
                bool isIn = coll.Contains(row);
                bool shouldBe = BelongsToTab(row, tab);
                if (isIn == shouldBe) continue;
                if (shouldBe)
                {
                    int target = ProjectionIndexFor(tab, allIdx);
                    if (target < 0 || target > coll.Count) target = coll.Count;
                    coll.Insert(target, row);
                }
                else
                {
                    coll.Remove(row);
                }
            }
        }

        private void RecomputeCounts()
        {
            int all = 0, unread = 0, personal = 0, groups = 0;
            for (int i = 0; i < _allDialogs.Count; i++)
            {
                var r = _allDialogs[i];
                if (r == null) continue;
                all++;
                string key = r.PeerKey ?? string.Empty;
                if (r.UnreadCount > 0) unread++;
                if (key.StartsWith("user:", StringComparison.Ordinal)) personal++;
                else if (key.StartsWith("chat:", StringComparison.Ordinal)
                      || key.StartsWith("channel:", StringComparison.Ordinal)) groups++;
            }
            AllCount = all;
            UnreadCount = unread;
            PersonalCount = personal;
            GroupsCount = groups;
        }

        // ---- Subscription lifecycle ---------------------------------

        public void Subscribe()
        {
            if (_chats != null)
            {
                _chats.DialogChanged += OnDialogChanged;
            }

            // Subscribe to the kernel bus so we can react to
            // MessageReceived / PeerStatusChanged /
            // PeerTypingChanged / MessageReadByMe without going through
            // a full LoadAsync (which would hammer dialogs.getDialogs on
            // every keystroke from the peer's typing indicator).
            if (_bus != null && _busSubs == null)
            {
                _busSubs = new IDisposable[]
                {
                    _bus.Subscribe<MessageReceived>(OnLiveMessageReceived),
                    _bus.Subscribe<PeerStatusChanged>(OnLivePeerStatusChanged),
                    _bus.Subscribe<PeerTypingChanged>(OnLivePeerTypingChanged),
                    _bus.Subscribe<MessageReadByMe>(OnLiveMessageReadByMe),
                };
            }
        }

        public void Unsubscribe()
        {
            if (_chats != null)
            {
                _chats.DialogChanged -= OnDialogChanged;
            }
            if (_busSubs != null)
            {
                for (int i = 0; i < _busSubs.Length; i++)
                {
                    try { if (_busSubs[i] != null) _busSubs[i].Dispose(); }
                    catch { }
                }
                _busSubs = null;
            }
            // Release the timers so the VM can be garbage-collected after
            // the page is unloaded.
            // DispatcherTimer keeps a strong ref through its Tick handler.
            if (_typingSweepTimer != null)
            {
                try { _typingSweepTimer.Stop(); } catch { }
                _typingSweepTimer.Tick -= OnTypingSweepTick;
                _typingSweepTimer = null;
            }
            if (_dialogChangedDebounceTimer != null)
            {
                try { _dialogChangedDebounceTimer.Stop(); } catch { }
                _dialogChangedDebounceTimer.Tick -= OnDialogChangedDebounceTick;
                _dialogChangedDebounceTimer = null;
            }
            _dialogChangedRefreshPending = false;
        }

        /// <summary>
        /// Symmetric to <see cref="Unsubscribe"/> for callers that hold
        /// a long-lived reference to the VM. The page's OnUnloaded
        /// handler invokes Unsubscribe today; Dispose is here for
        /// future hosts (test rigs, side-pane previews).
        /// </summary>
        public void Dispose()
        {
            Unsubscribe();
        }

        private void OnDialogChanged(object sender, DialogChangedEventArgs args)
        {
            // Skip the `ListSynced` echo of our own LoadAsync.
            if (args != null && args.Reason == DialogChangedEventArgs.ChangeReason.ListSynced)
            {
                return;
            }

            // Coalesce bursts of DialogChanged into a single LoadAsync
            // after a 250 ms quiet
            // window. A sync sweep can fire 5-10 of these for the same
            // peer (pinned + last-message + unread count + folder), and
            // doing one refetch per event hammers messages.getDialogs.
            // Marshal to UI thread to safely manipulate the
            // DispatcherTimer (which must be created/started/stopped
            // from the UI thread on WP 8.1).
            _dialogChangedRefreshPending = true;
            var ignore = Dispatch.OnUiAsync(EnsureDialogChangedDebounceTimer);
        }

        private void EnsureDialogChangedDebounceTimer()
        {
            if (_dialogChangedDebounceTimer == null)
            {
                _dialogChangedDebounceTimer = new DispatcherTimer();
                _dialogChangedDebounceTimer.Interval = TimeSpan.FromMilliseconds(DialogChangedDebounceMs);
                _dialogChangedDebounceTimer.Tick += OnDialogChangedDebounceTick;
            }
            // Restart the timer — an event during the quiet window
            // pushes the deadline forward by another 250 ms.
            if (_dialogChangedDebounceTimer.IsEnabled)
            {
                _dialogChangedDebounceTimer.Stop();
            }
            _dialogChangedDebounceTimer.Start();
        }

        private void OnDialogChangedDebounceTick(object sender, object e)
        {
            if (_dialogChangedDebounceTimer != null) _dialogChangedDebounceTimer.Stop();
            if (!_dialogChangedRefreshPending) return;
            _dialogChangedRefreshPending = false;
            var loadIgnore = LoadAsync(CancellationToken.None);
        }

        // -----------------------------------------------------------------
        // Live event handlers (granular row mutation, no reload)
        // -----------------------------------------------------------------

        /// <summary>
        /// New message arrived. If the row is in our list we mutate it in
        /// place (last-message excerpt, timestamp, unread bump, move to
        /// top). If the row is NOT in our list (brand-new dialog) we
        /// fall back to <see cref="LoadAsync"/> via the existing
        /// <see cref="OnDialogChanged"/> path — Chats will publish a
        /// <c>DialogChangedEventArgs.ChangeReason.Added</c> shortly which
        /// triggers a refresh.
        /// </summary>
        private void OnLiveMessageReceived(MessageReceived e)
        {
            if (e == null || string.IsNullOrEmpty(e.PeerKey)) return;

            string peerKey = e.PeerKey;
            // Outgoing messages still surface — they bump the row to
            // the top with the new preview text but DON'T increment
            // unread (you can't be unread on something you sent). This
            // matches Telegram's UX: sending a message reorders the
            // dialog list immediately, before the server ack lands.
            //
            // The wire body may be a keyed-format token from TlDecoder
            // ("~Service.JoinedGroup"
            // / "~Media.Voice"). Translate before placing it in the
            // dialog preview so the chat list shows localized text.
            string body = Vianigram.App.Services.LocalizedText.Resolve(e.Body);
            if (string.IsNullOrEmpty(body)) body = Vianigram.App.Services.Strings.Get("Notif.Body.Generic");
            if (e.IsOutgoing && !string.IsNullOrEmpty(body))
            {
                // "You: hola" — the prefix is itself localizable.
                body = Vianigram.App.Services.Strings.Get("Common.YouPrefix") + body;
            }
            DateTime at = e.At == default(DateTime) ? DateTime.UtcNow : e.At;

            var ignore = Dispatch.OnUiAsync(() =>
            {
                DialogRow row = FindRow(peerKey);
                if (row == null)
                {
                    // Unknown peer — let the LoadAsync path catch up. The
                    // Chats application publishes DialogChanged.Added on
                    // first message from a never-seen peer.
                    return;
                }

                row.LastMessageText = body;
                row.TimestampLabel = FormatTimestampUtc(at);
                if (!e.IsOutgoing)
                {
                    row.UnreadCount = row.UnreadCount + 1;
                }

                // Pinned-aware move-to-top. Pinned dialogs occupy a fixed
                // "shelf" at the top of the
                // list — Telegram's UX never lets an unpinned dialog jump
                // above them on activity. We compute the target index by
                // peer kind:
                //   * pinned row → goes to 0 (top of the pinned shelf;
                //     server controls reordering within that shelf).
                //   * unpinned row → goes right after the last pinned
                //     row in each affected collection.
                // Three steps:
                //   1) Reorder _allDialogs.
                //   2) Reorder each tab projection where the row exists.
                //   3) Insert into projections it now qualifies for.
                int allTarget = ComputeMoveTargetForList(_allDialogs, row);
                int allIdx = _allDialogs.IndexOf(row);
                if (allIdx >= 0 && allIdx != allTarget)
                {
                    _allDialogs.RemoveAt(allIdx);
                    int adjusted = allTarget > allIdx ? allTarget - 1 : allTarget;
                    if (adjusted < 0) adjusted = 0;
                    if (adjusted > _allDialogs.Count) adjusted = _allDialogs.Count;
                    _allDialogs.Insert(adjusted, row);
                }

                MoveOrInsertRespectingPinned(DialogsAll, row, true);
                MoveOrInsertRespectingPinned(DialogsUnread, row, true);
                MoveOrInsertRespectingPinned(DialogsPersonal, row, BelongsToTab(row, DialogTab.Personal));
                MoveOrInsertRespectingPinned(DialogsGroups, row, BelongsToTab(row, DialogTab.Groups));

                RecomputeCounts();
                AppLog.For("App.ChatListPage").Info(
                    "live.received peer=" + peerKey +
                    " out=" + e.IsOutgoing +
                    " unread=" + row.UnreadCount +
                    " pinned=" + row.IsPinned);
            });
        }

        /// <summary>
        /// Returns the index inside <paramref name="list"/> where
        /// <paramref name="row"/> should land after a "move to top"
        /// activity event:
        ///   * If <paramref name="row"/> is pinned → 0 (head of list).
        ///   * Otherwise → the first index that doesn't already host a
        ///     pinned row, i.e. just below the pinned shelf.
        /// </summary>
        private static int ComputeMoveTargetForList(IList<DialogRow> list, DialogRow row)
        {
            if (row != null && row.IsPinned) return 0;
            for (int i = 0; i < list.Count; i++)
            {
                DialogRow r = list[i];
                if (r == null) continue;
                if (!r.IsPinned) return i;
            }
            return list.Count;
        }

        /// <summary>
        /// Idempotent helper for an <see cref="ObservableCollection{T}"/>:
        /// places <paramref name="row"/> at the correct pinned-aware
        /// position when <paramref name="shouldBe"/> is true (move
        /// existing or insert new), removes it when false. Avoids the
        /// trivial Move(n, n) notification when the row already sits at
        /// its target index.
        /// </summary>
        private static void MoveOrInsertRespectingPinned(
            ObservableCollection<DialogRow> coll, DialogRow row, bool shouldBe)
        {
            int idx = coll.IndexOf(row);
            if (!shouldBe)
            {
                if (idx >= 0) coll.RemoveAt(idx);
                return;
            }

            int target = ComputeMoveTargetForCollection(coll, row);
            if (idx < 0)
            {
                if (target < 0) target = 0;
                if (target > coll.Count) target = coll.Count;
                coll.Insert(target, row);
                return;
            }
            if (idx == target) return;
            // Move() handles the index shift; we just need the destination
            // expressed in post-removal coordinates (Move semantically
            // removes then inserts).
            coll.Move(idx, target > idx ? target - 1 : target);
        }

        private static int ComputeMoveTargetForCollection(
            ObservableCollection<DialogRow> coll, DialogRow row)
        {
            if (row != null && row.IsPinned) return 0;
            for (int i = 0; i < coll.Count; i++)
            {
                DialogRow r = coll[i];
                if (r == null) continue;
                if (!r.IsPinned) return i;
            }
            return coll.Count;
        }

        /// <summary>
        /// Peer presence flipped. We don't know which dialog rows render
        /// this user (could be a DM and/or a group where the user is a
        /// member) — for now we update the 1:1 DM row whose key is
        /// <c>user:{userId}</c>. Group-member presence is a future
        /// enhancement (would need the Chats context to expose
        /// member-of-chat lookup).
        /// </summary>
        private void OnLivePeerStatusChanged(PeerStatusChanged e)
        {
            if (e == null) return;
            long userId = e.UserId;
            bool isOnline = e.IsOnline;
            DateTime? lastOnline = e.LastOnlineUtc;

            var ignore = Dispatch.OnUiAsync(() =>
            {
                string dmKey = "user:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                DialogRow row = FindRow(dmKey);
                if (row == null) return;
                row.IsOnline = isOnline;
                row.LastOnlineUtc = lastOnline;
                AppLog.For("App.ChatListPage").Info(
                    "live.status user=" + userId + " online=" + isOnline);
            });
        }

        /// <summary>
        /// Typing / recording / uploading update. Sets the row's
        /// <see cref="DialogRow.IsTyping"/> flag and stamps the
        /// auto-expire deadline. A periodic sweeper run from
        /// <see cref="ScheduleTypingSweep"/> clears stale entries
        /// (Telegram's server-side TTL is ~6 s).
        /// </summary>
        private void OnLivePeerTypingChanged(PeerTypingChanged e)
        {
            if (e == null || string.IsNullOrEmpty(e.PeerKey)) return;
            string peerKey = e.PeerKey;
            string action = e.Action ?? "Typing";

            var ignore = Dispatch.OnUiAsync(() =>
            {
                DialogRow row = FindRow(peerKey);
                if (row == null) return;

                if (string.Equals(action, "Cancel", StringComparison.Ordinal))
                {
                    row.IsTyping = false;
                    row.TypingActionLabel = null;
                    return;
                }

                row.TypingActionLabel = action;
                row.IsTyping = true;
                row.TypingDeadlineUtc = DateTime.UtcNow.AddSeconds(6);
                ScheduleTypingSweep();
                AppLog.For("App.ChatListPage").Info(
                    "live.typing peer=" + peerKey + " action=" + action);
            });
        }

        /// <summary>
        /// We read the peer's messages on a different session of ours —
        /// clear the unread badge locally so the badge doesn't stick
        /// after we read on the other device. Also drops the row out of
        /// the "unread" tab projection (membership flipped from yes to no).
        /// </summary>
        private void OnLiveMessageReadByMe(MessageReadByMe e)
        {
            if (e == null || string.IsNullOrEmpty(e.PeerKey)) return;
            string peerKey = e.PeerKey;

            var ignore = Dispatch.OnUiAsync(() =>
            {
                DialogRow row = FindRow(peerKey);
                if (row == null) return;
                if (row.UnreadCount == 0) return;
                row.UnreadCount = 0;
                // Membership in "unread" tab just flipped to false.
                SyncTabMembership(row);
                RecomputeCounts();
            });
        }

        // -----------------------------------------------------------------
        // Typing auto-expire sweeper.
        //
        // A single DispatcherTimer ticks at 1 Hz only while at least one
        // row is typing. When the last
        // typing row expires, Stop() halts the timer until a fresh
        // PeerTypingChanged restarts it. Cheaper and more idiomatic on
        // WP 8.1 than the previous Task.Delay loop, which kept a
        // continuation queued every iteration.
        // -----------------------------------------------------------------

        private void EnsureTypingSweepTimer()
        {
            if (_typingSweepTimer != null) return;
            _typingSweepTimer = new DispatcherTimer();
            _typingSweepTimer.Interval = TimeSpan.FromSeconds(1);
            _typingSweepTimer.Tick += OnTypingSweepTick;
        }

        private void ScheduleTypingSweep()
        {
            EnsureTypingSweepTimer();
            if (!_typingSweepTimer.IsEnabled) _typingSweepTimer.Start();
        }

        private void OnTypingSweepTick(object sender, object e)
        {
            DateTime now = DateTime.UtcNow;
            bool anyActive = false;
            for (int i = 0; i < _allDialogs.Count; i++)
            {
                DialogRow row = _allDialogs[i];
                if (row == null || !row.IsTyping) continue;
                if (row.TypingDeadlineUtc <= now)
                {
                    row.IsTyping = false;
                    row.TypingActionLabel = null;
                }
                else
                {
                    anyActive = true;
                }
            }
            if (!anyActive && _typingSweepTimer != null && _typingSweepTimer.IsEnabled)
            {
                _typingSweepTimer.Stop();
            }
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------

        private DialogRow FindRow(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return null;
            for (int i = 0; i < _allDialogs.Count; i++)
            {
                DialogRow row = _allDialogs[i];
                if (row == null) continue;
                if (string.Equals(row.PeerKey, peerKey, StringComparison.Ordinal)) return row;
            }
            return null;
        }

        // ---- Data load ----------------------------------------------

        public async Task LoadAsync(CancellationToken ct)
        {
            ErrorMessage = null;

            var log = AppLog.For("App.ChatListPage");
            log.Info("LoadAsync begin");

            if (_chats == null)
            {
                ErrorMessage = "Chats service not available.";
                log.Warn("LoadAsync aborted: IChatsApi missing");
                return;
            }

            IsRefreshing = true;
            StatusText = "syncing";
            // Reset pagination — LoadAsync replaces the visible set.
            _nextCursor = DialogCursor.Empty;
            HasMore = true;
            try
            {
                Result<DialogPage, ChatError> result;
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    result = await _chats.GetDialogsAsync(PageSize, DialogCursor.Empty, ct)
                        .ConfigureAwait(true);
                    sw.Stop();
                    log.Info("GetDialogsAsync returned elapsed=" + sw.ElapsedMilliseconds + "ms ok=" + result.IsOk);
                }
                catch (OperationCanceledException)
                {
                    sw.Stop();
                    log.Info("GetDialogsAsync cancelled elapsed=" + sw.ElapsedMilliseconds + "ms");
                    return;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    log.Error("GetDialogsAsync threw elapsed=" + sw.ElapsedMilliseconds + "ms " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    StatusText = "error";
                    return;
                }

                if (result.IsFail)
                {
                    string detail = result.Error == null ? "(null error)" : result.Error.ToString();
                    log.Warn("GetDialogsAsync failed: " + detail);
                    ErrorMessage = FormatError(result.Error);
                    StatusText = "error";
                    return;
                }

                int count = (result.Value == null || result.Value.Items == null) ? 0 : result.Value.Items.Count;
                log.Info("GetDialogsAsync success items=" + count);
                ApplyPage(result.Value);
                if (result.Value != null)
                {
                    _nextCursor = result.Value.NextCursor ?? DialogCursor.Empty;
                    HasMore = result.Value.HasMore;
                }
                log.Info("LoadAsync done items=" + _allDialogs.Count +
                    " hasMore=" + HasMore +
                    " cursorOffsetId=" + _nextCursor.OffsetId);
                StatusText = (_allDialogs.Count == 0) ? "no chats yet" : "ok";
            }
            finally
            {
                IsRefreshing = false;
            }
        }

        /// <summary>
        /// Paginated continuation. Called by the page from
        /// <c>ContainerContentChanging</c> when the user
        /// scrolls within <see cref="PrefetchTrigger"/> rows of the
        /// current end of the list. Idempotent — concurrent calls are
        /// coalesced via the in-flight guard, no-ops when
        /// <see cref="HasMore"/> is false, and never overlaps with a
        /// LoadAsync (the cursor reset ensures consistency).
        /// </summary>
        public Task LoadMoreAsync(CancellationToken ct)
        {
            if (!_hasMore)
            {
                AppLog.For("App.ChatListPage").Info("LoadMoreAsync skipped: hasMore=false");
                return CompletedTask;
            }
            if (_isRefreshing)
            {
                AppLog.For("App.ChatListPage").Info("LoadMoreAsync skipped: isRefreshing=true");
                return CompletedTask;
            }
            if (_chats == null)
            {
                AppLog.For("App.ChatListPage").Warn("LoadMoreAsync skipped: IChatsApi is null");
                return CompletedTask;
            }
            if (Interlocked.Exchange(ref _loadMoreInFlight, 1) != 0)
            {
                AppLog.For("App.ChatListPage").Info("LoadMoreAsync skipped: already in flight");
                return CompletedTask;
            }
            return LoadMoreCoreAsync(ct);
        }

        private static readonly Task CompletedTask = Task.FromResult(0);

        private async Task LoadMoreCoreAsync(CancellationToken ct)
        {
            var log = AppLog.For("App.ChatListPage");
            try
            {
                IsLoadingMore = true;
                DialogCursor cursor = _nextCursor ?? DialogCursor.Empty;
                log.Info("LoadMoreAsync begin offsetId=" + cursor.OffsetId +
                    " offsetDate=" + cursor.OffsetDate.ToString("o", System.Globalization.CultureInfo.InvariantCulture));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                Result<DialogPage, ChatError> result;
                try
                {
                    result = await _chats.GetDialogsAsync(PageSize, cursor, ct).ConfigureAwait(true);
                    sw.Stop();
                    log.Info("LoadMoreAsync RPC elapsed=" + sw.ElapsedMilliseconds + "ms ok=" + result.IsOk);
                }
                catch (OperationCanceledException)
                {
                    sw.Stop();
                    log.Info("LoadMoreAsync cancelled elapsed=" + sw.ElapsedMilliseconds + "ms");
                    return;
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    log.Warn("LoadMoreAsync threw elapsed=" + sw.ElapsedMilliseconds + "ms " + ex.GetType().Name + ": " + ex.Message);
                    return;
                }

                if (result.IsFail)
                {
                    log.Warn("LoadMoreAsync failed: " + (result.Error == null ? "(null)" : result.Error.ToString()));
                    return;
                }

                DialogPage page = result.Value;
                if (page == null || page.Items == null || page.Items.Count == 0)
                {
                    HasMore = false;
                    return;
                }

                AppendPage(page);
                _nextCursor = page.NextCursor ?? DialogCursor.Empty;
                HasMore = page.HasMore;
                log.Info("LoadMoreAsync appended=" + page.Items.Count +
                    " total=" + _allDialogs.Count + " hasMore=" + HasMore);
            }
            finally
            {
                IsLoadingMore = false;
                Interlocked.Exchange(ref _loadMoreInFlight, 0);
            }
        }

        /// <summary>
        /// Append a continuation page to the existing collections without
        /// disturbing the rows already visible. Skips peers we've already
        /// observed (defensive — Telegram's pagination overlap can
        /// repeat the boundary item).
        /// </summary>
        private void AppendPage(DialogPage page)
        {
            if (page == null || page.Items == null) return;

            Vianigram.Composition.Infrastructure.IPeerCache peerCache = null;
            if (App.Composition != null)
            {
                App.Composition.TryResolve<Vianigram.Composition.Infrastructure.IPeerCache>(out peerCache);
            }

            for (int i = 0; i < page.Items.Count; i++)
            {
                var preview = page.Items[i];
                if (preview == null) continue;
                string peerKey = preview.Peer != null ? preview.Peer.ToString() : null;
                if (string.IsNullOrEmpty(peerKey)) continue;
                // De-dup against already-loaded rows (boundary overlap).
                if (FindRow(peerKey) != null) continue;

                var row = DialogRow.From(preview);
                if (row == null) continue;

                string resolved = ResolveTitleFromCache(peerCache, preview);
                if (!string.IsNullOrEmpty(resolved))
                {
                    row.Title = resolved;
                    row.RefreshAvatar();
                }

                long topMessageId = preview.LastMessageId.GetValueOrDefault(0);
                if (peerCache != null && topMessageId > 0 && topMessageId <= int.MaxValue)
                {
                    string previewText;
                    DateTime previewDate;
                    if (peerCache.TryGetMessagePreview((int)topMessageId, out previewText, out previewDate))
                    {
                        if (!string.IsNullOrEmpty(previewText)) row.LastMessageText = previewText;
                        if (previewDate != default(DateTime)) row.TimestampLabel = FormatTimestampUtc(previewDate);
                    }
                }

                HydrateAvatarFromCache(row, peerCache, preview);

                _allDialogs.Add(row);
                DialogsAll.Add(row);
                if (BelongsToTab(row, DialogTab.Unread)) DialogsUnread.Add(row);
                if (BelongsToTab(row, DialogTab.Personal)) DialogsPersonal.Add(row);
                if (BelongsToTab(row, DialogTab.Groups)) DialogsGroups.Add(row);
            }

            RecomputeCounts();
        }

        /// <summary>
        /// Two-stage avatar hydration.
        ///   Stage 1 (synchronous, ~5 ms): expand the inline stripped
        ///   JPEG so the row paints with a recognisable thumbnail
        ///   immediately — no network round-trip.
        ///   Stage 2 (async, optional): if the peer has a real photo
        ///   ref (photo_id + dc_id captured by the slice decoder),
        ///   issue an HD download via PeerAvatarFetcher. When it
        ///   completes we swap AvatarBitmap to the sharp version.
        /// </summary>
        private static void HydrateAvatarFromCache(
            DialogRow row,
            Vianigram.Composition.Infrastructure.IPeerCache peerCache,
            Vianigram.Chats.Domain.ValueObjects.DialogPreview preview)
        {
            if (row == null || peerCache == null || preview == null || preview.Peer == null) return;

            bool isUser = preview.Peer.Kind == Vianigram.Chats.Domain.ValueObjects.PeerKind.User;
            long peerIdLocal = preview.Peer.Id;
            byte[] stripped = null;
            try
            {
                stripped = isUser
                    ? peerCache.GetUserPhotoStripped(peerIdLocal)
                    : peerCache.GetChatPhotoStripped(peerIdLocal);
            }
            catch
            {
                // Cache lookup is best-effort.
            }

            // Stage 1: stripped thumb → BitmapImage.
            if (stripped != null && stripped.Length >= 3)
            {
                var ignore = ExpandAndAssignAsync(row, stripped);
                GC.KeepAlive(ignore);
            }

            // Stage 2: HD avatar via upload.getFile. Best-effort —
            // failures keep the stripped thumb visible.
            long photoId; int photoDcId;
            bool havePhotoRef = isUser
                ? peerCache.TryGetUserPhotoRef(peerIdLocal, out photoId, out photoDcId)
                : peerCache.TryGetChatPhotoRef(peerIdLocal, out photoId, out photoDcId);
            if (!havePhotoRef || photoId == 0L || photoDcId <= 0) return;

            Vianigram.App.Services.PeerAvatarFetcher fetcher = ResolveAvatarFetcher();
            if (fetcher == null) return;

            // Map PeerKind → PeerPhotoKind. The peer's access_hash is
            // needed for inputPeerUser / inputPeerChannel — pull it
            // from the cache the same way ChatPage does for opening
            // a conversation.
            Vianigram.Media.Domain.ValueObjects.PeerPhotoKind ppKind;
            long peerAccessHash = 0L;
            if (isUser)
            {
                ppKind = Vianigram.Media.Domain.ValueObjects.PeerPhotoKind.User;
                long? ah = peerCache.GetUserAccessHash(peerIdLocal);
                if (ah.HasValue) peerAccessHash = ah.Value;
            }
            else if (preview.Peer.Kind == Vianigram.Chats.Domain.ValueObjects.PeerKind.Channel)
            {
                ppKind = Vianigram.Media.Domain.ValueObjects.PeerPhotoKind.Channel;
                long? ah = peerCache.GetChannelAccessHash(peerIdLocal);
                if (ah.HasValue) peerAccessHash = ah.Value;
            }
            else
            {
                // Basic chat — no access_hash needed for inputPeerChat.
                ppKind = Vianigram.Media.Domain.ValueObjects.PeerPhotoKind.Chat;
            }

            DialogRow rowRef = row;
            int dcId = photoDcId;
            long pid = photoId;
            long ah2 = peerAccessHash;
            long pidPeer = peerIdLocal;
            var fetchTask = FetchAndAssignHdAsync(fetcher, ppKind, pidPeer, ah2, dcId, pid, rowRef);
            GC.KeepAlive(fetchTask);
        }

        private static async Task ExpandAndAssignAsync(DialogRow row, byte[] stripped)
        {
            try
            {
                var bmp = await Vianigram.App.Services.StrippedThumbExpander
                    .ExpandToBitmapAsync(stripped).ConfigureAwait(true);
                if (bmp != null && row != null && row.AvatarBitmap == null)
                {
                    // Only assign when the slot is still empty — the
                    // HD path may have already won the race; never
                    // overwrite a sharper image with a blurry one.
                    row.AvatarBitmap = bmp;
                }
            }
            catch (Exception ex)
            {
                AppLog.For("App.ChatListPage").Warn(
                    "avatar.expand threw " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static async Task FetchAndAssignHdAsync(
            Vianigram.App.Services.PeerAvatarFetcher fetcher,
            Vianigram.Media.Domain.ValueObjects.PeerPhotoKind kind,
            long peerId, long accessHash, int dcId, long photoId,
            DialogRow row)
        {
            try
            {
                using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30)))
                {
                    var bmp = await fetcher.FetchSmallAsync(
                        kind, peerId, accessHash, dcId, photoId, cts.Token).ConfigureAwait(true);
                    if (bmp != null && row != null) row.AvatarBitmap = bmp;
                }
            }
            catch (Exception ex)
            {
                AppLog.For("App.ChatListPage").Info(
                    "avatar.hd skipped peer=" + kind + ":" + peerId +
                    " err=" + ex.GetType().Name);
            }
        }

        // Memoised fetcher resolved on first call — composition root
        // creates lazy IMediaApi, IMediaCache; we wire them once and
        // reuse for every row in the dialog list.
        private static Vianigram.App.Services.PeerAvatarFetcher _avatarFetcher;
        private static readonly object _avatarFetcherGate = new object();

        private static Vianigram.App.Services.PeerAvatarFetcher ResolveAvatarFetcher()
        {
            if (_avatarFetcher != null) return _avatarFetcher;
            lock (_avatarFetcherGate)
            {
                if (_avatarFetcher != null) return _avatarFetcher;
                if (App.Composition == null) return null;
                Vianigram.Media.Ports.Inbound.IMediaApi media;
                Vianigram.Media.Ports.Outbound.IMediaCache cache;
                if (!App.Composition.TryResolve<Vianigram.Media.Ports.Inbound.IMediaApi>(out media) || media == null) return null;
                if (!App.Composition.TryResolve<Vianigram.Media.Ports.Outbound.IMediaCache>(out cache) || cache == null) return null;
                var log = AppLog.For("App.AvatarFetcher");
                _avatarFetcher = new Vianigram.App.Services.PeerAvatarFetcher(media, cache, log);
                return _avatarFetcher;
            }
        }

        private void ApplyPage(DialogPage page)
        {
            _allDialogs.Clear();
            DialogsAll.Clear();
            DialogsUnread.Clear();
            DialogsPersonal.Clear();
            DialogsGroups.Clear();
            if (page == null || page.Items == null)
            {
                RecomputeCounts();
                return;
            }

            // Resolve display names from the peer cache (hydrated by every
            // RPC response that carries users:Vector<User> /
            // chats:Vector<Chat>). The Chats handler hands us PeerId-only
            // dialogs because the chats/users slices live outside its
            // bounded context — we cross-link here at the UI layer.
            Vianigram.Composition.Infrastructure.IPeerCache peerCache = null;
            if (App.Composition != null)
            {
                App.Composition.TryResolve<Vianigram.Composition.Infrastructure.IPeerCache>(out peerCache);
            }

            for (int i = 0; i < page.Items.Count; i++)
            {
                var preview = page.Items[i];
                if (preview == null) continue;
                var row = DialogRow.From(preview);
                if (row != null)
                {
                    string resolved = ResolveTitleFromCache(peerCache, preview);
                    if (!string.IsNullOrEmpty(resolved))
                    {
                        row.Title = resolved;
                        row.RefreshAvatar();
                    }

                    // Last-activity text + timestamp from the peer cache,
                    // hydrated by the same getDialogs response (its
                    // messages:Vector<Message> slice). When we have the
                    // top_message id we can pull both fields; otherwise
                    // we leave the row's defaults from DialogPreview.
                    long topMessageId = preview.LastMessageId.GetValueOrDefault(0);
                    if (peerCache != null && topMessageId > 0 && topMessageId <= int.MaxValue)
                    {
                        string previewText;
                        DateTime previewDate;
                        if (peerCache.TryGetMessagePreview((int)topMessageId, out previewText, out previewDate))
                        {
                            if (!string.IsNullOrEmpty(previewText))
                            {
                                row.LastMessageText = previewText;
                            }
                            if (previewDate != default(DateTime))
                            {
                                row.TimestampLabel = FormatTimestampUtc(previewDate);
                            }
                        }
                    }

                    // Hydrate inline thumbnail from the peer cache so the
                    // avatar circle renders the user's face instead of just
                    // initials. Fire-and-forget — the row updates via
                    // PropertyChanged when the BitmapImage materialises.
                    HydrateAvatarFromCache(row, peerCache, preview);

                    _allDialogs.Add(row);
                    // Append into every applicable tab projection. Order
                    // matches _allDialogs (preserved by appending sequentially).
                    DialogsAll.Add(row);
                    if (BelongsToTab(row, DialogTab.Unread)) DialogsUnread.Add(row);
                    if (BelongsToTab(row, DialogTab.Personal)) DialogsPersonal.Add(row);
                    if (BelongsToTab(row, DialogTab.Groups)) DialogsGroups.Add(row);
                }
            }

            RecomputeCounts();
        }

        private static string FormatTimestampUtc(DateTime utc)
        {
            if (utc == default(DateTime)) return string.Empty;
            DateTime local = utc.ToLocalTime();
            DateTime today = DateTime.Now.Date;
            if (local.Date == today)
            {
                return local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            }
            if (local.Date == today.AddDays(-1))
            {
                return "yesterday";
            }
            if ((today - local.Date).TotalDays < 7)
            {
                return local.ToString("ddd", System.Globalization.CultureInfo.CurrentCulture);
            }
            return local.ToString("dd/MM/yy", System.Globalization.CultureInfo.CurrentCulture);
        }

        private static string ResolveTitleFromCache(
            Vianigram.Composition.Infrastructure.IPeerCache cache,
            DialogPreview preview)
        {
            if (cache == null || preview == null || preview.Peer == null) return null;
            var peer = preview.Peer;
            try
            {
                if (peer.Kind == PeerKind.User)
                {
                    return cache.GetUserDisplayName(peer.Id);
                }
                if (peer.Kind == PeerKind.Chat || peer.Kind == PeerKind.Channel)
                {
                    return cache.GetChatTitle(peer.Id);
                }
            }
            catch
            {
                // Cache is best-effort — never break the dialog list.
            }
            return null;
        }

        private static string FormatError(ChatError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ChatErrorKind.NetworkError:
                    return "Network error: " + error.Message;
                case ChatErrorKind.AccessDenied:
                    return "Access denied.";
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }
    }

    /// <summary>
    /// Row-level binding shape rendered by ChatListPage.ListView.
    /// Public + sealed so .NET Native (Release) can reflect it.
    ///
    /// The row inherits <see cref="ObservableObject"/> because the live-event
    /// pipeline (MessageReceived / PeerStatusChanged / PeerTypingChanged from
    /// <see cref="Vianigram.Kernel.Events.IEventBus"/>) mutates rows in place
    /// and pushes <c>PropertyChanged</c> per field. XAML <c>{Binding}</c>
    /// expressions automatically pick up the updates without a full
    /// <c>LoadAsync</c> rebuild.
    /// </summary>
    public sealed class DialogRow : ObservableObject
    {
        private string _peerKey;
        private string _title;
        private string _lastMessageText;
        private string _timestampLabel;
        private int _unreadCount;
        private string _avatarSource;
        private string _initials;
        private long _avatarColorSeed;
        private bool _isTyping;
        private string _typingActionLabel;
        private bool _isOnline;
        private DateTime? _lastOnlineUtc;
        private DateTime _typingExpiresUtc;
        private bool _isPinned;
        private bool _isMuted;

        public string PeerKey
        {
            get { return _peerKey; }
            set { SetProperty(ref _peerKey, value); }
        }

        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public string LastMessageText
        {
            get { return _lastMessageText; }
            set { SetProperty(ref _lastMessageText, value); }
        }

        public string TimestampLabel
        {
            get { return _timestampLabel; }
            set { SetProperty(ref _timestampLabel, value); }
        }

        public int UnreadCount
        {
            get { return _unreadCount; }
            set
            {
                if (SetProperty(ref _unreadCount, value))
                {
                    OnPropertyChanged("HasUnread");
                    OnPropertyChanged("UnreadLabel");
                }
            }
        }

        public string AvatarSource
        {
            get { return _avatarSource; }
            set { SetProperty(ref _avatarSource, value); }
        }

        // Inline thumbnail surfaced as a BitmapImage that the ChatListItem
        // control binds to. The setter is invoked by the VM after expanding
        // the cached stripped JPEG
        // bytes; XAML observes via PropertyChanged and the avatar circle
        // swaps from initials to the thumbnail with no flicker.
        private Windows.UI.Xaml.Media.Imaging.BitmapImage _avatarBitmap;
        public Windows.UI.Xaml.Media.Imaging.BitmapImage AvatarBitmap
        {
            get { return _avatarBitmap; }
            set
            {
                if (SetProperty(ref _avatarBitmap, value))
                {
                    OnPropertyChanged("HasAvatarBitmap");
                }
            }
        }

        public Windows.UI.Xaml.Visibility HasAvatarBitmap
        {
            get
            {
                return _avatarBitmap != null
                    ? Windows.UI.Xaml.Visibility.Visible
                    : Windows.UI.Xaml.Visibility.Collapsed;
            }
        }

        public string Initials
        {
            get { return _initials; }
            set { SetProperty(ref _initials, value); }
        }

        public long AvatarColorSeed
        {
            get { return _avatarColorSeed; }
            set { SetProperty(ref _avatarColorSeed, value); }
        }

        /// <summary>
        /// True while the peer is composing a message / recording voice /
        /// uploading media in this conversation. Auto-expires after ~6 s
        /// (server-side TTL) via <see cref="TypingDeadlineUtc"/>.
        /// </summary>
        public bool IsTyping
        {
            get { return _isTyping; }
            set
            {
                if (SetProperty(ref _isTyping, value))
                {
                    OnPropertyChanged("TypingVisibility");
                    OnPropertyChanged("StatusOrLastMessage");
                }
            }
        }

        /// <summary>
        /// Free-form action label projected from
        /// <c>SendMessageAction</c> — e.g. "Typing", "RecordingVoice",
        /// "RecordingVideo", "UploadingPhoto". UI maps these to
        /// localized strings.
        /// </summary>
        public string TypingActionLabel
        {
            get { return _typingActionLabel; }
            set
            {
                if (SetProperty(ref _typingActionLabel, value))
                {
                    OnPropertyChanged("StatusOrLastMessage");
                }
            }
        }

        /// <summary>
        /// True when the peer is observed online (1:1 DMs only — irrelevant
        /// for chats / channels). Drives the avatar online dot.
        /// </summary>
        public bool IsOnline
        {
            get { return _isOnline; }
            set
            {
                if (SetProperty(ref _isOnline, value))
                {
                    OnPropertyChanged("OnlineVisibility");
                    OnPropertyChanged("OnlineStatus");
                }
            }
        }

        /// <summary>
        /// Wire-friendly status string consumed by
        /// <c>ChatListItem.OnlineStatus</c> (the existing UserControl
        /// expects "Online" / empty rather than a bool). Computed —
        /// changes follow IsOnline.
        /// </summary>
        public string OnlineStatus
        {
            get { return _isOnline ? "Online" : string.Empty; }
        }

        /// <summary>
        /// Pinned dialogs render with a thumbtack glyph next to the
        /// preview and float to the top of the list above unpinned
        /// dialogs (Telegram's UX). Sourced from <c>DialogPreview.IsPinned</c>.
        /// </summary>
        public bool IsPinned
        {
            get { return _isPinned; }
            set { SetProperty(ref _isPinned, value); }
        }

        /// <summary>
        /// Muted dialogs render with a speaker-off glyph and skip the
        /// device's notification sound. Sourced from <c>DialogPreview.IsMuted</c>.
        /// </summary>
        public bool IsMuted
        {
            get { return _isMuted; }
            set { SetProperty(ref _isMuted, value); }
        }

        /// <summary>
        /// Server-reported last-online time (UTC) when the peer is offline,
        /// null when unknown. UI formats as "last seen 5 min ago" etc.
        /// </summary>
        public DateTime? LastOnlineUtc
        {
            get { return _lastOnlineUtc; }
            set { SetProperty(ref _lastOnlineUtc, value); }
        }

        /// <summary>
        /// Soft TTL stamp used by the parent VM's typing-clear sweeper to
        /// reset <see cref="IsTyping"/> after ~6 s of silence even if the
        /// peer never sent a "Cancel" action update.
        /// </summary>
        internal DateTime TypingDeadlineUtc
        {
            get { return _typingExpiresUtc; }
            set { _typingExpiresUtc = value; }
        }

        public Visibility HasUnread
        {
            get { return UnreadCount > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility TypingVisibility
        {
            get { return IsTyping ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility OnlineVisibility
        {
            get { return IsOnline ? Visibility.Visible : Visibility.Collapsed; }
        }

        /// <summary>
        /// Convenience helper for the row template: returns the typing
        /// action when active, otherwise the last-message preview. XAML
        /// can bind directly without juggling visibility converters.
        /// </summary>
        public string StatusOrLastMessage
        {
            get
            {
                if (_isTyping)
                {
                    return string.IsNullOrEmpty(_typingActionLabel)
                        ? "typing…"
                        : FormatTypingAction(_typingActionLabel);
                }
                return _lastMessageText ?? string.Empty;
            }
        }

        public string UnreadLabel
        {
            get
            {
                if (UnreadCount <= 0) return string.Empty;
                if (UnreadCount > 99) return "99+";
                return UnreadCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public static DialogRow From(DialogPreview p)
        {
            if (p == null) return null;
            var row = new DialogRow();
            row.PeerKey = p.Peer != null ? p.Peer.ToString() : string.Empty;
            row.Title = string.IsNullOrEmpty(p.Title) ? "(untitled)" : p.Title;
            row.LastMessageText = p.LastMessageText ?? string.Empty;
            row.TimestampLabel = FormatTimestamp(p.LastMessageDate);
            row.UnreadCount = p.UnreadCount;
            row.IsPinned = p.IsPinned;
            row.IsMuted = p.IsMuted;
            row.RefreshAvatar();
            return row;
        }

        public void RefreshAvatar()
        {
            Initials = CreateInitials(Title);
            AvatarColorSeed = CreateColorSeed(PeerKey, Title);
        }

        private static string FormatTypingAction(string action)
        {
            // Mirror the labels used by ChatPageViewModel.MapTypingAction
            // so both screens speak the same language. Also accept the
            // raw TypingActionKind enum names (which is what the
            // Sync→Messages bridge surfaces via PeerTypingChanged.Action).
            switch (action)
            {
                case "Typing":               return "typing…";
                case "RecordAudio":
                case "RecordingVoice":       return "recording voice…";
                case "UploadAudio":
                case "UploadingVoice":       return "sending voice…";
                case "RecordVideo":
                case "RecordingVideo":       return "recording video…";
                case "UploadVideo":
                case "UploadingVideo":       return "sending video…";
                case "UploadPhoto":
                case "UploadingPhoto":       return "sending photo…";
                case "UploadDocument":
                case "UploadingDocument":    return "sending file…";
                case "GeoLocation":          return "sharing location…";
                case "ChooseContact":
                case "ChoosingContact":      return "picking contact…";
                case "ChooseSticker":
                case "ChoosingSticker":      return "choosing sticker…";
                case "EmojiInteraction":     return "watching emoji…";
                case "EmojiInteractionSeen": return "saw emoji…";
                case "RecordRound":
                case "RoundVideo":
                case "UploadRound":
                case "UploadingRound":       return "recording video…";
                case "GamePlay":             return "playing a game…";
                case "SpeakingInGroupCall":  return "speaking…";
                case "ImportingHistory":     return "importing history…";
                default:                     return "typing…";
            }
        }

        private static string FormatTimestamp(DateTime utc)
        {
            if (utc == default(DateTime)) return string.Empty;
            var local = utc.ToLocalTime();
            var now = DateTime.Now;
            if (local.Date == now.Date) return local.ToString("HH:mm");
            if ((now - local).TotalDays < 7) return local.ToString("ddd");
            return local.ToString("yyyy-MM-dd");
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
    }
}
