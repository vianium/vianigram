// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ChatPage.xaml.cs — code-behind only handles plumbing.
//
// Receives a ChatPageNavArgs (peerKey + title) as the navigation parameter,
// builds the VM from the resolved IMessagesApi, subscribes to MessagesChanged
// on Loaded and unsubscribes on Unloaded, kicks off the initial history load,
// and forwards "Send" / "Back" presses to the VM and the navigation frame.
//
// Infinite scroll:
//   - On first Loaded, walk the ListView's visual tree to find its inner
//     ScrollViewer. Hook ScrollViewer.ViewChanged.
//   - When VerticalOffset drops below OlderMessageLoadThresholdPx (i.e. the
//     user scrolled near the top), invoke ChatPageViewModel.LoadOlderAsync.
//   - Around that load, capture ExtentHeight + VerticalOffset BEFORE prepend
//     and after the new items materialise, restore the scroll position so the
//     visible content stays anchored: targetOffset = previousOffset + delta,
//     where delta = newExtent - previousExtent.
//   - When the collection updates because of an *append* (incoming message,
//     initial load), scroll to the bottom; on *prepend* (older-load), do NOT
//     scroll — that would fight the offset-restore.

using System;
using System.Collections.Specialized;
using System.Threading;
using Vianigram.App.ViewModels;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Kernel.Logging;
using Vianigram.Messages.Ports.Inbound;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class ChatPage : Page
    {
        // 900px threshold — generous on purpose so the next page is in flight
        // by the time the user actually reaches the top and the scroll never
        // stalls.
        private const double OlderMessageLoadThresholdPx = 900.0;
        private const double FollowBottomThresholdPx = 140.0;

        private ChatPageViewModel _vm;
        private ScrollViewer _innerScroller;
        private bool _scrollHooked;
        // Track whether LoadInitialAsync already completed for the current
        // VM. Reset on every peer
        // change in OnNavigatedTo so a navigation to a NEW peer does
        // refetch history. A cache-hit return to the same peer skips
        // the refetch — VM still holds the bubble collection, scroll
        // position is preserved by NavigationCacheMode.
        private bool _initialLoadDone;

        // Captured immediately before LoadOlderAsync begins so we can restore
        // the visual scroll position once the prepended items have laid out.
        // _olderRestorePending is set while a paginated load is in flight; it
        // tells OnMessagesCollectionChanged to skip ScrollToLatest for the
        // resulting Insert events.
        private bool _olderRestorePending;
        private double _savedExtentHeight;
        private double _savedVerticalOffset;

        public ChatPage()
        {
            InitializeComponent();
            // The chat surface is the most expensive page after the dialog
            // list — XAML parse
            // for the bubble template is non-trivial, the ListView's
            // virtualization state is per-instance, and re-attaching
            // the scroll handler after every navigation is wasteful.
            //
            // NavigationCacheMode.Enabled tells WinRT to keep the page
            // instance in the LRU pool sized by Frame.CacheSize (= 6,
            // configured in App.xaml.cs). When the user backs out of
            // the chat to ChatListPage and re-enters the SAME peer,
            // the cached instance is reused: visual tree intact, VM
            // still holds bubbles + scroll position, no LoadInitial
            // round-trip. When the user enters a DIFFERENT peer the
            // cached instance gets repurposed (see OnNavigatedTo).
            NavigationCacheMode = NavigationCacheMode.Enabled;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string peerKey = string.Empty;
            string title = string.Empty;
            if (e != null && e.Parameter != null)
            {
                var args = e.Parameter as ChatPageNavArgs;
                if (args != null)
                {
                    peerKey = args.PeerKey ?? string.Empty;
                    title = args.Title ?? string.Empty;
                }
                else
                {
                    // Defensive: tolerate a bare-string parameter.
                    peerKey = e.Parameter as string ?? string.Empty;
                }
            }

            // Cache-hit detection. If the navigation parameter matches the
            // current VM's peer,
            // the user is returning to the same conversation — skip
            // VM rebuild, skip LoadInitialAsync, skip scroll re-hook.
            // The Loaded handler will see _initialLoadDone == true and
            // no-op. Bubbles, scroll position, composer text, typing
            // indicator — all preserved.
            if (_vm != null && string.Equals(_vm.PeerKey, peerKey, StringComparison.Ordinal))
            {
                EarlyLog.Write("Boot",
                    "ChatPage OnNavigatedTo (cache hit) peer=" + peerKey);
                // Re-attach handlers in case they were removed somehow
                // (defensive — they shouldn't be, but Loaded/Unloaded
                // detaching is annoying to debug if it slips).
                Loaded -= OnLoaded;
                Unloaded -= OnUnloaded;
                Loaded += OnLoaded;
                Unloaded += OnUnloaded;
                return;
            }

            // Different peer (or first navigation): tear down the
            // previous VM cleanly, then build a fresh one.
            if (_vm != null)
            {
                try { _vm.Messages.CollectionChanged -= OnMessagesCollectionChanged; }
                catch { }
                try { _vm.Unsubscribe(); }
                catch { }
                _vm = null;
            }
            DetachScrollHandler();
            _initialLoadDone = false;

            IMessagesApi messages = null;
            if (App.Composition != null)
            {
                App.Composition.TryResolve<IMessagesApi>(out messages);
            }

            _vm = new ChatPageViewModel(messages, peerKey, title);
            DataContext = _vm;
            EarlyLog.Write("Boot",
                "ChatPage OnNavigatedTo (rebuilt VM) peer=" + peerKey);

            Loaded -= OnLoaded;
            Unloaded -= OnUnloaded;
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            // Subscribe is idempotent inside the VM (bool flag) — safe
            // to call after a cache-hit return when the VM was already
            // subscribed before the previous Unloaded.
            _vm.Subscribe();
            _vm.Messages.CollectionChanged -= OnMessagesCollectionChanged;
            _vm.Messages.CollectionChanged += OnMessagesCollectionChanged;

            if (!_initialLoadDone)
            {
                _initialLoadDone = true;
                await _vm.LoadInitialAsync(CancellationToken.None).ConfigureAwait(true);
                ScrollToLatest();
            }
            // else: cache hit. VM bubbles are intact, ListView keeps
            // its scroll position courtesy of NavigationCacheMode.

            // Hook the ListView's inner ScrollViewer once the template has
            // realised. AttachScrollHandler is idempotent (returns early
            // when _scrollHooked is true), so re-running on cache-hit
            // returns is a no-op when the handler is still attached.
            var ignored = Dispatcher.RunAsync(
                Windows.UI.Core.CoreDispatcherPriority.Normal,
                AttachScrollHandler);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // Keep the VM subscribed to the bus while the page is parked
            // in the cache. That way new messages, edits,
            // read receipts, and typing indicators continue to update
            // the bubble collection in memory — when the user comes
            // back to this peer the conversation is already current
            // (no flash, no refetch). We DO unhook the
            // CollectionChanged → ScrollToLatest path because it would
            // try to scroll a detached ListView and the inner scroll
            // viewer that the older-load infrastructure depends on.
            if (_vm != null)
            {
                try { _vm.Messages.CollectionChanged -= OnMessagesCollectionChanged; }
                catch { }
            }
            DetachScrollHandler();
        }

        private async void OnSendClicked(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            bool sent = await _vm.SendAsync(CancellationToken.None).ConfigureAwait(true);
            if (sent) ScrollToLatest();
        }

        private async void OnComposerKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter || _vm == null || !_vm.CanSend) return;

            e.Handled = true;
            bool sent = await _vm.SendAsync(CancellationToken.None).ConfigureAwait(true);
            if (sent) ScrollToLatest();
        }

        private void OnMessagesCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // Skip auto-scroll-to-latest while a paginated older-load is in
            // flight. The OnInnerScrollerViewChanged restore step handles the
            // visual position for those Insert(0, …) events; ScrollIntoView
            // here would slam the view back to the bottom instead.
            if (_olderRestorePending) return;
            if (e == null) return;

            // Only follow the bottom for inserts that landed at the END of
            // the collection (server-confirmed, optimistic-send, incoming).
            // Prepends from PrependPage land at index 0 and shouldn't move
            // the view, even outside the older-restore window.
            if (e != null && e.Action == NotifyCollectionChangedAction.Add &&
                e.NewStartingIndex >= 0 && _vm != null && _vm.Messages != null)
            {
                int tail = _vm.Messages.Count - (e.NewItems != null ? e.NewItems.Count : 0);
                if (e.NewStartingIndex < tail) return;
                if (!IsNearBottom()) return;
                ScrollToLatest();
                return;
            }

            if (e.Action != NotifyCollectionChangedAction.Reset) return;
            if (!IsNearBottom()) return;
            ScrollToLatest();
        }

        private bool IsNearBottom()
        {
            if (_innerScroller == null) return true;
            return (_innerScroller.ScrollableHeight - _innerScroller.VerticalOffset) <= FollowBottomThresholdPx;
        }

        private void ScrollToLatest()
        {
            if (_vm == null || _vm.Messages == null || _vm.Messages.Count == 0 || MessageList == null) return;
            MessageList.ScrollIntoView(_vm.Messages[_vm.Messages.Count - 1]);
        }

        private void OnHeaderTapped(object sender, TappedRoutedEventArgs e)
        {
            if (_vm == null || Frame == null) return;
            Frame.Navigate(typeof(Profile.ProfilePage), new ProfilePageNavArgs
            {
                PeerKey = _vm.PeerKey,
                DisplayName = _vm.PeerTitle,
                StatusText = _vm.StatusText,
                PreferBackToChat = true
            });
        }

        // ---- Infinite scroll plumbing -----------------------------------

        /// <summary>
        /// Locates the ScrollViewer the ListView's templated ItemsPresenter
        /// hosts and hooks ViewChanged. Idempotent — safe to call until the
        /// ScrollViewer materialises (we retry via Dispatcher on first miss).
        /// </summary>
        private void AttachScrollHandler()
        {
            if (_scrollHooked || MessageList == null) return;

            _innerScroller = FindDescendant<ScrollViewer>(MessageList);
            if (_innerScroller == null)
            {
                // Template not realised yet — try again on the next tick.
                var ignored = Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Normal,
                    AttachScrollHandler);
                return;
            }

            _innerScroller.ViewChanged += OnInnerScrollerViewChanged;
            _scrollHooked = true;
        }

        private void DetachScrollHandler()
        {
            if (!_scrollHooked || _innerScroller == null) return;
            _innerScroller.ViewChanged -= OnInnerScrollerViewChanged;
            _innerScroller = null;
            _scrollHooked = false;
        }

        private void OnInnerScrollerViewChanged(object sender, ScrollViewerViewChangedEventArgs e)
        {
            // Stage 1: if a paginated load is still settling and IsIntermediate
            // events are firing, leave the restore-trigger logic alone.
            if (e != null && e.IsIntermediate) return;
            MaybeLoadOlderMessages();
        }

        private DateTime _lastOlderLoadAt = DateTime.MinValue;
        private const double OlderLoadCooldownMs = 350.0;

        private async void MaybeLoadOlderMessages()
        {
            if (_vm == null || _innerScroller == null) return;
            if (_vm.IsLoadingOlder || !_vm.HasMoreOlder) return;
            if (_innerScroller.ScrollableHeight <= 0) return;
            if (_innerScroller.VerticalOffset > OlderMessageLoadThresholdPx) return;

            // Cooldown: ScrollViewer.ViewChanged fires on every micro-scroll
            // and again on layout changes triggered by our own prepend. The
            // cooldown ensures we don't issue back-to-back paginated requests
            // within milliseconds of each other when a page returns fewer
            // messages than expected (the scan-skip recovery in TlDecoder
            // should normally yield a full page, but defensively we cap the
            // request rate here).
            DateTime now = DateTime.UtcNow;
            if ((now - _lastOlderLoadAt).TotalMilliseconds < OlderLoadCooldownMs) return;
            _lastOlderLoadAt = now;

            // Snapshot the current scroll geometry. After PrependPage adds
            // older bubbles at the top of the ObservableCollection, the
            // ScrollViewer's ExtentHeight grows; we offset the saved
            // VerticalOffset by that delta to keep the visible content stable.
            _savedExtentHeight = _innerScroller.ExtentHeight;
            _savedVerticalOffset = _innerScroller.VerticalOffset;
            _olderRestorePending = true;

            try
            {
                await _vm.LoadOlderAsync(CancellationToken.None).ConfigureAwait(true);
            }
            finally
            {
                // Restore on the dispatcher's Low priority so the ListView's
                // ItemsPresenter has had a chance to layout the prepended
                // containers. UpdateLayout() right before reading ExtentHeight
                // forces the measure/arrange pass when virtualization defers it.
                var ignored = Dispatcher.RunAsync(
                    Windows.UI.Core.CoreDispatcherPriority.Low,
                    RestoreScrollAfterOlderLoad);
            }
        }

        private void RestoreScrollAfterOlderLoad()
        {
            try
            {
                if (_innerScroller == null) return;
                _innerScroller.UpdateLayout();

                double delta = _innerScroller.ExtentHeight - _savedExtentHeight;
                if (delta < 0) delta = 0;

                double target = _savedVerticalOffset + delta;
                if (target > _innerScroller.ScrollableHeight)
                    target = _innerScroller.ScrollableHeight;

                _innerScroller.ChangeView(null, target, null, true);
            }
            finally
            {
                _olderRestorePending = false;
            }
        }

        private static T FindDescendant<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                var match = child as T;
                if (match != null) return match;
                var nested = FindDescendant<T>(child);
                if (nested != null) return nested;
            }
            return null;
        }
    }
}
