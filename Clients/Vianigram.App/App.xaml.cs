// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// App.xaml.cs — Vianigram client entry point.
//
// Host shell. Builds the composition root with the real
// Vianigram.Core.MTProto channel attached (DH handshake at startup against
// Telegram test DC #2) and decides whether the user is already authorized,
// navigating to WelcomePage or ChatListPage accordingly. After an authorized
// launch, kicks off ISyncApi.BootstrapAsync in the background so the
// dialog list / message history / live updates can flow before the user
// touches the UI.
//
// Beyond startup wiring, no business state lives here.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Phone.UI.Input;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.Pages;
using Vianigram.App.Services;
using Vianigram.Composition.Roots;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Time;
using Vianigram.Storage.Composition;
using Vianigram.Sync.Ports.Inbound;

namespace Vianigram.App
{
    public sealed partial class App : Application
    {
        /// <summary>
        /// Static composition root. Pages resolve <see cref="IAccountApi"/>,
        /// <see cref="IChatsApi"/>, <see cref="IMessagesApi"/>, <see cref="ISyncApi"/>
        /// via <c>App.Composition.Resolve&lt;T&gt;()</c>.
        /// </summary>
        public static VianigramCompositionRoot Composition { get; private set; }

        /// <summary>
        /// Navigation service exposed to ViewModels. Set on first launch.
        /// The route-mapped <see cref="NavigationService"/>. The interface
        /// is also registered into <see cref="Composition"/> so VMs constructor-
        /// inject <see cref="INavigationService"/> via AppViewModels factories.
        /// </summary>
        public static INavigationService Navigation { get; private set; }

        // Component logger; resolved against Composition once BuildPhase2Async
        // returns. Pre-composition log calls go through EarlyLog.Write directly.
        private IComponentLogger _appLog;
        private CallNavigationCoordinator _callNavigation;
        private static int _storageMaintenanceScheduled;
        private static int _syncBootstrapScheduled;
        private static int _mainChannelWarmupScheduled;

        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            HardwareButtons.BackPressed += OnHardwareBackPressed;
            UnhandledException += (sender, args) =>
            {
                try
                {
                    EarlyLog.Write("App", "UnhandledException: " + args.Message);
                    if (args.Exception != null)
                        EarlyLog.Write("App", "Exception: " + args.Exception);
                }
                catch
                {
                    // Last-chance handler must never throw.
                }
            };
        }

        // Captured by OnActivated when the app is launched via a
        // tg://proxy?... URL (or any other tg:// link, future-proof).
        // Read by the post-composition routing logic so we can
        // navigate to ProxySettingsPage with the URL as parameter
        // before the first frame paints.
        private static string _pendingActivationUri;

        protected override void OnActivated(IActivatedEventArgs args)
        {
            try
            {
                EarlyLog.Write("Boot", "OnActivated kind=" +
                    (args != null ? args.Kind.ToString() : "null"));

                var protocol = args as Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs;
                if (protocol != null && protocol.Uri != null)
                {
                    string url = protocol.Uri.ToString();
                    if (!string.IsNullOrEmpty(url))
                    {
                        _pendingActivationUri = url;
                        EarlyLog.Write("Boot", "OnActivated protocol URI captured: " + url);
                    }
                }
            }
            catch (Exception ex)
            {
                EarlyLog.Write("App", "OnActivated threw: " + ex.GetType().Name + ": " + ex.Message);
            }

            // Dispatch into the same path OnLaunched uses. Passing
            // null Arguments is fine — the activation URI is carried
            // through _pendingActivationUri instead.
            OnLaunched(null);
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            // Total-launch stopwatch for cold-start timing. Reset on
            // every entry so re-activations get fresh timings.
            var launchSw = Stopwatch.StartNew();
            try
            {
                EarlyLog.Write("Boot", "OnLaunched begin");

                // Restore the user's last-chosen UI language before any
                // resource lookups happen. Without this the first frame
                // navigation would render in the system default and only
                // pick up the override after the next app launch.
                LanguageService.LoadFromSettings();

                // Register the foreground UI dispatcher so domain-event
                // subscribers wired via
                // IEventBus.SubscribeOnUi auto-marshal back to the UI
                // thread without each call site remembering to do so.
                Vianigram.App.Services.Dispatch.Register(
                    new Vianigram.App.Services.WindowsPhoneUiDispatcher());

                var rootFrame = Window.Current.Content as Frame;
                if (rootFrame == null)
                {
                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    // Allow up to 6 cached pages with
                    // NavigationCacheMode.Enabled (LRU-style).
                    // ChatListPage uses Required (always cached, doesn't
                    // count toward this budget), so 6 is for any future
                    // pages that adopt Enabled. WinRT's default is 10 —
                    // we trim a bit because WP 8.1 devices have limited
                    // memory headroom and we'd rather rebuild a rarely-
                    // visited page than thrash the GC.
                    rootFrame.CacheSize = 6;
                    Window.Current.Content = rootFrame;
                    EarlyLog.Write("Boot", "Frame initialized elapsed=" +
                        launchSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

                    // Route-mapped navigation service. The instance is
                    // bound to the rootFrame here and re-registered into the
                    // composition root below so injected VMs share it.
                    var nav = new NavigationService();
                    nav.Initialize(rootFrame);
                    Navigation = nav;

                    Type initialPage;

                    // Time-bomb gate (see BuildExpiry.cs). When the public
                    // alpha is past its three-month window the entire shell
                    // is replaced with ExpiredPage — no composition root
                    // built, no MTProto channel opened, no storage touched.
                    // The user is steered to the Telegram channel where the
                    // next build is announced. The gate also bumps a
                    // monotonic high-water mark in LocalSettings so a
                    // future clock-rollback can't reopen the app.
                    if (BuildExpiry.IsExpired())
                    {
                        EarlyLog.Write("Boot",
                            "BuildExpiry gate: EXPIRED — routing to ExpiredPage");
                        rootFrame.Navigate(typeof(ExpiredPage));
                        Window.Current.Activate();
                        launchSw.Stop();
                        EarlyLog.Write("Boot", "Window activated total=" +
                            launchSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            "ms (expired)");
                        return;
                    }

                    try
                    {
                        // Full graph: kernel + Storage + Account + Chats +
                        // Messages + Sync, with the real Vianigram.Core.MTProto
                        // channel performing DH against the test DC. Storage is
                        // wired inside BuildPhase2Async; we no longer call
                        // StorageCompositionRoot.Register here.
                        Composition = await VianigramCompositionRoot.BuildPhase2Async(
                            new InMemoryEventBus(),
                            new DebugLogger(),
                            new SystemClock()).ConfigureAwait(true);
                        EarlyLog.Write("Boot", "Composition built elapsed=" +
                            launchSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

                        // Wave-2 logging: now that Composition is built we can
                        // resolve the component-tagged logger for App's own use.
                        ILoggerFactory factory;
                        if (Composition.TryResolve<ILoggerFactory>(out factory) && factory != null)
                        {
                            _appLog = factory.ForComponent("App");
                            _appLog.Info("composition ready");
                        }

                        // Expose the App-side INavigationService through
                        // the same composition root every page-VM resolves from.
                        Composition.Register<INavigationService>(nav);

                        // Bind the AppViewModels factory surface so pages
                        // can call AppViewModels.CreateXxxPageViewModel() during
                        // OnNavigatedTo without threading the root through every
                        // page constructor.
                        AppViewModels.Initialize(Composition);

                        IEventBus appBus;
                        if (Composition.TryResolve<IEventBus>(out appBus) && appBus != null)
                        {
                            _callNavigation = new CallNavigationCoordinator(appBus, nav, AppLog.For("App.Calls"));
                        }

                        initialPage = AppServices.PickInitialPage(Composition);

                        // ChatListPage schedules Sync bootstrap after its
                        // first dialog load. That keeps navigation/first paint
                        // and the UI-critical dialogs.getDialogs RPC free from
                        // the background updates.getState call.
                    }
                    catch (Exception ex)
                    {
                        EarlyLog.Write("App", "composition failed: " + ex);
                        initialPage = typeof(WelcomePage);
                    }

                    EarlyLog.Write("Boot", "Frame.Navigate begin elapsed=" +
                        launchSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
                    rootFrame.Navigate(initialPage, e != null ? e.Arguments : null);
                }

                // Protocol activation handler: if the app was started
                // (or resumed) via tg://proxy?... we route to the
                // proxy settings page with the URL as parameter so the
                // VM can pre-fill the form. Runs AFTER the normal
                // initial navigation so the back-stack has a sensible
                // page to fall back to (Welcome/ChatList).
                string pendingUri = _pendingActivationUri;
                if (!string.IsNullOrEmpty(pendingUri))
                {
                    _pendingActivationUri = null;
                    if (Navigation != null
                        && (pendingUri.StartsWith("tg://proxy", StringComparison.OrdinalIgnoreCase)
                            || pendingUri.IndexOf("t.me/proxy", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        try
                        {
                            Navigation.NavigateTo(Route.ProxySettings, pendingUri);
                            EarlyLog.Write("Boot", "Activation routed to ProxySettings via tg://");
                        }
                        catch (Exception navEx)
                        {
                            EarlyLog.Write("App", "Activation->ProxySettings navigation threw: " +
                                navEx.GetType().Name + ": " + navEx.Message);
                        }
                    }
                }

                Window.Current.Activate();
                launchSw.Stop();
                EarlyLog.Write("Boot", "Window activated total=" +
                    launchSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
                if (_appLog != null) _appLog.Info("Window activated");

                if (rootFrame.Content is ChatListPage)
                {
                    ScheduleMainChannelWarmup();
                    // Rehydrated-session boot path: PickInitialPage returned
                    // ChatListPage because storage already had an authorised
                    // session, which means OnUserLoggedIn() never fires for
                    // this launch (it's only triggered by fresh QR / phone
                    // login). Without this, MPNS registration, the
                    // foreground-toast subscription, the background
                    // coordination heartbeat, and the OS-level
                    // PeriodicTask + VoIP keep-alive triggers never get
                    // wired up, so test messages produce zero toasts even
                    // though the Sync→Messages bridge is publishing
                    // RemoteMessageReceived correctly.
                    EnsureAuthorizedSessionWiring();
                }
            }
            catch (Exception ex)
            {
                if (_appLog != null) _appLog.Fatal("OnLaunched FATAL: " + ex);
                else EarlyLog.Write("App", "OnLaunched FATAL: " + ex);
                throw;
            }
        }

        /// <summary>
        /// Login pages call this after a successful 2FA-or-direct sign-in so the
        /// Sync engine can cold-start before ChatListPage queries dialogs.
        /// Idempotent (Sync.BootstrapAsync is itself idempotent on retry).
        ///
        /// Two phases run sequentially in a detached task:
        ///   1) Re-target the main MtProtoChannelAdapter to the user's home
        ///      DC (where auth.signIn just authorised an auth_key). Without
        ///      this the next updates.getState fires on the default DC#2
        ///      with a stranger auth_key and the server replies
        ///      AUTH_KEY_UNREGISTERED, blowing up Sync bootstrap.
        ///   2) Run Sync.BootstrapAsync against the now-authorised channel.
        /// </summary>
        public static void OnUserLoggedIn()
        {
            if (Composition == null) return;
            Interlocked.Exchange(ref _syncBootstrapScheduled, 0);
            ScheduleDeferredSyncBootstrap();
            EnsureAuthorizedSessionWiring();
        }

        /// <summary>
        /// Idempotent post-authorization wiring shared by the fresh-login
        /// path (<see cref="OnUserLoggedIn"/>) and the rehydrated-session
        /// boot path (<see cref="OnLaunched"/> when PickInitialPage returns
        /// ChatListPage). Each scheduler inside is itself idempotent so a
        /// double-call does not double-register.
        ///
        /// Sequence:
        ///   1) MPNS push channel + account.registerDevice + foreground
        ///      MessageReceived toast surface.
        ///   2) Background-task layer: heartbeat / unread-summary
        ///      writer that the Vianigram.App.BackgroundTasks WinRT
        ///      component reads, plus OS-level PeriodicTask + VoIP
        ///      keep-alive triggers.
        /// </summary>
        private static void EnsureAuthorizedSessionWiring()
        {
            if (Composition == null) return;
            // Register the MPNS push channel and POST its URI to Telegram
            // via account.registerDevice. Best-effort — failures only mean
            // we lose notifications until next login.
            SchedulePushRegistration();
            // Start the heartbeat / unread-summary writer that the
            // Vianigram.App.BackgroundTasks WinRT component reads, and
            // register the OS-level PeriodicTask + VoIP keep-alive trigger.
            StartBackgroundCoordination();
            ScheduleBackgroundTaskRegistration();
            // Start the live-tile-painting service. Subscribes to
            // MessageReceived/MessageReadByMe, cycles up
            // to 5 tile frames showing recent unread peers + bodies,
            // and pushes a numeric badge.
            EnsureLiveTileService();
        }

        private static BackgroundCoordination _backgroundCoordination;
        private static int _backgroundTaskRegistrationScheduled;

        public static BackgroundCoordination BackgroundCoordinator
        {
            get { return _backgroundCoordination; }
        }

        private static void StartBackgroundCoordination()
        {
            var composition = Composition;
            if (composition == null) return;
            if (_backgroundCoordination != null) return;
            try
            {
                Vianigram.Kernel.Events.IEventBus bus;
                if (!composition.TryResolve<Vianigram.Kernel.Events.IEventBus>(out bus) || bus == null) return;
                IComponentLogger log = AppLog.For("App.BgCoord");
                _backgroundCoordination = new BackgroundCoordination(bus, log);
            }
            catch (Exception ex)
            {
                EarlyLog.Write("App", "background-coordination start threw: " + ex);
            }
        }

        private static void ScheduleBackgroundTaskRegistration()
        {
            if (Interlocked.Exchange(ref _backgroundTaskRegistrationScheduled, 1) != 0) return;
            Task.Run(async () =>
            {
                IComponentLogger log = AppLog.For("App.BgRegister");
                try
                {
                    var registrar = new BackgroundTaskRegistrar(log);
                    await registrar.RegisterAllAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    log.Warn("background-tasks: schedule threw " + ex.GetType().Name + ": " + ex.Message);
                }
            });
        }

        private static int _pushRegistrationScheduled;
        private static PushNotificationsService _pushService;
        private static MutedPeersStore _mutedPeersStore;
        private static LiveTileService _liveTileService;

        public static LiveTileService LiveTile
        {
            get { return _liveTileService; }
        }

        private static void EnsureLiveTileService()
        {
            if (_liveTileService != null) return;
            var composition = Composition;
            if (composition == null) return;
            try
            {
                Vianigram.Kernel.Events.IEventBus bus;
                if (!composition.TryResolve<Vianigram.Kernel.Events.IEventBus>(out bus) || bus == null) return;
                Vianigram.Composition.Infrastructure.IPeerCache cache;
                composition.TryResolve<Vianigram.Composition.Infrastructure.IPeerCache>(out cache);
                _liveTileService = new LiveTileService(bus, cache, AppLog.For("App.LiveTile"));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("App", "live-tile init threw: " + ex);
            }
        }

        private static void EnsureMutedPeersStore()
        {
            if (_mutedPeersStore != null) return;
            var composition = Composition;
            if (composition == null) return;
            try
            {
                Vianigram.Kernel.Events.IEventBus bus;
                if (!composition.TryResolve<Vianigram.Kernel.Events.IEventBus>(out bus) || bus == null) return;
                _mutedPeersStore = new MutedPeersStore(bus, AppLog.For("App.Mute"));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("App", "muted-peers store init threw: " + ex);
            }
        }

        public static PushNotificationsService PushService
        {
            get { return _pushService; }
        }

        private static void SchedulePushRegistration()
        {
            var composition = Composition;
            if (composition == null) return;
            if (Interlocked.Exchange(ref _pushRegistrationScheduled, 1) != 0) return;

            Task.Run(async () =>
            {
                IComponentLogger log = AppLog.For("App.Push");
                try
                {
                    Vianigram.Account.Ports.Inbound.IAccountApi account;
                    if (!composition.TryResolve<Vianigram.Account.Ports.Inbound.IAccountApi>(out account)
                        || account == null)
                    {
                        log.Warn("push.boot: IAccountApi not resolvable; skipping registration");
                        return;
                    }

                    Vianigram.Kernel.Events.IEventBus bus;
                    if (!composition.TryResolve<Vianigram.Kernel.Events.IEventBus>(out bus) || bus == null)
                    {
                        log.Warn("push.boot: IEventBus not resolvable; skipping registration");
                        return;
                    }

                    // Replace any prior instance — re-login (account
                    // switch) needs a fresh registration.
                    if (_pushService != null)
                    {
                        try { _pushService.Dispose(); }
                        catch { }
                    }

                    // Optional: peer cache → toast title resolution. If
                    // the cache hasn't been wired (or hasn't observed
                    // the peer yet) the service falls back to the raw
                    // PeerKey so the toast still surfaces.
                    Vianigram.Composition.Infrastructure.IPeerCache peerCache;
                    if (!composition.TryResolve<Vianigram.Composition.Infrastructure.IPeerCache>(
                        out peerCache))
                    {
                        peerCache = null;
                    }

                    // The mute store listens to RemoteNotifySettingsChanged
                    // from the bus and filters muted peers out of the toast
                    // path. One instance per app lifetime; we cache it
                    // statically so re-registers (account switch) reuse
                    // the same store rather than leaking subscriptions.
                    EnsureMutedPeersStore();

                    _pushService = new PushNotificationsService(account, bus, log, peerCache, _mutedPeersStore);
                    using (var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(30)))
                    {
                        await _pushService.RegisterAsync(cts.Token).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    log.Warn("push.boot: register threw " + ex.GetType().Name + ": " + ex.Message);
                }
            });
        }

        public static void NavigateToMainPage(Frame frame)
        {
            if (frame == null) return;
            if (frame.Navigate(typeof(ChatListPage)))
            {
                frame.BackStack.Clear();
            }
        }

        public static void ScheduleDeferredStorageMaintenance()
        {
            if (Interlocked.Exchange(ref _storageMaintenanceScheduled, 1) != 0) return;

            try
            {
                StorageCompositionRoot.ScheduleDeferredMaintenance();
            }
            catch (Exception ex)
            {
                EarlyLog.Write("App", "storage maintenance schedule failed: " + ex);
            }
        }

        public static void ScheduleDeferredSyncBootstrap()
        {
            var composition = Composition;
            if (composition == null) return;
            if (Interlocked.Exchange(ref _syncBootstrapScheduled, 1) != 0) return;

            try
            {
                MigrateMainChannelToHomeDcThenBootstrapSync(composition);
            }
            catch (Exception ex)
            {
                EarlyLog.Write("App", "sync bootstrap schedule failed: " + ex);
            }
        }

        public static void ScheduleMainChannelWarmup()
        {
            var composition = Composition;
            if (composition == null) return;
            if (Interlocked.Exchange(ref _mainChannelWarmupScheduled, 1) != 0) return;

            Task.Run(async () =>
            {
                IComponentLogger log = AppLog.For("App");

                int homeDcId = 0;
                try
                {
                    Vianigram.Account.Ports.Outbound.IPreferredDcStore preferred;
                    if (composition.TryResolve<Vianigram.Account.Ports.Outbound.IPreferredDcStore>(out preferred) && preferred != null)
                    {
                        homeDcId = preferred.GetHomeDcId();
                    }
                }
                catch (Exception ex)
                {
                    log.Warn("main channel warm-up home DC lookup threw: " + ex.Message);
                }

                if (homeDcId <= 0) return;

                try
                {
                    Vianigram.Composition.Infrastructure.MtProtoChannelAdapter mainAdapter;
                    if (composition.TryResolve<Vianigram.Composition.Infrastructure.MtProtoChannelAdapter>(out mainAdapter) && mainAdapter != null)
                    {
                        log.Info("main channel warm-up begin dc=" + homeDcId);
                        bool warmed = await mainAdapter.MigrateToDcAsync(homeDcId, false, CancellationToken.None).ConfigureAwait(false);
                        log.Info("main channel warm-up result=" + warmed + " dc=" + homeDcId);

                        // H (Media DC pre-warm) — kick off avatar/file DCs
                        // now so the first ChatListPage avatar download
                        // doesn't pay the open + auth.import cost on the
                        // demand path.
                        if (warmed)
                        {
                            SchedulePrewarmCommonMediaDcs(mainAdapter, homeDcId, log);
                        }
                    }
                }
                catch (Exception ex)
                {
                    log.Warn("main channel warm-up threw: " + ex);
                }
            });
        }

        /// <summary>
        /// Fire-and-forget pre-warm of the Telegram media DC cluster
        /// (DC#2 EU and DC#4 AMS — the avatar/file storage DCs). Runs
        /// in parallel with the rest of post-login bootstrap so the
        /// first ChatListPage avatar fetch finds a ready session and
        /// skips both the TCP open and the auth.importAuthorization
        /// round-trip. Best-effort: per-DC failures are logged and
        /// swallowed.
        ///
        /// Caller invariants: <paramref name="adapter"/> must be the
        /// live MtProtoChannelAdapter (not the stub), the user must
        /// already be authorised, and the main channel must have just
        /// landed on <paramref name="homeDcId"/>. We skip pre-warming
        /// the home DC itself — every RPC there reuses the main
        /// channel, no media session needed.
        /// </summary>
        private static void SchedulePrewarmCommonMediaDcs(
            Vianigram.Composition.Infrastructure.MtProtoChannelAdapter adapter,
            int homeDcId,
            IComponentLogger log)
        {
            if (adapter == null) return;

            // The static set of Telegram media-cluster DC ids. Pre-warming
            // every non-home DC here is cheap (8 RPCs total in the worst
            // case) and saves ~350-500 ms on the first media download.
            int[] mediaDcs = new int[] { 2, 4 };

            for (int i = 0; i < mediaDcs.Length; i++)
            {
                int targetDcId = mediaDcs[i];
                if (targetDcId == homeDcId)
                {
                    // No separate session needed — the main channel
                    // already handles RPCs for this DC.
                    continue;
                }

                int capturedDcId = targetDcId;
                Task.Run(async delegate
                {
                    try
                    {
                        var sw = Stopwatch.StartNew();
                        await adapter.PrewarmMediaDcAsync(capturedDcId, CancellationToken.None).ConfigureAwait(false);
                        sw.Stop();
                        log.Info("media DC#" + capturedDcId + " prewarm dispatched elapsed=" +
                            sw.ElapsedMilliseconds + "ms");
                    }
                    catch (Exception ex)
                    {
                        log.Warn("media DC#" + capturedDcId + " prewarm threw: " + ex);
                    }
                });
            }
        }

        private void OnHardwareBackPressed(object sender, BackPressedEventArgs e)
        {
            try
            {
                var frame = Window.Current != null
                    ? Window.Current.Content as Frame
                    : null;

                if (frame == null || !frame.CanGoBack) return;

                e.Handled = true;
                frame.GoBack();
            }
            catch (Exception ex)
            {
                if (_appLog != null) _appLog.Error("Hardware back failed: " + ex);
                else EarlyLog.Write("App", "Hardware back failed: " + ex);
            }
        }

        private static void MigrateMainChannelToHomeDcThenBootstrapSync(VianigramCompositionRoot composition)
        {
            IComponentLogger log = AppLog.For("App");

            int homeDcId = 0;
            try
            {
                Vianigram.Account.Ports.Outbound.IPreferredDcStore preferred;
                if (composition.TryResolve<Vianigram.Account.Ports.Outbound.IPreferredDcStore>(out preferred) && preferred != null)
                {
                    homeDcId = preferred.GetHomeDcId();
                }
            }
            catch (Exception ex)
            {
                log.Warn("home DC lookup threw: " + ex.Message);
            }

            // Detached task — UI thread must not wait on a network round-trip.
            Task.Run(async () =>
            {
                if (homeDcId > 0)
                {
                    Vianigram.Composition.Infrastructure.MtProtoChannelAdapter mainAdapter;
                    if (composition.TryResolve<Vianigram.Composition.Infrastructure.MtProtoChannelAdapter>(out mainAdapter) && mainAdapter != null)
                    {
                        try
                        {
                            log.Info("post-login: migrating main MtProtoChannel to home DC#" + homeDcId);
                            bool migrated = await mainAdapter.ReopenToDcWithPersistedAuthKeyAsync(homeDcId, CancellationToken.None).ConfigureAwait(false);
                            log.Info("post-login: main channel reopen result=" + migrated + " dc=" + homeDcId);

                            // H (Media DC pre-warm) — same dispatch as
                            // the rehydrated-session path. Runs in
                            // parallel with BootstrapSyncCoreAsync so
                            // updates.getState and avatar pre-warm
                            // overlap on the wire.
                            if (migrated)
                            {
                                SchedulePrewarmCommonMediaDcs(mainAdapter, homeDcId, log);
                            }
                        }
                        catch (Exception ex)
                        {
                            log.Warn("post-login: main channel migration threw: " + ex);
                        }
                    }
                    else
                    {
                        log.Warn("post-login: MtProtoChannelAdapter not registered; skipping migration");
                    }
                }
                else
                {
                    log.Info("post-login: no home DC persisted; skipping migration");
                }

                await BootstrapSyncCoreAsync(composition, log).ConfigureAwait(false);
            });
        }

        private static async Task BootstrapSyncCoreAsync(VianigramCompositionRoot composition, IComponentLogger log)
        {
            ISyncApi sync;
            if (!composition.TryResolve<ISyncApi>(out sync) || sync == null) return;

            try
            {
                var result = await sync.BootstrapAsync(CancellationToken.None).ConfigureAwait(false);
                if (!result.IsOk)
                {
                    string detail = (result.Error == null) ? "(no error)" : result.Error.ToString();
                    log.Warn("Sync bootstrap failed: " + detail);
                }
                else
                {
                    log.Info("Sync bootstrap completed.");
                }
            }
            catch (Exception ex)
            {
                log.Error("Sync bootstrap threw: " + ex);
            }
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            string msg = "Navigation failed for " +
                (e.SourcePageType != null ? e.SourcePageType.FullName : "(null)") +
                " — Exception: " + (e.Exception != null ? e.Exception.ToString() : "(none)");
            if (_appLog != null) _appLog.Error(msg);
            else EarlyLog.Write("App", msg);
            throw new InvalidOperationException("Failed to load Page " + e.SourcePageType.FullName, e.Exception);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                if (_appLog != null) _appLog.Info("OnSuspending");
                else EarlyLog.Write("App", "OnSuspending");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
