// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Account.Composition;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Ports.Inbound;
using Vianigram.Account.Ports.Outbound;
using Vianigram.Calls.Composition;
using Vianigram.Calls.Ports.Inbound;
using Vianigram.Chats.Composition;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Composition.Configuration;
using Vianigram.Composition.Infrastructure;
using Vianigram.Contacts.Composition;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Kernel.Capability;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Telemetry;
using Vianigram.Kernel.Time;
using Vianigram.Media.Composition;
using Vianigram.Media.Ports.Inbound;
using Vianigram.Messages.Composition;
using Vianigram.Messages.Ports.Inbound;
using Vianigram.Notifications.Composition;
using Vianigram.Notifications.Ports.Inbound;
using Vianigram.Privacy.Composition;
using Vianigram.Privacy.Ports.Inbound;
using Vianigram.Search.Composition;
using Vianigram.Search.Ports.Inbound;
using Vianigram.SecretChats.Composition;
using Vianigram.SecretChats.Ports.Inbound;
using Vianigram.Settings.Composition;
using Vianigram.Settings.Ports.Inbound;
using Vianigram.Stickers.Composition;
using Vianigram.Stickers.Ports.Inbound;
using Vianigram.Storage.Composition;
using Vianigram.Sync.Composition;
using Vianigram.Sync.Infrastructure;
using Vianigram.Sync.Ports.Inbound;

namespace Vianigram.Composition.Roots
{
    /// <summary>
    /// Single entry point used by App.xaml.cs to construct the dependency graph.
    ///
    /// <list type="bullet">
    ///   <item><see cref="BuildAsync(IEventBus, ILogger, IClock)"/> — kernel-only
    ///     wiring. Use this when a host wants to register additional contexts
    ///     manually.</item>
    ///   <item><see cref="BuildPhase2Async(IEventBus, ILogger, IClock, string, int)"/>
    ///     — full wiring: kernel + Storage + Account + Chats + Messages
    ///     + Sync. Performs the MTProto DH handshake against the configured
    ///     DC at composition time and wires the resulting MtProtoChannel
    ///     into every per-context outbound IMtProtoRpcPort. Falls back to the
    ///     no-DC stub adapters if the handshake fails so the App can still
    ///     boot to the login UI.</item>
    /// </list>
    /// </summary>
    public sealed class VianigramCompositionRoot
    {
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        // Deferred-init resolvers built on first Resolve<T>() touch, to keep
        // cold start fast. Used for bounded contexts that aren't on the
        // first-paint critical path (Stickers, SecretChats, Calls, Search,
        // Privacy, Notifications, Media). The factory runs once; the result
        // is then promoted into _services so subsequent resolves are O(1).
        private readonly Dictionary<Type, Func<object>> _lazyFactories = new Dictionary<Type, Func<object>>();
        private readonly object _lazyLock = new object();

        public void Register<T>(T instance) where T : class
        {
            if (instance == null) throw new ArgumentNullException("instance");
            _services[typeof(T)] = instance;
        }

        /// <summary>
        /// Cold-start helper. Registers a factory that produces
        /// <typeparamref name="T"/> on the first <see cref="Resolve{T}"/>
        /// or <see cref="TryResolve{T}"/> call. Use for bounded contexts
        /// that aren't required to render the first page — defers their
        /// composition cost out of OnLaunched and into the moment the
        /// user actually opens the relevant feature (sticker panel,
        /// secret chat, search, call, etc.).
        /// </summary>
        public void RegisterLazy<T>(Func<T> factory) where T : class
        {
            if (factory == null) throw new ArgumentNullException("factory");
            // Don't shadow an already-eager registration (eager wins).
            if (_services.ContainsKey(typeof(T))) return;
            _lazyFactories[typeof(T)] = delegate { return (object)factory(); };
        }

        public T Resolve<T>() where T : class
        {
            T svc;
            if (TryResolve<T>(out svc)) return svc;
            throw new InvalidOperationException("Service not registered: " + typeof(T).FullName);
        }

        public bool TryResolve<T>(out T instance) where T : class
        {
            object svc;
            if (_services.TryGetValue(typeof(T), out svc))
            {
                instance = (T)svc;
                return true;
            }
            // Lazy slot? Materialize under lock so concurrent first-touches
            // (e.g. two pages activating in parallel) don't double-build.
            Func<object> factory;
            if (_lazyFactories.TryGetValue(typeof(T), out factory))
            {
                lock (_lazyLock)
                {
                    if (_services.TryGetValue(typeof(T), out svc))
                    {
                        instance = (T)svc;
                        return true;
                    }
                    var sw = Stopwatch.StartNew();
                    object built = factory();
                    sw.Stop();
                    EarlyLog.Write("Boot", "lazy-build " + typeof(T).Name +
                        " elapsed=" + sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
                    _services[typeof(T)] = built;
                    _lazyFactories.Remove(typeof(T));
                    instance = (T)built;
                    return true;
                }
            }
            instance = null;
            return false;
        }

        /// <summary>
        /// Kernel-only builder: wires just the kernel-level ports. Returns a
        /// fully populated root ready for App startup or for incremental
        /// extension by the host.
        /// </summary>
        public static Task<VianigramCompositionRoot> BuildAsync(
            IEventBus eventBus,
            ILogger logger,
            IClock clock)
        {
            if (eventBus == null) throw new ArgumentNullException("eventBus");
            if (logger == null) throw new ArgumentNullException("logger");
            if (clock == null) throw new ArgumentNullException("clock");

            var root = new VianigramCompositionRoot();

            // Kernel ports.
            root.Register<IEventBus>(eventBus);
            root.Register<ILogger>(logger);
            // Logging foundation: factory hands out IComponentLogger
            // instances pre-tagged with their component name. Bounded contexts
            // ctor-inject ILoggerFactory and resolve once per class.
            var loggerFactory = new TimestampedLoggerFactory(logger);
            root.Register<ILoggerFactory>(loggerFactory);
            root.Register<IClock>(clock);
            root.Register<ICapabilityRegistry>(StaticCapabilityRegistry.Create().Build());
            root.Register<ITelemetry>(NullTelemetry.Instance);

            return TaskFromResult(root);
        }

        /// <summary>
        /// Full builder: kernel + Storage + Account + Chats + Messages + Sync.
        ///
        /// <para>
        /// Performs the MTProto DH handshake against the configured DC at
        /// composition time and opens an encrypted MtProtoChannel for the
        /// duration of the App session. The same physical adapter
        /// (<see cref="MtProtoChannelAdapter"/>) is registered against every
        /// per-context outbound IMtProtoRpcPort interface — bounded contexts
        /// stay decoupled (each defines its own port shape) but share the
        /// single transport.
        /// </para>
        /// <para>
        /// Storage adapters are wired via <see cref="StorageCompositionRoot.Build"/>;
        /// the encrypted JsonAuthKeyStore is bridged to the Account
        /// IAuthKeyStore shape via <see cref="BridgeAuthKeyStore"/>.
        /// </para>
        /// <para>
        /// The real <see cref="Vianigram.Sync.Ports.Outbound.IUpdatesPort"/>
        /// adapter (<see cref="MtProtoUpdatesAdapter"/>) is wired against the
        /// live MtProtoChannel: the encrypted_session read loop dispatches push
        /// updates (constructor ids 0x74ae4240, 0x725b04c3, 0x78d4dec1,
        /// 0x313bc7f8, 0x4d6deea5, 0x9015e101, 0xe317af7e) to the registered
        /// MtProtoUpdateHandler, which fans-out to managed IUpdatesPort
        /// subscribers. Sync's <c>SyncApplication</c> subscribes once on
        /// bootstrap and feeds incoming raw TL bytes into ProcessUpdatesHandler.
        /// </para>
        /// <para>
        /// Deferred storage work:
        /// <list type="bullet">
        ///   <item>Sync state, dialog, and message persistence — the in-memory
        ///     repositories are sufficient until the SQLite-backed bridges
        ///     land.</item>
        /// </list>
        /// <para>
        /// SRP-2048 is handled by the native crypto projection:
        /// <see cref="ISrpClientPort"/> now resolves to
        /// <see cref="NativeSrpClientPort"/>, which projects the native
        /// <c>Vianium.Crypto.SrpClient.ComputeProofAsync</c> WinMD entry
        /// point, returning both <c>A</c> and <c>M1</c> for
        /// <c>auth.checkPassword</c>.
        /// </para>
        /// </para>
        /// <para>
        /// On DH handshake failure (offline / unreachable DC) the host falls
        /// back to the no-DC stub adapters so the App can still navigate the
        /// login UI. The fallback is logged at WARN.
        /// </para>
        /// </summary>
        public static async Task<VianigramCompositionRoot> BuildPhase2Async(
            IEventBus eventBus,
            ILogger logger,
            IClock clock)
        {
            // Read the user's persisted "home DC" before resolving the
            // endpoint. Without this we always boot against
            // TelegramAppConfig.ActiveDcId (DC#2 by default), even after a
            // prior login successfully migrated the user to e.g. DC#1 — the
            // post-login Sync layer then opens an unauthorised channel and
            // updates.getState fails with AUTH_KEY_UNREGISTERED.
            int targetDcId = 0;
            string targetDcSource = "default";
            try
            {
                var preferredStore = new LocalSettingsPreferredDcStore();
                if (preferredStore.GetUserId() > 0L)
                {
                    targetDcId = preferredStore.GetHomeDcId();
                    if (targetDcId > 0) targetDcSource = "home";
                }

                if (targetDcId <= 0)
                {
                    targetDcId = preferredStore.GetLoginDcHint();
                    if (targetDcId > 0) targetDcSource = "login-hint";
                }
            }
            catch
            {
                // LocalSettings unavailable (designer / tests): fall through
                // to the configured default below.
            }
            if (targetDcId <= 0)
            {
                targetDcId = TelegramAppConfig.ActiveDcId;
                targetDcSource = "default";
            }
            EarlyLog.Write(
                "Boot",
                "boot DC selected: " + targetDcId + " (" + targetDcSource + ")");

            string dcHost;
            int dcPort;
            if (!TelegramDcOptions.TryGetEndpoint(
                targetDcId,
                TelegramAppConfig.UseTestEnvironment,
                out dcHost,
                out dcPort))
            {
                dcHost = "149.154.167.51";
                dcPort = 443;
            }

            return await BuildPhase2Async(eventBus, logger, clock, dcHost, dcPort, targetDcId).ConfigureAwait(false);
        }

        public static Task<VianigramCompositionRoot> BuildPhase2Async(
            IEventBus eventBus,
            ILogger logger,
            IClock clock,
            string dcHost,
            int dcPort)
        {
            return BuildPhase2Async(eventBus, logger, clock, dcHost, dcPort, 0);
        }

        public static async Task<VianigramCompositionRoot> BuildPhase2Async(
            IEventBus eventBus,
            ILogger logger,
            IClock clock,
            string dcHost,
            int dcPort,
            int initialDcId)
        {
            if (string.IsNullOrEmpty(dcHost)) throw new ArgumentException("dcHost required", "dcHost");
            if (dcPort <= 0 || dcPort > 65535) throw new ArgumentOutOfRangeException("dcPort");

            // Cold-start stopwatch instrumentation. Per-phase elapsed values
            // are emitted via EarlyLog ("Boot") so they show up in attached
            // ETW captures and the VS Output window. The marker convention is
            // "<phase> elapsed=<ms>ms".
            var totalSw = Stopwatch.StartNew();
            var phaseSw = new Stopwatch();
            EarlyLog.Write("Boot", "Composition begin");

            phaseSw.Restart();
            var root = await BuildAsync(eventBus, logger, clock).ConfigureAwait(false);
            phaseSw.Stop();
            EarlyLog.Write("Boot", "Kernel elapsed=" + phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // Tag composition-time messages with the standard component name
            // so the host log mirrors the rest of Vianigram.
            IComponentLogger compLog = new TimestampedLogger(logger, "Composition.Root");

            // ---- Peer cache (process-local, no network dependency) ----
            // Shared InMemoryPeerCache. Hydrated by the
            // MtProtoChannelAdapter (every typed RPC response that returns
            // users:Vector<User> / chats:Vector<Chat> populates it) and by
            // MtProtoUpdatesAdapter (push payloads carry the same slices).
            // Consumed by the same adapter when building inputUser /
            // inputChannel / inputPeerUser / inputPeerChannel — handlers
            // stay agnostic.
            var peerCache = new InMemoryPeerCache();
            root.Register<IPeerCache>(peerCache);

            // ---- Storage (no network dependency) ----
            // Build the Storage adapter set. The host owns the lifetime; bridges
            // below adapt the Storage stub shapes to the per-context port shapes.
            phaseSw.Restart();
            StorageRegistrations storage = StorageCompositionRoot.Build();
            phaseSw.Stop();
            EarlyLog.Write("Boot", "storage-build elapsed=" +
                phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // Bridge the encrypted JsonAuthKeyStore to the Account port shape.
            var authKeyStore = new BridgeAuthKeyStore(storage.AuthKeyStore);
            root.Register<IAuthKeyStore>(authKeyStore);

            // Account-internal SRP backed by the Vianigram.Core.Crypto WinMD
            // SrpClient. Heavy modular exponentiation runs off the UI thread
            // inside the native concurrency::create_async worker.
            ISrpClientPort srp = new NativeSrpClientPort();
            root.Register<ISrpClientPort>(srp);

            // ---- Auth-key generator (DH handshake) ----
            var keyGen = new AuthKeyGeneratorAdapter();
            root.Register<IAuthKeyGeneratorPort>(keyGen);

            // ---- MTProto channel: handshake + open ----
            // Cold-start tuning:
            //
            //   1. Fast path: if a persisted auth_key already exists for this DC,
            //      OpenAsync (TCP+key install only — no DH dance) is enough.
            //      This converts cold-start handshake cost (~1.5–2.0s on a
            //      1 GB device) into a single TCP RTT (~150–300ms).
            //
            //   2. No persisted key: kick the DH handshake off without awaiting
            //      it before navigation completes. Bounded contexts that need
            //      RPC (Sync.BootstrapAsync, etc.) await on the resulting Task
            //      via the deferred-channel adapter on first port call —
            //      ChatListPage can render its cached-dialogs view in the
            //      meantime.
            //
            //   On any failure we fall back to the stub adapters so the App
            //   still boots to the login UI offline.

            // ---- MTProxy bootstrap: must run BEFORE the first MTProto dial ----
            // ProxyBootstrap reads the saved descriptor from LocalSettings
            // directly (synchronous) and arms Vianigram.MTProto.MtProxyRuntime.
            // If no proxy is configured this clears the runtime — the
            // subsequent MtProtoChannel.OpenAsync dials direct. If an
            // MTProxy is configured but unreachable, the channel-open
            // attempt below will fail through the normal endpoint-failure
            // fallback and surface to the user as a connection error.
            //
            // Wiring this here (not later via the Settings ctx phase)
            // matters: SettingsCompositionRoot.Build runs after the
            // MTProto channel open, so any saved proxy would otherwise
            // miss the first dial.
            ProxyBootstrap.LoadAndApply(logger);

            phaseSw.Restart();
            object rpcAdapter = null;
            // The live native channel — non-null only when OpenAsync succeeded.
            // Captured here so the IUpdatesPort wiring below can attach the real
            // push-subscription adapter; the stub fallback path leaves it null.
            Vianigram.MTProto.MtProtoChannel liveChannel = null;
            DeferredMtProtoChannel deferredChannel = null;
            // Honour the persisted "home DC" override threaded in by the
            // shorter BuildPhase2Async. Falls back to the configured default
            // DC#2 for fresh installs / pre-login.
            int activeDcId = initialDcId > 0 ? initialDcId : TelegramAppConfig.ActiveDcId;

            // Persistence port for the user's home DC. Created here and
            // registered into the service map so the Account context can
            // write to it after auth.signIn succeeds.
            var preferredDcStore = new LocalSettingsPreferredDcStore();
            root.Register<IPreferredDcStore>(preferredDcStore);

            // Fast path: try the persisted key first. We give ourselves a tight
            // budget (5s) — if it doesn't come back quickly we fall through to
            // the deferred handshake path so we don't block first paint.
            bool hasPersistedSession = false;
            try
            {
                hasPersistedSession = preferredDcStore.GetUserId() > 0L && activeDcId > 0;
            }
            catch
            {
                hasPersistedSession = false;
            }

            if (hasPersistedSession)
            {
                // Persisted session markers prove we can build the app graph
                // and rehydrate auth state, but opening the native socket
                // should not block first paint. Build a lazy deferred channel:
                // the first real RPC or updates subscription starts the reopen.
                var deferred = new DeferredMtProtoChannel(
                    () =>
                    {
                        var cachedOpenCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                        return Task.Run(async () =>
                            await RunDeferredPersistedOpenAsync(
                                authKeyStore,
                                activeDcId,
                                dcHost,
                                dcPort,
                                compLog,
                                cachedOpenCts.Token).ConfigureAwait(false));
                    },
                    TimeSpan.FromSeconds(8));
                deferredChannel = deferred;
                rpcAdapter = new MtProtoChannelAdapter(
                    deferred,
                    peerCache,
                    keyGen,
                    authKeyStore,
                    activeDcId,
                    dcHost,
                    dcPort);

                phaseSw.Stop();
                EarlyLog.Write("Boot", "MTProto-channel-cached-deferred setup elapsed=" +
                    phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
            }

            // Slow path: no usable cached key. Instead of awaiting the DH
            // handshake here (1.5–2.0s on a 1 GB device), kick it off in
            // the background and wrap the resulting Task<MtProtoChannel> in
            // a DeferredMtProtoChannel. The MtProtoChannelAdapter is built
            // with the deferred ctor, so first-paint is no longer gated on
            // DH; bounded contexts that issue an RPC before DH lands simply
            // await the deferred wrapper on first port call (subject to the
            // wrapper's max-wait budget). The fast path above (cached
            // auth_key) still synchronizes inline because re-opening with a
            // cached key is a single TCP RTT (~150–300 ms) and resolving a
            // live channel synchronously lets the IUpdatesPort wiring stay
            // simple in the common case.
            //
            // Note: we cannot wire MtProtoUpdatesAdapter against a deferred
            // channel today (the adapter takes a live channel in its ctor).
            // On the slow path the App therefore boots in poll-only mode
            // until the next launch picks up the persisted auth_key; this
            // matches the behavior of the StubUpdatesPort fallback below.
            if (rpcAdapter == null)
            {
                phaseSw.Stop();
                EarlyLog.Write("Boot", "MTProto-cached-miss elapsed=" +
                    phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

                // Lazy DH+open. The factory persists the key on success and
                // produces the live MtProtoChannel; failures surface as a
                // faulted task that DeferredMtProtoChannel re-throws on first
                // port call.
                var deferred = new DeferredMtProtoChannel(
                    () =>
                    {
                        var bgCts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                        return Task.Run(async () =>
                            await RunDeferredHandshakeAsync(
                                keyGen,
                                authKeyStore,
                                activeDcId,
                                dcHost,
                                dcPort,
                                compLog,
                                bgCts.Token).ConfigureAwait(false));
                    },
                    TimeSpan.FromSeconds(8));
                deferredChannel = deferred;
                rpcAdapter = new MtProtoChannelAdapter(
                    deferred,
                    peerCache,
                    keyGen,
                    authKeyStore,
                    activeDcId,
                    dcHost,
                    dcPort);
                EarlyLog.Write("Boot", "MTProto channel prepared lazily (anonymous deferred)");
            }
            else
            {
                EarlyLog.Write("Boot", "DH-handshake skipped (persisted session)");
            }

            // Fallback path if anything above failed.
            if (rpcAdapter == null)
            {
                rpcAdapter = new StubMtProtoRpcPort(logger);
            }

            // Account gets a dedicated login client: one socket, private
            // initConnection state, and its own DC migration/reconnect loop.
            // The rest of the app keeps the pooled transport.
            var accountRpcAdapter = new AccountLoginMtProtoRpcPort(
                keyGen,
                authKeyStore,
                activeDcId,
                preferredDcStore);
            root.Register<Vianigram.Account.Ports.Outbound.IMtProtoRpcPort>(accountRpcAdapter);
            root.Register<Vianigram.Account.Ports.Outbound.IMtProtoDcProvider>(accountRpcAdapter);
            root.Register<AccountLoginMtProtoRpcPort>(accountRpcAdapter);

            // Concrete-type registration for the main pooled adapter so the
            // host (App.OnUserLoggedIn) can call the public MigrateToDcAsync
            // hook after auth.signIn lands. Only registered when the live
            // path succeeded — the stub fallback isn't migratable.
            var concreteMainAdapter = rpcAdapter as MtProtoChannelAdapter;
            if (concreteMainAdapter != null)
            {
                root.Register<MtProtoChannelAdapter>(concreteMainAdapter);
            }
            root.Register<Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            // Nine additional context faces.
            root.Register<Vianigram.Contacts.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Contacts.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Notifications.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Notifications.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Settings.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Settings.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Privacy.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Privacy.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Search.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Search.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Media.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Media.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Stickers.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Stickers.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.SecretChats.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.SecretChats.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);
            root.Register<Vianigram.Calls.Ports.Outbound.IMtProtoRpcPort>(
                (Vianigram.Calls.Ports.Outbound.IMtProtoRpcPort)rpcAdapter);

            // Sync auxiliary ports — push subscription is wired against the live
            // MtProtoChannel when available; otherwise we install the no-op stub
            // so Sync still boots (poll-only mode). The MtProtoUpdatesAdapter
            // owns its native subscription token for the App lifetime; it is
            // registered into the service map so the host can dispose it on
            // suspend / logout.
            Vianigram.Sync.Ports.Outbound.IUpdatesPort updatesPort;
            if (concreteMainAdapter != null)
            {
                var channelUpdatesPort = new MtProtoChannelUpdatesPort(
                    concreteMainAdapter,
                    liveChannel,
                    peerCache);
                root.Register<MtProtoChannelUpdatesPort>(channelUpdatesPort);
                updatesPort = channelUpdatesPort;
                if (liveChannel != null)
                {
                    compLog.Info("MtProtoUpdatesAdapter wired (push subscription active).");
                }
                else if (hasPersistedSession)
                {
                    compLog.Info("MtProtoUpdatesAdapter will wire after deferred channel opens.");
                }
                else
                {
                    compLog.Info("Anonymous boot: push updates will wire after login channel opens.");
                }
            }
            else
            {
                updatesPort = new StubUpdatesPort();
                compLog.Warn("No MtProtoChannelAdapter; falling back to StubUpdatesPort (no push).");
            }
            root.Register<Vianigram.Sync.Ports.Outbound.IUpdatesPort>(updatesPort);
            // Single-subscription model. Sync owns the IUpdatesPort
            // subscription and emits typed Remote* events on
            // the bus AFTER applying the cursor. MessagesUpdatesProcessor
            // bridges those Remote* events into Messages domain events
            // (MessageReceived with body, MessagesReadByPeer, PeerStatusChanged,
            // PeerTypingChanged) so the existing MessagesApplication +
            // ChatPage / ChatList wiring lights up with full payloads.
            try
            {
                var messagesUpdates = new MessagesUpdatesProcessor(eventBus);
                root.Register<MessagesUpdatesProcessor>(messagesUpdates);
                compLog.Info("MessagesUpdatesProcessor wired (Sync.Remote* → Messages bus events).");
            }
            catch (Exception ex)
            {
                compLog.Warn("MessagesUpdatesProcessor wire failed: " + ex.GetType().Name + ": " + ex.Message);
            }
            var syncStateRepo = new InMemorySyncStateRepository();
            root.Register<Vianigram.Sync.Ports.Outbound.ISyncStateRepository>(syncStateRepo);

            // ---- First-paint critical path contexts ----
            // Account, Chats, Messages, Sync, Contacts must be eager: the
            // initial page (LoginPage or ChatListPage) resolves them in the
            // page ctor / OnNavigatedTo, and the App.OnLaunched authorization
            // probe touches IAccountApi.

            // ---- Account ----
            phaseSw.Restart();
            var accountRpc = root.Resolve<Vianigram.Account.Ports.Outbound.IMtProtoRpcPort>();
            var accountApi = AccountCompositionRoot.Register(
                accountRpc,
                authKeyStore,
                keyGen,
                srp,
                eventBus,
                logger,
                clock,
                TelegramAppConfig.ApiId,
                TelegramAppConfig.ApiHash,
                activeDcId,
                preferredDcStore);
            root.Register<IAccountApi>(accountApi);

            // Rehydrate the auth aggregate from persisted (homeDc, userId,
            // auth_key) so a previously-signed-in user boots straight into
            // ChatListPage. We give it a small budget — if the store is
            // unhealthy we just fall through to the login UI rather than
            // hanging the composition.
            try
            {
                using (var rehydrateCts = new CancellationTokenSource(TimeSpan.FromSeconds(3)))
                {
                    bool restored = await accountApi.RehydrateFromPersistenceAsync(rehydrateCts.Token).ConfigureAwait(false);
                    EarlyLog.Write(
                        "Composition.Root",
                        restored ? "auth aggregate rehydrated from persistence"
                                 : "auth aggregate stayed Anonymous (no persisted session)");
                }
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "Composition.Root",
                    "rehydrate threw, continuing with Anonymous: " + ex.GetType().Name + ": " + ex.Message);
            }
            phaseSw.Stop();
            EarlyLog.Write("Boot", "ctx-Account elapsed=" + phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // ---- Chats ----
            phaseSw.Restart();
            var chatsRpc = root.Resolve<Vianigram.Chats.Ports.Outbound.IMtProtoRpcPort>();
            var chatsApi = ChatsCompositionRoot.Build(chatsRpc, eventBus);
            root.Register<IChatsApi>(chatsApi);
            phaseSw.Stop();
            EarlyLog.Write("Boot", "ctx-Chats elapsed=" + phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // ---- Messages ----
            phaseSw.Restart();
            var messagesRpc = root.Resolve<Vianigram.Messages.Ports.Outbound.IMtProtoRpcPort>();
            // Bridge the cross-context IPeerCache so LoadHistoryHandler can
            // resolve access_hash before serialising messages.getHistory.
            // Without this Telegram answers PEER_ID_INVALID in ~70ms because
            // the inputPeer carries access_hash=0.
            var peerHashAdapter = new Vianigram.Composition.Infrastructure.PeerAccessHashAdapter(peerCache);
            var messagesApi = MessagesCompositionRoot.Build(
                messagesRpc,
                eventBus,
                clock,
                logger,
                NullTelemetry.Instance,
                repository: null,
                idGenerator: null,
                peerHashes: peerHashAdapter);
            root.Register<IMessagesApi>(messagesApi);
            phaseSw.Stop();
            EarlyLog.Write("Boot", "ctx-Messages elapsed=" + phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // ---- Sync ----
            phaseSw.Restart();
            var syncRpc = root.Resolve<Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort>();
            // Pass a resolver delegate so updates.getChannelDifference
            // carries the real access_hash from IPeerCache. Without it
            // the server returns CHANNEL_INVALID on every gap recovery
            // and the
            // channel cursor stays frozen → ALL subsequent messages
            // from that channel disappear from the notification path.
            Func<long, long> channelAhResolver = channelId =>
            {
                long? ah = peerCache.GetChannelAccessHash(channelId);
                return ah.HasValue ? ah.Value : 0L;
            };
            var syncApi = SyncCompositionRoot.Register(
                syncRpc, updatesPort, syncStateRepo, eventBus, logger, clock,
                channelAhResolver);
            root.Register<ISyncApi>(syncApi);
            phaseSw.Stop();
            EarlyLog.Write("Boot", "ctx-Sync elapsed=" + phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // ---- Nine additional bounded contexts ----
            //
            // MtProtoChannelAdapter / StubMtProtoRpcPort both implement every
            // per-context IMtProtoRpcPort interface. Each context's
            // CompositionRoot.Build(rpc, bus, logger, clock) convenience
            // overload provides in-memory repositories and stub auxiliary
            // ports (IPasscodeHasher, IVoipMediaPort, ISecretCryptoPort,
            // ISecretChatRepository, INotificationProfileRepository,
            // IPlatformNotifier, IPreferencesStore, ISearchHistory,
            // IStickerRepository, IStickerCachePort, IContactRepository) so
            // the App composes end-to-end without further dependencies.
            //
            // TODO: replace the convenience overloads with explicit Build(...)
            // calls once Storage-backed repositories and native crypto/VoIP
            // WinMD ports land.

            // ---- Contacts (eager: touched by ChatListPage via ContactsApi.GetCachedAsync) ----
            phaseSw.Restart();
            var contactsRpc = root.Resolve<Vianigram.Contacts.Ports.Outbound.IMtProtoRpcPort>();
            var contactsApi = ContactsCompositionRoot.Build(contactsRpc, eventBus, logger, clock);
            root.Register<IContactsApi>(contactsApi);
            phaseSw.Stop();
            EarlyLog.Write("Boot", "ctx-Contacts elapsed=" + phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // ---- Settings (eager: login-flow language pack + theme load on first paint) ----
            // Disk-backed preferences store (LocalSettings + LocalFolder
            // spill-over for oversized values) so saved theme / language /
            // MTProxy descriptor / data-usage policies survive launches.
            // The MtProxyRuntimeSink propagates a Save into the native
            // transport so the next channel reconnect picks up the new
            // descriptor; the boot-time arming was already applied above
            // via ProxyBootstrap.LoadAndApply.
            phaseSw.Restart();
            var settingsRpc = root.Resolve<Vianigram.Settings.Ports.Outbound.IMtProtoRpcPort>();
            var settingsStore = new LocalSettingsPreferencesStore(logger);
            root.Register<Vianigram.Settings.Ports.Outbound.IPreferencesStore>(settingsStore);
            var proxySink = new MtProxyRuntimeSink(logger);
            root.Register<Vianigram.Settings.Ports.Outbound.IProxyRuntimeSink>(proxySink);
            var proxyProbe = new MtProxyProbe(logger);
            root.Register<Vianigram.Settings.Ports.Outbound.IProxyProbe>(proxyProbe);
            var settingsApi = SettingsCompositionRoot.Build(
                settingsRpc, settingsStore, eventBus, logger, clock, proxySink, proxyProbe);
            root.Register<ISettingsApi>(settingsApi);

            // Wire the reconnect hook now that both the sink and the
            // MtProtoChannelAdapter exist. When the user saves a new
            // proxy descriptor the sink's Apply runs, arms the native
            // runtime, and then fires this hook — which kicks off a
            // best-effort channel reopen against the same DC so the
            // next RPC call uses the new transport.
            //
            // We capture concreteMainAdapter (built ~200 lines earlier
            // in the same BuildPhase2Async scope) and dispatch via
            // Task.Run so Apply itself stays sync and the settings save
            // path returns to the UI immediately.
            if (concreteMainAdapter != null)
            {
                var capturedAdapter = concreteMainAdapter;
                var hookLog = new TimestampedLogger(logger, "MtProxy.Reconnect");
                proxySink.SetReconnectHook(delegate(Vianigram.Settings.Domain.ValueObjects.ProxyConfig cfg)
                {
                    Task.Run(async delegate
                    {
                        try
                        {
                            bool reopened = await capturedAdapter
                                .ReopenAfterProxyChangeAsync(CancellationToken.None)
                                .ConfigureAwait(false);
                            hookLog.Info("post-proxy-save channel reopen result=" + reopened);
                        }
                        catch (Exception ex)
                        {
                            hookLog.Warn("post-proxy-save reopen threw: " + ex.GetType().Name + ": " + ex.Message);
                        }
                    });
                });
            }
            phaseSw.Stop();
            EarlyLog.Write("Boot", "ctx-Settings elapsed=" + phaseSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");

            // ---- Lazy contexts (cold-start tuning) ----
            // The bounded contexts below are NOT needed for first paint.
            // Registering them lazily defers their construction cost out
            // of OnLaunched and into the moment the user actually opens
            // the relevant feature. Each closure captures the rpcAdapter
            // (already-typed) and shared kernel ports; building one of
            // these on demand is on the order of 5–25ms (in-memory repos
            // and stub aux ports).

            // ---- Notifications (lazy: first taps deferred until first push delivery / settings panel) ----
            root.RegisterLazy<INotificationsApi>(delegate
            {
                var rpc = root.Resolve<Vianigram.Notifications.Ports.Outbound.IMtProtoRpcPort>();
                return NotificationsCompositionRoot.Build(rpc, eventBus, logger, clock, peerHashAdapter);
            });

            // ---- Privacy (lazy: only touched from Settings -> Privacy panel) ----
            root.RegisterLazy<IPrivacyApi>(delegate
            {
                var rpc = root.Resolve<Vianigram.Privacy.Ports.Outbound.IMtProtoRpcPort>();
                return PrivacyCompositionRoot.Build(rpc, eventBus, logger, clock);
            });

            // ---- Search (lazy: only touched when search panel opens) ----
            root.RegisterLazy<ISearchApi>(delegate
            {
                var rpc = root.Resolve<Vianigram.Search.Ports.Outbound.IMtProtoRpcPort>();
                return SearchCompositionRoot.Build(rpc, eventBus, logger, clock);
            });

            // ---- Media (lazy: photo/video viewer + uploader paths only) ----
            //
            // The IMediaApi internally uses an IMediaCache to stash downloaded
            // chunks. The PeerAvatarFetcher
            // in Vianigram.App.Services re-reads bytes from that cache after
            // a successful DownloadAsync to materialise a BitmapImage for
            // the chat list. Previously MediaCompositionRoot.Build created
            // its own internal InMemoryMediaCache and never exposed it,
            // so the App-side resolution of IMediaCache always returned
            // null — the fetcher was never instantiated and HD avatars
            // never appeared (only the blurry stripped thumb did). We now
            // construct the cache here, register it globally, AND pass it
            // into MediaCompositionRoot.Build so both halves of the
            // download → read pipeline share the same instance.
            var mediaCache = new Vianigram.Media.Infrastructure.InMemoryMediaCache();
            root.Register<Vianigram.Media.Ports.Outbound.IMediaCache>(mediaCache);
            root.RegisterLazy<IMediaApi>(delegate
            {
                var rpc = root.Resolve<Vianigram.Media.Ports.Outbound.IMtProtoRpcPort>();
                return MediaCompositionRoot.Build(rpc, eventBus, clock, logger, NullTelemetry.Instance, mediaCache);
            });

            // ---- Stickers (lazy: only touched when sticker panel opens) ----
            root.RegisterLazy<IStickersApi>(delegate
            {
                var rpc = root.Resolve<Vianigram.Stickers.Ports.Outbound.IMtProtoRpcPort>();
                return StickersCompositionRoot.Build(rpc, eventBus, logger, clock);
            });

            // ---- SecretChats (lazy: only touched when user opens a secret chat) ----
            // Convenience overload wires StubSecretCryptoPort + InMemorySecretChatRepository.
            root.RegisterLazy<ISecretChatsApi>(delegate
            {
                var rpc = root.Resolve<Vianigram.SecretChats.Ports.Outbound.IMtProtoRpcPort>();
                return SecretChatsCompositionRoot.Build(rpc, eventBus, logger, clock);
            });

            // ---- Calls (lazy: only touched when an inbound call signal lands or user initiates) ----
            // Signaling stays in Vianigram.Calls; crypto/media capability is
            // bridged through the VianiumVoIP native WinMD adapter.
            root.RegisterLazy<ICallsApi>(delegate
            {
                var rpc = root.Resolve<Vianigram.Calls.Ports.Outbound.IMtProtoRpcPort>();
                // Bind the shared peer cache so phone.requestCall sources
                // the user's access_hash automatically — without this the
                // server returns USER_ID_INVALID for every outgoing call.
                Vianigram.Calls.Ports.Outbound.IUserAccessHashPort userHashes = null;
                IPeerCache localCache;
                if (root.TryResolve<IPeerCache>(out localCache) && localCache != null)
                {
                    userHashes = new Vianigram.Composition.Infrastructure.CallsUserAccessHashAdapter(localCache);
                }
                var voip = new Vianigram.Composition.Infrastructure.VianiumVoipCallsAdapter(
                    new Vianium.VoIP.VoipRuntime(),
                    rpc,
                    logger);
                return CallsCompositionRoot.Build(
                    rpc,
                    voip,
                    voip,
                    new Vianigram.Calls.Infrastructure.InMemoryCallRepository(),
                    eventBus,
                    logger,
                    clock,
                    userHashes);
            });

            try
            {
                var callsUpdates = new CallsUpdatesProcessor(
                    updatesPort,
                    delegate
                    {
                        ICallsApi api;
                        if (!root.TryResolve<ICallsApi>(out api) || api == null) return null;
                        return api as Vianigram.Calls.Application.CallsApplication;
                    });
                root.Register<CallsUpdatesProcessor>(callsUpdates);
                compLog.Info("CallsUpdatesProcessor wired (updatePhoneCall -> Calls).");
            }
            catch (Exception ex)
            {
                compLog.Warn("CallsUpdatesProcessor wire failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            try
            {
                var callsPoller = new CallsUpdatePoller(
                    root.Resolve<Vianigram.Sync.Ports.Outbound.IMtProtoRpcPort>(),
                    delegate
                    {
                        ISyncApi api;
                        return root.TryResolve<ISyncApi>(out api) ? api : null;
                    },
                    delegate
                    {
                        ICallsApi api;
                        if (!root.TryResolve<ICallsApi>(out api) || api == null) return null;
                        return api as Vianigram.Calls.Application.CallsApplication;
                    },
                    eventBus,
                    clock);
                root.Register<CallsUpdatePoller>(callsPoller);
                compLog.Info("CallsUpdatePoller wired (updates.getDifference fallback for calls).");
            }
            catch (Exception ex)
            {
                compLog.Warn("CallsUpdatePoller wire failed: " + ex.GetType().Name + ": " + ex.Message);
            }

            totalSw.Stop();
            EarlyLog.Write("Boot", "Composition end total=" +
                totalSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
            return root;
        }

        // Local Task.FromResult shim — avoids dependency on the System.Threading.Tasks
        // overload set being available in WP8.1 RT library targets.
        private static Task<VianigramCompositionRoot> TaskFromResult(VianigramCompositionRoot value)
        {
            var tcs = new TaskCompletionSource<VianigramCompositionRoot>();
            tcs.SetResult(value);
            return tcs.Task;
        }

        private static bool IsUsableAuthKeyRecord(AuthKeyRecord record)
        {
            if (record == null) return false;
            if (record.AuthKey == null || record.AuthKey.Length != 256) return false;
            if (record.AuthKeyId == 0) return false;
            return true;
        }

        private static void ClearAuthKeyRecord(AuthKeyRecord record)
        {
            if (record == null || record.AuthKey == null) return;
            Array.Clear(record.AuthKey, 0, record.AuthKey.Length);
        }

        private static async Task<Vianigram.MTProto.MtProtoChannel> RunDeferredPersistedOpenAsync(
            BridgeAuthKeyStore authKeyStore,
            int dcId,
            string dcHost,
            int dcPort,
            IComponentLogger compLog,
            CancellationToken ct)
        {
            if (authKeyStore == null) throw new ArgumentNullException("authKeyStore");

            AuthKeyRecord key = null;
            try
            {
                var loadSw = Stopwatch.StartNew();
                key = await authKeyStore.LoadAsync(dcId, ct).ConfigureAwait(false);
                loadSw.Stop();
                EarlyLog.Write("Boot", "auth-key-load-deferred elapsed=" +
                    loadSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "ms result=" + (key == null ? "miss" : "hit"));

                if (!IsUsableAuthKeyRecord(key))
                {
                    compLog.Warn("Deferred auth_key missing or malformed for DC #" + dcId + ".");
                    if (key != null)
                    {
                        try
                        {
                            await authKeyStore.DeleteAsync(dcId, ct).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            compLog.Warn("Deleting malformed deferred auth_key failed: " +
                                ex.GetType().Name + ": " + ex.Message);
                        }
                    }
                    throw new InvalidOperationException("No usable persisted auth_key for DC #" + dcId);
                }

                return await RunDeferredCachedOpenAsync(key, dcId, dcHost, dcPort, compLog, ct).ConfigureAwait(false);
            }
            finally
            {
                ClearAuthKeyRecord(key);
            }
        }

        // Deferred-RPC helper. Runs the DH handshake + persistence
        // + MtProtoChannel.OpenAsync entirely off the cold-launch critical
        // path; the resulting Task<MtProtoChannel> is wrapped in a
        // DeferredMtProtoChannel and consumed lazily by MtProtoChannelAdapter
        // on first port call. Errors propagate as a faulted task.
        private static async Task<Vianigram.MTProto.MtProtoChannel> RunDeferredCachedOpenAsync(
            AuthKeyRecord key,
            int dcId,
            string dcHost,
            int dcPort,
            IComponentLogger compLog,
            CancellationToken ct)
        {
            if (key == null) throw new ArgumentNullException("key");

            TelegramDcEndpoint[] endpoints = TelegramDcOptions.GetConnectionPlan(
                dcId,
                TelegramAppConfig.UseTestEnvironment,
                dcHost,
                dcPort);
            if (endpoints.Length == 0)
            {
                throw new InvalidOperationException("No MTProto endpoints configured for DC #" + dcId);
            }

            compLog.Info("Deferred cached open plan for DC #" + dcId + ": " +
                TelegramDcOptions.DescribePlan(endpoints));

            Exception lastError = null;
            try
            {
                for (int i = 0; i < endpoints.Length; i++)
                {
                    TelegramDcEndpoint endpoint = endpoints[i];
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        var channel = await OpenChannelWithDcDeadlineAsync(
                            endpoint,
                            dcId,
                            key,
                            TelegramDcOptions.CachedOpenTimeout,
                            ct).ConfigureAwait(false);
                        sw.Stop();

                        if (channel == null)
                        {
                            TelegramDcOptions.ReportEndpointFailure(endpoint);
                            compLog.Warn("MtProtoChannel.OpenAsync timed out/null (cached deferred) against " +
                                endpoint.ToString() + " after " +
                                sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms.");
                            continue;
                        }

                        TelegramDcOptions.ReportEndpointSuccess(endpoint);
                        compLog.Info("MtProtoChannel reopened with cached auth_key (id=0x" +
                            key.AuthKeyId.ToString("x16") + ") against " + endpoint.ToString() + " in " +
                            sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms.");
                        return channel;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        sw.Stop();
                        lastError = ex;
                        TelegramDcOptions.ReportEndpointFailure(endpoint);
                        compLog.Warn("MtProtoChannel.OpenAsync (cached deferred) threw after " +
                            sw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            "ms against " + endpoint.ToString() + ": " + ex.GetType().Name + ": " + ex.Message);
                    }
                }

                throw new InvalidOperationException(
                    "All deferred cached MTProto endpoints failed for DC #" + dcId,
                    lastError);
            }
            finally
            {
                ClearAuthKeyRecord(key);
            }
        }

        private static async Task<Vianigram.MTProto.MtProtoChannel> RunDeferredHandshakeAsync(
            AuthKeyGeneratorAdapter keyGen,
            BridgeAuthKeyStore authKeyStore,
            int dcId,
            string dcHost,
            int dcPort,
            IComponentLogger compLog,
            CancellationToken ct)
        {
            var hsSw = Stopwatch.StartNew();
            AuthKeyRecord key = null;
            try
            {
                TelegramDcEndpoint[] endpoints = TelegramDcOptions.GetConnectionPlan(
                    dcId,
                    TelegramAppConfig.UseTestEnvironment,
                    dcHost,
                    dcPort);
                if (endpoints.Length == 0)
                {
                    throw new InvalidOperationException("No MTProto endpoints configured for DC #" + dcId);
                }

                compLog.Info("Deferred DH/open plan for DC #" + dcId + ": " +
                    TelegramDcOptions.DescribePlan(endpoints));

                Exception lastError = null;
                for (int i = 0; i < endpoints.Length; i++)
                {
                    TelegramDcEndpoint endpoint = endpoints[i];
                    try
                    {
                        if (key == null)
                        {
                            var keyResult = await GenerateKeyWithDeadlineAsync(keyGen, endpoint, dcId, ct).ConfigureAwait(false);
                            if (!keyResult.IsOk || keyResult.Value == null || !IsUsableAuthKeyRecord(keyResult.Value))
                            {
                                string msg = (keyResult.Error == null) ? "(no detail)" : keyResult.Error.ToString();
                                TelegramDcOptions.ReportEndpointFailure(endpoint);
                                compLog.Warn("DH handshake failed (deferred) against " + endpoint.ToString() + ": " + msg);
                                continue;
                            }

                            key = keyResult.Value;
                            try
                            {
                                await authKeyStore.SaveAsync(dcId, key, ct).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                compLog.Warn("Persisting auth_key failed (deferred, continuing in-memory): " +
                                    ex.GetType().Name + ": " + ex.Message);
                            }
                        }

                        var openSw = Stopwatch.StartNew();
                        var channel = await OpenChannelWithDcDeadlineAsync(
                            endpoint,
                            dcId,
                            key,
                            TelegramDcOptions.ChannelOpenTimeout,
                            ct).ConfigureAwait(false);
                        openSw.Stop();
                        if (channel == null)
                        {
                            TelegramDcOptions.ReportEndpointFailure(endpoint);
                            compLog.Warn("MtProtoChannel.OpenAsync timed out/null (deferred) against " +
                                endpoint.ToString() + " after " +
                                openSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms.");
                            continue;
                        }

                        TelegramDcOptions.ReportEndpointSuccess(endpoint);
                        compLog.Info("MtProtoChannel opened (deferred) against " + endpoint.ToString() +
                            " (auth_key_id=0x" + key.AuthKeyId.ToString("x16") + ").");
                        return channel;
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        TelegramDcOptions.ReportEndpointFailure(endpoint);
                        compLog.Warn("Deferred MTProto open failed against " + endpoint.ToString() + ": " +
                            ex.GetType().Name + ": " + ex.Message);
                    }
                }

                throw new InvalidOperationException(
                    "All deferred MTProto endpoints failed for DC #" + dcId,
                    lastError);
            }
            finally
            {
                hsSw.Stop();
                EarlyLog.Write("Boot", "DH-handshake-deferred elapsed=" +
                    hsSw.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + "ms");
                ClearAuthKeyRecord(key);
            }
        }

        private static async Task<Result<AuthKeyRecord, AccountError>> GenerateKeyWithDeadlineAsync(
            AuthKeyGeneratorAdapter keyGen,
            TelegramDcEndpoint endpoint,
            int dcId,
            CancellationToken ct)
        {
            // DC-aware so the obfuscated MTProxy init packet routes to
            // the correct upstream DC. Direct-dial ignores dcId.
            Task<Result<AuthKeyRecord, AccountError>> keyTask =
                keyGen.GenerateForDcAsync(endpoint.Host, endpoint.Port, dcId, ct);
            Task timeoutTask = Task.Delay(TelegramDcOptions.AuthKeyGenerationTimeout, ct);
            Task completed = await Task.WhenAny(keyTask, timeoutTask).ConfigureAwait(false);
            if (!object.ReferenceEquals(completed, keyTask))
            {
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                ObserveFault(keyTask);
                return Result<AuthKeyRecord, AccountError>.Fail(
                    AccountError.NetworkError("auth_key generation timed out against " + endpoint.ToString()));
            }

            return await keyTask.ConfigureAwait(false);
        }

        private static async Task<Vianigram.MTProto.MtProtoChannel> OpenChannelWithDeadlineAsync(
            TelegramDcEndpoint endpoint,
            AuthKeyRecord key,
            TimeSpan timeout,
            CancellationToken ct)
        {
            return await OpenChannelWithDcDeadlineAsync(endpoint, /* dcId = */ 2, key, timeout, ct).ConfigureAwait(false);
        }

        // DC-aware variant. When an MTProxy is active the dcId is embedded
        // in the obfuscated init packet so the proxy routes to the
        // correct upstream Telegram DC. Plain-TCP path ignores the
        // parameter.
        private static async Task<Vianigram.MTProto.MtProtoChannel> OpenChannelWithDcDeadlineAsync(
            TelegramDcEndpoint endpoint,
            int dcId,
            AuthKeyRecord key,
            TimeSpan timeout,
            CancellationToken ct)
        {
            Task<Vianigram.MTProto.MtProtoChannel> openTask = Vianigram.MTProto.MtProtoChannel
                .OpenWithDcAsync(
                    endpoint.Host,
                    endpoint.Port,
                    dcId > 0 ? dcId : 2,
                    key.AuthKey,
                    key.AuthKeyId,
                    key.ServerSalt,
                    key.ServerTimeOffset)
                .AsTask(ct);
            Task timeoutTask = Task.Delay(timeout, ct);
            Task completed = await Task.WhenAny(openTask, timeoutTask).ConfigureAwait(false);
            if (!object.ReferenceEquals(completed, openTask))
            {
                if (ct.IsCancellationRequested)
                {
                    throw new OperationCanceledException(ct);
                }

                ObserveDetachedOpen(openTask);
                return null;
            }

            return await openTask.ConfigureAwait(false);
        }

        private static void ObserveFault(Task task)
        {
            if (task == null) return;
            task.ContinueWith(
                delegate(Task t)
                {
                    var ignored = t.Exception;
                },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void ObserveDetachedOpen(Task<Vianigram.MTProto.MtProtoChannel> task)
        {
            if (task == null) return;
            task.ContinueWith(
                delegate(Task<Vianigram.MTProto.MtProtoChannel> t)
                {
                    if (t.IsFaulted)
                    {
                        var ignored = t.Exception;
                        return;
                    }

                    if (t.IsCanceled)
                    {
                        return;
                    }

                    Vianigram.MTProto.MtProtoChannel channel = t.Result;
                    if (channel != null)
                    {
                        try { channel.Close(); }
                        catch (Exception) { }
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
