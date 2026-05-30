// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ChatListPage.xaml.cs — code-behind only handles plumbing.
//
// Builds the ChatListPageViewModel from the resolved IChatsApi, subscribes
// to dialog-change events on Loaded, unsubscribes on Unloaded, kicks off the
// initial load, and forwards Pivot selection + AppBar button taps.
//
// The legacy "Refresh" primary AppBar button is replaced with Search and
// New Chat (matches the reference design). Refresh is still reachable via
// the secondary commands menu so we keep the manual-pull escape hatch.

using System;
using System.Threading;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.App.ViewModels;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Kernel.Logging;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class ChatListPage : Page
    {
        private ChatListPageViewModel _vm;
        // Track whether the first initial-load already happened, so
        // subsequent navigations
        // back to this cached page don't re-fire LoadAsync (which
        // would mask the live in-memory state with a fresh server
        // pull). Live events keep _vm.Dialogs* up to date while the
        // user is in a chat — coming back is "free".
        private bool _firstNavigationCompleted;

        public ChatListPage()
        {
            EarlyLog.Write("Boot", "ChatListPage ctor begin");
            InitializeComponent();

            // Cache the page across navigations. Patterns mirrored from the
            // platform clients:
            //
            //   * iOS Telegram pushes the chat controller onto a
            //     UINavigationController stack. The chat list view
            //     controller stays in memory while a chat is on top —
            //     viewWillDisappear is fired but no destruction. Coming
            //     back via popViewController paints instantly.
            //
            //   * Android Telegram keeps the dialog-list Fragment in
            //     the FragmentManager back-stack. RecyclerView state
            //     (scroll, view holders, animations) is preserved.
            //
            // NavigationCacheMode.Required tells WinRT to keep this
            // page instance alive for the app's lifetime — never
            // evicted by Frame.CacheSize (which only governs Enabled
            // pages). The VM, its 4 ObservableCollections, all bus
            // subscriptions, and the ListView's virtualization state
            // all survive in-app navigations.
            NavigationCacheMode = NavigationCacheMode.Required;

            _vm = AppViewModels.CreateChatListPageViewModel();
            if (_vm == null)
            {
                // Composition not ready (extremely unusual) — fall back
                // to a minimal VM resolving just IChatsApi so the screen
                // still renders; live updates won't fire but the user
                // can still see their dialogs.
                IChatsApi chats = null;
                if (App.Composition != null)
                {
                    App.Composition.TryResolve<IChatsApi>(out chats);
                }
                _vm = new ChatListPageViewModel(chats);
            }
            DataContext = _vm;

            // Wire bus / IChatsApi subscriptions ONCE here. With page
            // caching, Loaded fires on every navigation back (cache
            // hits) — wiring there would re-subscribe every time the
            // user returns from a chat. The VM's Subscribe() is
            // idempotent (re-entry is a no-op), but moving the call
            // here makes the lifecycle obvious and keeps subscriptions
            // continuously active so live events accumulate even
            // while the user is on another page.
            _vm.Subscribe();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            EarlyLog.Write("Boot", "ChatListPage ctor end");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            EarlyLog.Write("Boot",
                "ChatListPage OnNavigatedTo first=" + (!_firstNavigationCompleted));
            base.OnNavigatedTo(e);
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_firstNavigationCompleted)
            {
                // Cache hit — the user came back from a chat / settings.
                // The VM still has every dialog row, the ListView still
                // has its scroll position, and live events have kept
                // both up to date in the background. Nothing to fetch.
                EarlyLog.Write("Boot", "ChatListPage Loaded (cache hit)");
                return;
            }
            _firstNavigationCompleted = true;

            EarlyLog.Write("Boot", "ChatListPage Loaded (first render complete)");
            await _vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
            App.ScheduleDeferredSyncBootstrap();
            App.ScheduleDeferredStorageMaintenance();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Do NOT unsubscribe here. The page is cached and
            // its subscriptions need to keep firing while the user is
            // viewing a chat — that's how new messages bump unread
            // counts, online dots flip, and typing indicators appear
            // on the dialog rows even while we're not on screen.
            //
            // Subscription teardown happens when the composition root
            // is rebuilt on logout (a fresh ChatListPage is constructed
            // for the new session). For app suspend / terminate the
            // process dies and OS reclaims everything anyway.
            EarlyLog.Write("Boot", "ChatListPage Unloaded (subs kept alive)");
        }

        // ---- Pivot wiring ----

        // Maps the active Pivot index to the VM's filter. We index by
        // position (0 = all, 1 = unread, 2 = personal, 3 = groups) — same
        // order as the PivotItems declared in XAML. Touching the Pivot's
        // Header strings here would couple this code to copy localisation
        // when those strings get translated; positional dispatch is more
        // robust.
        private void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_vm == null || MainPivot == null) return;
            DialogTab tab;
            switch (MainPivot.SelectedIndex)
            {
                case 1: tab = DialogTab.Unread; break;
                case 2: tab = DialogTab.Personal; break;
                case 3: tab = DialogTab.Groups; break;
                case 0:
                default: tab = DialogTab.All; break;
            }
            _vm.SetFilter(tab);
        }

        // ---- AppBar handlers ----

        private void OnSearchClicked(object sender, RoutedEventArgs e)
        {
            NavigateToRoute(Route.Search, typeof(Settings.SearchPage));
        }

        private void OnNewChatClicked(object sender, RoutedEventArgs e)
        {
            NavigateToRoute(Route.NewChat, typeof(Compose.NewChatPage));
        }

        private void OnCallsClicked(object sender, RoutedEventArgs e)
        {
            NavigateToRoute(Route.Calls, typeof(Calls.CallsPage));
        }

        private void OnContactsClicked(object sender, RoutedEventArgs e)
        {
            NavigateToRoute(Route.Contacts, typeof(Profile.ContactsPage));
        }

        private void OnSettingsClicked(object sender, RoutedEventArgs e)
        {
            NavigateToRoute(Route.Settings, typeof(Settings.SettingsPage));
        }

        private async void OnRefreshClicked(object sender, RoutedEventArgs e)
        {
            await _vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        }

        // Routes via INavigationService when available, falls back to a
        // direct Frame.Navigate. Centralised so every AppBar button uses
        // the same plumbing.
        private void NavigateToRoute(Route route, Type fallbackPageType)
        {
            if (App.Composition != null)
            {
                INavigationService nav = null;
                App.Composition.TryResolve<INavigationService>(out nav);
                if (nav != null && nav.NavigateTo(route)) return;
            }
            if (fallbackPageType != null && Frame != null)
            {
                Frame.Navigate(fallbackPageType);
            }
        }

        // ---- ListView container realisation ----

        // Tracks the last threshold-crossing event we logged so a single
        // sweeping scroll doesn't spam the trace with one line per
        // realised row. The page logs "trigger" once per LoadMore call.
        private int _lastPrefetchLogIndex = -1;

        /// <summary>
        /// Hook for the ListView item realisation pipeline. WinRT raises
        /// this each time a row's
        /// container is materialised or recycled. Two responsibilities:
        ///   1. Extension point for future per-row work (lazy
        ///      avatar fetch via args.RegisterUpdateCallback, frame-time
        ///      telemetry).
        ///   2. Prefetch trigger. When the user scrolls within
        ///      the last 8 rows of the active tab, fire a paginated
        ///      <c>LoadMoreAsync</c>. The VM's in-flight guard makes
        ///      concurrent calls cheap; the typical case is one fetch
        ///      per page-end.
        ///
        /// Diagnostics: every event logs the (item, total, hasMore)
        /// triple so we can see in the trace whether the handler is
        /// actually firing on scroll. Cuts off after the trigger fires
        /// once per page-set to keep the trace clean.
        /// </summary>
        private void OnDialogContainerContentChanging(
            ListViewBase sender, ContainerContentChangingEventArgs args)
        {
            if (_vm == null || sender == null || args == null) return;
            if (args.InRecycleQueue) return;

            // Lazy-fetch trigger: this is the FIRST place we know the
            // row is actually about to paint on screen. DialogRow.RequestAvatar()
            // is idempotent — the row's _avatarRequested flag flips on the
            // first call and ignores subsequent realisations (which happen
            // on every scroll-recycle round-trip even though the row is
            // the same instance). On a 4G Lumia this drops the cold-start
            // RPC fan-out from "all ~30 rows immediately" to "the 6 rows
            // visible in the first viewport plus whatever the user scrolls
            // to," which is the single biggest contributor to the ~5 s of
            // post-login avatar wall time.
            var row = args.Item as DialogRow;
            if (row != null) row.RequestAvatar();

            var coll = sender.ItemsSource as System.Collections.ICollection;
            if (coll == null) return;
            int total = coll.Count;
            const int prefetchThreshold = 8;
            int boundary = total - prefetchThreshold;
            bool past = args.ItemIndex >= boundary;
            // Only log the FIRST time we cross the threshold for the
            // current total so the trace shows "I tried" without
            // flooding (one log line per page boundary).
            if (past && _lastPrefetchLogIndex != total)
            {
                _lastPrefetchLogIndex = total;
                AppLog.For("App.ChatListPage").Info(
                    "scroll.prefetch trigger item=" + args.ItemIndex +
                    " total=" + total +
                    " hasMore=" + _vm.HasMore +
                    " loadingMore=" + _vm.IsLoadingMore);
            }
            if (!past) return;
            var ignore = _vm.LoadMoreAsync(System.Threading.CancellationToken.None);
        }

        // ---- List item taps ----

        private void OnDialogClick(object sender, ItemClickEventArgs e)
        {
            var row = e != null ? (e.ClickedItem as DialogRow) : null;
            if (row == null || string.IsNullOrEmpty(row.PeerKey)) return;

            // Navigation parameter is the (peerKey, title) pair so ChatPage
            // can render the title without a second lookup.
            if (Frame != null)
            {
                Frame.Navigate(typeof(ChatPage), new ChatPageNavArgs(row.PeerKey, row.Title));
            }
        }
    }

    /// <summary>
    /// Navigation parameter for ChatPage. Public + sealed so the WP8.1
    /// navigation frame can serialize/deserialize it across suspensions
    /// (small DTO, no behaviour).
    /// </summary>
    public sealed class ChatPageNavArgs
    {
        public ChatPageNavArgs() { }

        public ChatPageNavArgs(string peerKey, string title)
        {
            PeerKey = peerKey;
            Title = title;
        }

        public string PeerKey { get; set; }
        public string Title { get; set; }
    }
}
