# Shell & Host — Composition Root + Native Bridges + Background Agent

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md). Assumes DDD+hex, managed Kernel, ports + adapters. The Vianigram mirror of the equivalent document that lives in the `vianium-managed-kernel\docs\managed-architecture\11-shell-and-host.md` sibling.

## Purpose

This document covers **three interconnected concerns**:

1. **`Vianigram.Composition`** — the **Composition Root** (manual DI). The only place where the bounded contexts are wired to each other via cross-context adapters (which respect rule 3 of [principles.md](principles.md): no direct cross-context references).
2. **`Vianigram.Shell`** — the managed layer that hosts the bridges to the native stack: WinMDs published by the `vianium-tls`, `vianium-http`, `vianium-net`, `vianium-mtproto` (with its `src\tl\` and `src\mtproto\` subcomponents), `vianium-crypto`, `vianium-voip` siblings, plus the local context `Vianigram.Core.Media`. It also centralizes app-level navigation between pages.
3. **`Vianigram.Agent`** — a WP8.1 **background task** registered under `PushNotificationTrigger`. It is a secondary host that starts when a push arrives and must operate in less than 25 seconds wall-clock with < 16 MB of memory.

## Scope split

| Project | Responsibility |
|---|---|
| `Vianigram.Composition` | DI registration, cross-context adapters (ACL implementations), object lifetimes, root container, lifecycle hooks |
| `Vianigram.Shell` | Native bridges (HTTP, TLS, MTProto, TL, Crypto, Media, Voip, Net), App-level page navigation, hardware back button service, App lifecycle hooks (suspend/resume), memory pressure monitor |
| `Vianigram.App` | Only XAML pages + the activation entry point + composition root construction |
| `Vianigram.Agent` | The background task entry point. Does NOT share memory with `Vianigram.App`; shares storage (`LocalSettings`, `LocalFolder`) with file-locking discipline |

---

## 1. `Vianigram.Composition` — layout

```
Core/Vianigram.Composition/
├── Vianigram.Composition.csproj         (references ALL the contexts + Shell + Kernel)
├── Properties/AssemblyInfo.cs
│
├── Container/
│   ├── ICompositionContainer.cs         (a minimal DI interface)
│   ├── CompositionContainer.cs          (manual impl; no reflection)
│   ├── CompositionScope.cs              (per-account scope, per-page scope)
│   └── CompositionException.cs
│
├── Roots/
│   ├── VianigramCompositionRoot.cs      (the main entry point: BuildAsync(...) returns a container)
│   ├── KernelModule.cs                  (registers Clock, Logger, EventBus, Telemetry, CapabilityRegistry, Random)
│   ├── StorageModule.cs                 (registers LocalSettings, LocalFolder, SecretStore, SQLite)
│   ├── NetModule.cs                     (registers HTTP, TLS, sockets)
│   ├── MtprotoModule.cs                 (registers MTProto + the root TL gateway)
│   ├── AuthModule.cs                    (registers the Auth context)
│   ├── MessagingModule.cs               (registers Messaging + Dialogs + SecretChats)
│   ├── MediaModule.cs                   (registers Media + Voip)
│   ├── StickersModule.cs                (registers Stickers)
│   ├── NotificationsModule.cs           (registers Notifications)
│   ├── SettingsModule.cs                (registers Settings)
│   ├── SearchModule.cs                  (registers Search)
│   ├── PrivacyModule.cs                 (registers Privacy)
│   ├── ContactsModule.cs                (registers Contacts)
│   └── PresentationModule.cs            (registers ViewModels)
│
├── Adapters/
│   ├── TlMessagingGatewayAdapter.cs
│   ├── TlStickerGatewayAdapter.cs
│   ├── TlNotificationsGatewayAdapter.cs
│   ├── TlSearchGatewayAdapter.cs
│   ├── TlPrivacyGatewayAdapter.cs
│   ├── TlAuthGatewayAdapter.cs
│   ├── TlContactsGatewayAdapter.cs
│   ├── MediaStickerDecoderAdapter.cs    (Vianigram.Core.Media → IStickerDecoder)
│   ├── MediaPhotoDecoderAdapter.cs
│   ├── MediaVoiceDecoderAdapter.cs
│   ├── VoipNativeAdapter.cs
│   ├── CryptoNativeAdapter.cs
│   ├── HttpNativeAdapter.cs
│   ├── NetNativeAdapter.cs
│   ├── SettingsLookupAdapter.cs         (Vianigram.Settings.Api.V1 → IXxxSettingsLookup in other contexts)
│   └── EventBusForwardAdapter.cs        (App ↔ Agent event mirror via file)
│
├── Storage/
│   ├── LocalSettingsAdapter.cs
│   ├── LocalFolderObjectStore.cs
│   ├── LocalFolderSecretStore.cs
│   ├── SqliteConnectionFactory.cs
│   └── JsonSerializer.cs
│
├── Kernel/
│   ├── DefaultKernelInfra.cs            (SystemClock, DebugLogger, InMemoryEventBus, NullTelemetry, RandomCryptoProvider)
│   ├── CompositeLogger.cs
│   ├── FileTelemetry.cs
│   └── UiOverlayLogger.cs
│
└── Lifecycle/
    ├── AppLifecycleObserver.cs          (suspend → save state, resume → getDifference + restore)
    ├── AppFocusEventsAdapter.cs         (CoreApplication.Suspending/Resuming → IAppFocusEvents)
    └── ContainerStartupSequence.cs      (defined init order)
```

### `ICompositionContainer`

Minimal DI without reflection (WP8.1 reflection is slow + restrictive). Explicit registration per type. No auto-wiring.

```csharp
namespace Vianigram.Composition.Container
{
    public interface ICompositionContainer : IDisposable
    {
        T Resolve<T>() where T : class;
        bool TryResolve<T>(out T instance) where T : class;
        ICompositionContainer CreateScope();
    }

    public sealed class CompositionContainer : ICompositionContainer
    {
        private readonly Dictionary<Type, Func<ICompositionContainer, object>> _factories = new Dictionary<Type, Func<ICompositionContainer, object>>();
        private readonly Dictionary<Type, object> _singletons = new Dictionary<Type, object>();
        private readonly object _lock = new object();
        private readonly CompositionContainer _parent;

        public CompositionContainer() { }
        private CompositionContainer(CompositionContainer parent) { _parent = parent; }

        public void RegisterSingleton<T>(Func<ICompositionContainer, T> factory) where T : class
        {
            lock (_lock)
            {
                _factories[typeof(T)] = c =>
                {
                    object existing;
                    if (_singletons.TryGetValue(typeof(T), out existing)) return existing;
                    var inst = factory(c);
                    _singletons[typeof(T)] = inst;
                    return inst;
                };
            }
        }

        public void RegisterTransient<T>(Func<ICompositionContainer, T> factory) where T : class
        {
            lock (_lock) { _factories[typeof(T)] = c => factory(c); }
        }

        public T Resolve<T>() where T : class
        {
            T inst;
            if (TryResolve(out inst)) return inst;
            throw new CompositionException("Type not registered: " + typeof(T).FullName);
        }

        public bool TryResolve<T>(out T instance) where T : class
        {
            Func<ICompositionContainer, object> fac;
            lock (_lock)
            {
                if (!_factories.TryGetValue(typeof(T), out fac))
                {
                    if (_parent != null) return _parent.TryResolve(out instance);
                    instance = null; return false;
                }
            }
            instance = (T)fac(this);
            return true;
        }

        public ICompositionContainer CreateScope() { return new CompositionContainer(this); }

        public void Dispose()
        {
            lock (_lock)
            {
                foreach (var kv in _singletons)
                {
                    var d = kv.Value as IDisposable;
                    if (d != null) try { d.Dispose(); } catch { }
                }
                _singletons.Clear();
                _factories.Clear();
            }
        }
    }

    public sealed class CompositionException : Exception { public CompositionException(string m) : base(m) { } }
}
```

### `VianigramCompositionRoot`

```csharp
namespace Vianigram.Composition
{
    public sealed class VianigramCompositionRoot
    {
        public async Task<ICompositionContainer> BuildAsync(IEventBus events, ILogger logger, IClock clock, CancellationToken ct)
        {
            var container = new CompositionContainer();

            // Phase 0: Kernel (non-async)
            KernelModule.Register(container, events, logger, clock);

            // Phase 1: Storage adapters (non-async; LocalSettings is sync, LocalFolder lazy)
            StorageModule.Register(container);

            // Phase 2: Net + TLS (native bridges)
            NetModule.Register(container);

            // Phase 3: Settings (consumed by everything else)
            SettingsModule.Register(container);

            // Phase 4: MTProto + TL gateway (consumed by Auth, Messaging, etc.)
            MtprotoModule.Register(container);

            // Phase 5: Auth (must be ready before others can call TL)
            await AuthModule.RegisterAsync(container, ct).ConfigureAwait(false);

            // Phase 6: Privacy (passcode controls decryption of all other state)
            PrivacyModule.Register(container);

            // Phase 7: Bounded contexts (parallel registration; no cross-deps among these)
            ContactsModule.Register(container);
            MessagingModule.Register(container);
            MediaModule.Register(container);
            StickersModule.Register(container);
            NotificationsModule.Register(container);
            SearchModule.Register(container);

            // Phase 8: Cross-context adapters
            AdapterRegistry.Register(container);

            // Phase 9: Presentation
            PresentationModule.Register(container);

            return container;
        }
    }
}
```

Phase ordering matches the pattern used by the sibling `vianium-managed-kernel`. Auth is async because it may need to read the passcode-locked auth blob (depends on Privacy) and decode the active session via MTProto.

### `KernelModule`

```csharp
namespace Vianigram.Composition.Roots
{
    public static class KernelModule
    {
        public static void Register(CompositionContainer c, IEventBus events, ILogger logger, IClock clock)
        {
            c.RegisterSingleton<IClock>(_ => clock);
            c.RegisterSingleton<ILogger>(_ => logger);
            c.RegisterSingleton<IEventBus>(_ => events);
            c.RegisterSingleton<ITelemetry>(_ => new NullTelemetry());
            c.RegisterSingleton<ICapabilityRegistry>(_ => BuildCapabilities());
            c.RegisterSingleton<IRandom>(_ => new CryptoRandomProvider());
        }

        private static ICapabilityRegistry BuildCapabilities()
        {
            var reg = new CapabilityRegistry();
            reg.Enable("notifications.toasts");
            reg.Enable("notifications.tile_updates");
            reg.Enable("notifications.badge_updates");
            reg.Enable("stickers.animated");
            reg.Enable("search.global");
            reg.Enable("search.public");
            reg.Enable("privacy.passcode");
            // ...
            return reg;
        }
    }
}
```

---

## 2. Native bridges — how managed calls native

Vianigram consumes **8 native WinMDs**, almost all published by sibling repos of the `vianium` org (only `Vianigram.Core.Media` lives in this repo):

| WinMD | Provides | Managed adapter (in Composition) |
|---|---|---|
| `Vianium.Core.Tls` (sibling `vianium-tls`) | TLS handshake + record layer | `TlsBridge` (singleton) |
| `Vianium.Core.Http` (sibling `vianium-http`) | HTTP/1.1 + H2 client | `HttpNativeAdapter` (impl `IHttpClientPort`) |
| `Vianium.Core.Net` (sibling `vianium-net`) | Sockets, DNS, network info | `NetNativeAdapter` |
| `Vianium.Mtproto` (sibling `vianium-mtproto` `src\mtproto\`) | MTProto framing + cipher + AuthKey lifecycle | `MtprotoBridge` (singleton) |
| `Vianium.Tl` (sibling `vianium-mtproto` `src\tl\`) | TL schema codecs (serialize/deserialize) | `TlCodecBridge` (singleton) |
| `Vianium.Crypto` (sibling `vianium-crypto`) | RSA, AES-IGE, AES-GCM, PBKDF2, HMAC, SRP | `CryptoNativeAdapter` |
| `Vianigram.Core.Media` (local) | WebP/JPEG/PNG decode, Lottie/TGS player, Opus encode/decode for voice, image rescale | `MediaPhotoDecoderAdapter`, `MediaStickerDecoderAdapter`, `MediaVoiceDecoderAdapter` |
| `Vianium.Voip` (sibling `vianium-voip`; includes `third_party\libtgvoip\` for layer 92 and `src\tgcalls\` for tgcalls v2) | layer-92 / tgcalls v2 stack + audio capture/playback | `VoipNativeAdapter` |

### WinMD ABI rules

- Types crossing the ABI are **WinRT-compatible**: primitives, `string`, `IBuffer`, `IAsyncOperation<T>`, `IAsyncAction`, `[Windows.Foundation.Metadata.Activatable]` structs, enums.
- Large buffers (TL messages, crypto blobs, decoded frames) travel as `Windows.Storage.Streams.IBuffer`. Managed conversion: `IBuffer.ToArray()` or `byte[].AsBuffer()` (a WinRT extension).
- Managed → native callbacks go as `Action<T>` or `EventHandler<T>` projected to a WinRT delegate.
- **No exceptions cross-ABI**: native code returns `Result<T, NativeError>` (projected as a WinRT struct) that the adapter maps to a managed `Result<T, Error>`.
- **No async void**: native code exposes `IAsyncOperation<T>` that `.AsTask()` converts to a `Task<T>`.

### Example: `MtprotoBridge`

```csharp
namespace Vianigram.Composition.Adapters
{
    using NativeMtproto = Vianium.Mtproto.MtprotoSession;     // sibling vianium-mtproto
    using Vianium.Tl;                                          // sibling vianium-mtproto (src\tl\)

    public sealed class MtprotoBridge : IMtprotoSession, IDisposable
    {
        private readonly NativeMtproto _native;

        public MtprotoBridge(NativeMtproto native) { _native = native; }

        public async Task<Result<TlMessageId, Error>> SendAsync(byte[] tlPayload, bool requiresAck, CancellationToken ct)
        {
            try
            {
                var buf = tlPayload.AsBuffer();
                var op = _native.SendAsync(buf, requiresAck);
                using (ct.Register(() => op.Cancel()))
                {
                    var nativeResult = await op.AsTask().ConfigureAwait(false);
                    if (!nativeResult.Success)
                        return Result.Fail<TlMessageId, Error>(MapError(nativeResult.Error));
                    return Result.Ok<TlMessageId, Error>(new TlMessageId(nativeResult.MessageId));
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                return Result.Fail<TlMessageId, Error>(new Error("mtproto.bridge.exception", ex.Message));
            }
        }

        public event EventHandler<TlPayloadReceivedArgs> PayloadReceived
        {
            add { _native.PayloadReceived += MarshalPayload(value); }
            remove { _native.PayloadReceived -= MarshalPayload(value); }
        }

        private static EventHandler<NativePayloadEventArgs> MarshalPayload(EventHandler<TlPayloadReceivedArgs> managed)
        {
            return (s, e) => managed(s, new TlPayloadReceivedArgs(e.Buffer.ToArray(), e.MessageId));
        }

        public void Dispose() { _native.Dispose(); }
        private static Error MapError(NativeMtprotoError ne) => new Error("mtproto." + ne.Code, ne.Message);
    }
}
```

### `MediaStickerDecoderAdapter`

```csharp
namespace Vianigram.Composition.Adapters
{
    using NativeMedia = Vianigram.Media.MediaCodec;
    using Vianigram.Stickers.Ports.Outbound;

    public sealed class MediaStickerDecoderAdapter : IStickerDecoder
    {
        private readonly NativeMedia _media;

        public MediaStickerDecoderAdapter(NativeMedia media) { _media = media; }

        public async Task<Result<DecodedFrame, Error>> DecodeStaticAsync(byte[] webpBytes, CancellationToken ct)
        {
            var op = _media.DecodeWebPAsync(webpBytes.AsBuffer());
            using (ct.Register(() => op.Cancel()))
            {
                var nat = await op.AsTask().ConfigureAwait(false);
                if (!nat.Success) return Result.Fail<DecodedFrame, Error>(new Error("media.decode_webp", nat.Error));
                return Result.Ok<DecodedFrame, Error>(new DecodedFrame(nat.Pixels.ToArray(), nat.Width, nat.Height));
            }
        }

        public async Task<Result<TgsAnimationHandle, Error>> OpenAnimationAsync(byte[] tgsBytes, CancellationToken ct)
        {
            var op = _media.OpenTgsAsync(tgsBytes.AsBuffer());
            var nat = await op.AsTask().ConfigureAwait(false);
            if (!nat.Success) return Result.Fail<TgsAnimationHandle, Error>(new Error("media.open_tgs", nat.Error));
            return Result.Ok<TgsAnimationHandle, Error>(new TgsAnimationHandle(nat.Handle));
        }

        public async Task<Result<DecodedFrame, Error>> RenderFrameAsync(TgsAnimationHandle handle, double timeSec, CancellationToken ct)
        {
            var op = _media.RenderTgsFrameAsync(handle.Native, timeSec);
            var nat = await op.AsTask().ConfigureAwait(false);
            if (!nat.Success) return Result.Fail<DecodedFrame, Error>(new Error("media.render_tgs", nat.Error));
            return Result.Ok<DecodedFrame, Error>(new DecodedFrame(nat.Pixels.ToArray(), nat.Width, nat.Height));
        }

        public Task<Result<bool, Error>> CloseAnimationAsync(TgsAnimationHandle handle, CancellationToken ct)
        {
            _media.CloseTgs(handle.Native);
            return Task.FromResult(Result.Ok<bool, Error>(true));
        }
    }
}
```

---

## 3. `Vianigram.Shell` — layout

```
Core/Vianigram.Shell/
├── Vianigram.Shell.csproj            (references the WinMDs + Kernel)
├── Properties/AssemblyInfo.cs
│
├── Net/
│   ├── HttpClientFactory.cs
│   ├── HeadersComposer.cs
│   └── UserAgentBuilder.cs           ("Vianigram/1.0 (WP8.1; 512 MB device)")
│
├── Navigation/
│   ├── AppPageNavigationService.cs
│   ├── INavigationServicePort.cs
│   └── PageRouteRegistry.cs
│
├── Lifecycle/
│   ├── AppLifecycleHooks.cs
│   ├── HardwareBackButtonRouter.cs
│   ├── MemoryPressureMonitor.cs
│   └── DeepLinkRouter.cs             (parses vianigram://chat/{peerId}/{msgId})
│
└── Telemetry/
    ├── DiagnosticsAdapter.cs
    └── CrashReporter.cs              (App.UnhandledException handler + LocalFolder/crashes/)
```

### `AppPageNavigationService`

```csharp
namespace Vianigram.Shell.Navigation
{
    public sealed class AppPageNavigationService : INavigationServicePort
    {
        private readonly Frame _frame;
        private readonly PageRouteRegistry _routes;

        public AppPageNavigationService(Frame frame, PageRouteRegistry routes)
        { _frame = frame; _routes = routes; }

        public bool NavigateTo(string pageId, object parameter)
        {
            var t = _routes.Resolve(pageId);
            if (t == null) return false;
            return _frame.Navigate(t, parameter);
        }

        public bool GoBack()
        {
            if (!_frame.CanGoBack) return false;
            _frame.GoBack(); return true;
        }

        public bool CanGoBack { get { return _frame.CanGoBack; } }
    }
}
```

### `DeepLinkRouter`

```csharp
public sealed class DeepLinkRouter
{
    public DeepLinkAction Parse(string uri)
    {
        // vianigram://chat/{peerId} → open chat page
        // vianigram://chat/{peerId}/{messageId} → open chat scrolled to message
        // vianigram://settings/privacy → open privacy settings
        // tg://join?invite=abc → handle invite link
        // ...
    }
}
```

`App.OnLaunched` receives `LaunchActivatedEventArgs.Arguments` (the string of the toast's `launch=`) and passes it through `DeepLinkRouter.Parse` before choosing the initial page.

### `MemoryPressureMonitor`

Identical to the pattern of the `vianium-managed-kernel` sibling. Subscribes to `Windows.System.MemoryManager.AppMemoryUsageIncreased` and publishes `MemoryPressureChanged`. `Vianigram.Stickers` and `Vianigram.Media` listen to it to purge caches.

---

## 4. `Vianigram.Agent` — Background task

```
Clients/Vianigram.Agent/
├── Vianigram.Agent.csproj            (Background Task project, separate AppX component)
├── Properties/AssemblyInfo.cs
├── Package.appxmanifest fragments    (declared as Background Task in App's manifest)
│
├── PushBackgroundTask.cs             (entry point: implements IBackgroundTask)
├── AgentCompositionRoot.cs           (slim DI: only what's needed for push handling)
├── AgentLogger.cs                    (writes to LocalFolder/agent/agent.log)
└── DeadlineGuard.cs                  (kills work after 22 seconds to leave 3s for cleanup)
```

### Responsibilities

WP8.1 assigns `Vianigram.Agent` a hard budget:

- **CPU**: ~2 seconds.
- **Wall-clock**: ~25 seconds.
- **Memory**: ~16 MB on low-mem devices, ~75 MB on high-mem.

For that reason, the Agent does **NOT**:
- Do a full `getDifference`.
- Decode animated TGS.
- Load large blobs.
- Use the full composition (~50 ports).

For that reason the Agent **DOES**:
1. Receive the raw push (`RawNotification.Content`).
2. Parse it (Telegram sends JSON with sender + body + peerId + msgId).
3. Check whether the app is locked (Privacy passcode locked) → if so, register "1 new message" without content (privacy).
4. If the capability `notifications.preview_in_lockscreen` is OFF, similar: a title + a generic "New message" body.
5. Build a `ToastPayload` and deliver it to `IToastSink` directly (without going through TL to hydrate anything).
6. Update the badge count: read LocalSettings `notifications.badge.last_count`, +1, write, set the sink.
7. Finish.

### `PushBackgroundTask`

```csharp
namespace Vianigram.Agent
{
    public sealed class PushBackgroundTask : IBackgroundTask
    {
        public async void Run(IBackgroundTaskInstance taskInstance)
        {
            var deferral = taskInstance.GetDeferral();
            var deadline = new DeadlineGuard(TimeSpan.FromSeconds(22));
            try
            {
                var details = taskInstance.TriggerDetails as RawNotification;
                if (details == null) { deferral.Complete(); return; }

                using (var container = new AgentCompositionRoot().Build())
                using (var ct = deadline.LinkedToken())
                {
                    var notifications = container.Resolve<INotificationsApi>();
                    var parser = container.Resolve<PushPayloadParser>();
                    var parsed = parser.Parse(details.Content);
                    if (!parsed.IsOk) return;

                    await notifications.HandlePushPayloadAsync(parsed.Value, ct).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                AgentLogger.Error("PushBackgroundTask failed: " + ex);
            }
            finally { deferral.Complete(); }
        }
    }
}
```

`AgentCompositionRoot.Build()` registers **only** what is needed:
- Kernel (Clock, Logger, EventBus, Random).
- Storage (LocalSettingsAdapter — read shared with the App).
- `INotificationsApi` with direct WP8.1 sinks.
- `PushPayloadParser`.

It does NOT register: MTProto, Messaging, Stickers, Search, Voip, Media, Privacy (but it reads the `privacy.passcode_enabled` flag from LocalSettings to decide the toast's privacy mode).

### App ↔ Agent lifecycle

| Aspect | App | Agent |
|---|---|---|
| Memory | independent | independent |
| `LocalSettings` storage | read/write | read/write (may collide) |
| `LocalFolder` storage | read/write | read/write (a file lock is recommended) |
| EventBus | its own | its own (not shared in-process) |
| Composition | complete | minimal |
| Lifetime | foreground / suspended | activated by a trigger, max 25s |

Synchronization between App and Agent via:

1. **LocalSettings** — atomic per-key writes; consistent read between processes.
2. **A LocalFolder file with `FileIO.WriteTextAsync` + retry on `UnauthorizedAccessException`** — the file is locked if the other party opened it. Backoff 50ms × 5.
3. **A named Mutex** (`Mutex(initiallyOwned=false, "Local\\Vianigram.AuthState")`) when the Agent touches the auth blob to register the device — the App may try to refresh the same blob. WP8.1 supports a `Mutex` named scope `Local\\` (per-package).
4. **EventBus mirror via file**: when the Agent emits `BadgeUpdated`, it also writes `LocalFolder/agent/events/badge_{ticks}.txt`, which the App reads on the next foreground (`AppLifecycleObserver.OnResuming`). Idempotent: the agent writes last, the app reads and purges.

---

## 5. `App.xaml.cs` after the migration

```csharp
namespace Vianigram.App
{
    public sealed partial class App : Application
    {
        public ICompositionContainer Composition { get; private set; }
        private MemoryPressureMonitor _memoryMonitor;
        private AppLifecycleObserver _lifecycle;

        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            Resuming += OnResuming;
            UnhandledException += OnUnhandledException;
        }

        protected override async void OnLaunched(LaunchActivatedEventArgs e)
        {
            var rootFrame = Window.Current.Content as Frame;
            if (rootFrame == null)
            {
                rootFrame = new Frame();
                Window.Current.Content = rootFrame;

                var events = new InMemoryEventBus();
                var clock = new SystemClock();
                var logger = new CompositeLogger(new[] { (ILogger)new DebugLogger() });

                Composition = await new VianigramCompositionRoot().BuildAsync(events, logger, clock, CancellationToken.None);

                var routes = new PageRouteRegistry();
                routes.Register("Login", typeof(Pages.LoginPage));
                routes.Register("ChatList", typeof(Pages.ChatListPage));
                routes.Register("Chat", typeof(Pages.ChatPage));
                routes.Register("Search", typeof(Pages.SearchPage));
                routes.Register("Settings", typeof(Pages.SettingsPage));
                routes.Register("ProxySettings", typeof(Pages.ProxySettingsPage));
                routes.Register("Passcode", typeof(Pages.PasscodePage));
                routes.Register("Profile", typeof(Pages.ProfilePage));
                routes.Register("EditProfile", typeof(Pages.EditProfilePage));
                routes.Register("GroupInfo", typeof(Pages.GroupInfoPage));
                routes.Register("SecretChat", typeof(Pages.SecretChatPage));
                routes.Register("KeyFingerprint", typeof(Pages.KeyFingerprintPage));
                routes.Register("Call", typeof(Pages.CallPage));
                routes.Register("IncomingCall", typeof(Pages.IncomingCallPage));
                routes.Register("MediaViewer", typeof(Pages.MediaViewerPage));
                routes.Register("Forward", typeof(Pages.ForwardPage));
                routes.Register("NewChat", typeof(Pages.NewChatPage));
                routes.Register("NewChannel", typeof(Pages.NewChannelPage));
                routes.Register("Poll", typeof(Pages.PollPage));
                routes.Register("Scheduled", typeof(Pages.ScheduledPage));
                routes.Register("Topics", typeof(Pages.TopicsPage));
                routes.Register("AccountSwitcher", typeof(Pages.AccountSwitcherPage));
                routes.Register("ActiveSessions", typeof(Pages.ActiveSessionsPage));
                routes.Register("BlockedUsers", typeof(Pages.BlockedUsersPage));
                routes.Register("Contacts", typeof(Pages.ContactsPage));
                routes.Register("QrLogin", typeof(Pages.QrLoginPage));

                var navService = new AppPageNavigationService(rootFrame, routes);
                ((CompositionContainer)Composition).RegisterSingleton<INavigationServicePort>(_ => navService);

                _memoryMonitor = new MemoryPressureMonitor(
                    Composition.Resolve<IEventBus>(),
                    Composition.Resolve<IClock>());
                _memoryMonitor.Start();

                _lifecycle = new AppLifecycleObserver(Composition.Resolve<INotificationsApi>(),
                                                     Composition.Resolve<IPrivacyApi>(),
                                                     Composition.Resolve<IEventBus>(),
                                                     Composition.Resolve<ILogger>());
                await _lifecycle.OnLaunchedAsync(e.Arguments).ConfigureAwait(true);
            }

            if (rootFrame.Content == null)
            {
                var initial = await ResolveInitialPageAsync().ConfigureAwait(true);
                rootFrame.Navigate(initial.PageType, initial.Parameter);
            }

            Window.Current.Activate();
        }

        private async Task<InitialNav> ResolveInitialPageAsync()
        {
            var auth = Composition.Resolve<IAuthApi>();
            var privacy = Composition.Resolve<IPrivacyApi>();
            var nav = Composition.Resolve<INavigationServicePort>();

            var authStatus = await auth.GetStatusAsync(CancellationToken.None).ConfigureAwait(true);
            if (!authStatus.IsOk || !authStatus.Value.IsAuthorized)
                return new InitialNav(typeof(Pages.LoginPage), null);

            var lockStatus = await privacy.GetLockStatusAsync(CancellationToken.None).ConfigureAwait(true);
            if (lockStatus.IsOk && lockStatus.Value.IsLocked)
                return new InitialNav(typeof(Pages.PasscodePage), "afterUnlock=ChatList");

            return new InitialNav(typeof(Pages.ChatListPage), null);
        }

        private async void OnSuspending(object sender, SuspendingEventArgs e)
        {
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                _memoryMonitor?.Stop();
                Composition.Resolve<IEventBus>()
                    .Publish(new AppSuspending(Composition.Resolve<IClock>().UtcNow));

                // Privacy: arm auto-lock
                await Composition.Resolve<IPrivacyApi>().ArmAutoLockAsync(CancellationToken.None).ConfigureAwait(false);

                // Settings: snapshot any pending changes
                await Composition.Resolve<ISettingsApi>().SnapshotAsync(CancellationToken.None).ConfigureAwait(false);

                // Sync: persist current state
                await Composition.Resolve<ISyncApi>().PersistAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally { deferral.Complete(); }
        }

        private async void OnResuming(object sender, object e)
        {
            // Privacy: tick auto-lock; if elapsed > interval, lock
            await Composition.Resolve<IPrivacyApi>().TickAutoLockAsync(CancellationToken.None).ConfigureAwait(false);

            // Sync: getDifference to fetch missed updates while suspended
            await Composition.Resolve<ISyncApi>().GetDifferenceAsync(CancellationToken.None).ConfigureAwait(false);

            // Drain Agent → App event mirror
            await _lifecycle.OnResumedAsync().ConfigureAwait(false);

            _memoryMonitor?.Start();
        }

        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try { Composition.Resolve<ILogger>().Error("Unhandled: " + e.Exception); } catch { }
        }
    }
}
```

---

## 6. Cross-cutting — Object lifetimes

| Type | Lifetime | Reason |
|---|---|---|
| `IClock`, `ILogger`, `ITelemetry`, `IEventBus`, `ICapabilityRegistry`, `IRandom` | Singleton | Shared by everything |
| `IHttpClientPort`, `ITlsPort` | Singleton | Reuse of the native connection pool |
| `IMtprotoSession` | Singleton (per-account scope in the future) | Maintains AuthKey + sequence numbers |
| `ITlCodec` | Singleton | Stateless |
| `IAuthApi`, `IMessagingApi`, `IDialogsApi`, `IStickersApi`, `INotificationsApi`, `ISettingsApi`, `ISearchApi`, `IPrivacyApi`, `IContactsApi`, `IVoipApi`, `ISecretChatsApi`, `IMediaApi` | Singleton | Aggregate roots, shared |
| `INavigationServicePort` | Singleton | Bound to the single `Frame` |
| `LockState` (Privacy runtime) | Singleton | Process-wide lock state |
| Page-bound ViewModels | Transient | A new one per Page activation |
| Item-bound ViewModels (`MessageBubbleViewModel`, `ChatListItemViewModel`) | Transient | Created by the parent VM, disposed with the parent |

### Threading

- The Kernel `IEventBus` (`InMemoryEventBus`) is thread-safe (an internal lock).
- Domain aggregates are **not** thread-safe; single-thread access is assumed. If two contexts modify the same aggregate concurrently, the responsibility is the Application use case's (lock or serialize).
- Composition resolve is thread-safe (a lock).
- TL gateway calls are thread-safe (the MTProto session handles an internal queue).

### Disposal

- App suspending → publishes an `AppSuspending` event.
- Each context subscribes and persists a snapshot.
- Composition `Dispose` frees all the singletons that are `IDisposable`.
- Native bridges (`MtprotoBridge`, `MediaBridge`) have a Dispose that closes the native handles.

---

## 7. Phases

### Phase 1 — Composition skeleton + Kernel (week 1)

`CompositionContainer`, `VianigramCompositionRoot` with empty modules, `KernelModule` registers Clock/Logger/EventBus.

### Phase 2 — Storage + Net adapters (week 2)

`LocalSettingsAdapter`, `LocalFolderObjectStore<T>`, `LocalFolderSecretStore`, `HttpNativeAdapter`, `NetNativeAdapter`. Tests with the emulator.

### Phase 3 — MTProto bridge + Auth module (weeks 3–4)

`MtprotoBridge`, `TlCodecBridge`, `CryptoNativeAdapter`. `AuthModule` wires it up.

### Phase 4 — Settings + Privacy modules (week 5)

`SettingsModule`, `PrivacyModule`. `DataProtectedPasscodeStore`. Lock screen integration.

### Phase 5 — Messaging + Dialogs + Contacts (weeks 6–8)

`MessagingModule`, `DialogsModule`, `ContactsModule`. TL adapters.

### Phase 6 — Stickers + Notifications + Search (weeks 9–10)

`StickersModule`, `NotificationsModule`, `SearchModule`.

### Phase 7 — Media + Voip + SecretChats (weeks 11–13)

`MediaModule`, `VoipModule`, `SecretChatsModule`.

### Phase 8 — Presentation module (week 14)

`PresentationModule` registers all the VMs as transient.

### Phase 9 — Vianigram.Agent (week 15)

`AgentCompositionRoot`, `PushBackgroundTask`, registration in Package.appxmanifest, mutex sync with the App.

### Phase 10 — Lifecycle integration + telemetry (week 16)

`AppLifecycleObserver` orchestrates suspend/resume + auto-lock + getDifference. `CrashReporter` captures `UnhandledException` to `LocalFolder/crashes/`.

---

## 8. Open questions

1. **How to share auth state between the App and the Agent**: options evaluated:
   - **`LocalSettings`**: simple, atomic per-key, but limited to 8 KB per value. The auth blob (encrypted) can exceed it.
   - **A Mutex-protected file in `LocalFolder`**: a `Local\\Vianigram.AuthBlob` mutex. Robust but requires discipline on every read/write. Preliminary decision: this path. The `auth.bin` blob is ~3 KB encrypted, lives in `LocalFolder/accounts/{id}/auth.bin`.
   - **An App service**: WP8.1 does NOT support App Services (only Win10). Discarded.
2. **How the Agent decides whether the app is unlocked**: it reads `LocalSettings["privacy.passcode_enabled"]` and `LocalFolder/privacy/last_unlock_marker.bin` (touched on unlock, deleted on lock by the App). If the marker does not exist → assume locked → a generic toast.
3. **MTProto session reconnect in the background**: when a push arrives, should the Agent open a TL socket? **No** — V1 only trusts what the push payload brings. If the payload is opaque (Telegram sometimes sends only a `loc_key`), the Agent shows a generic "New message" and lets the App do a getDifference at the next foreground.
4. **Multi-account in the Agent**: if the user has 3 accounts, which one sends the push? Telegram registers a device per session, so each account has its channel URI. Resolution: each `account.registerDevice` is done with an app-specific token derived from the WNS URI + accountId; on receiving the push, the parser extracts the accountId from the payload and saves the toast with an account tag. If the capability `notifications.show_account_label` is on, the toast shows "[Personal] John: hi".
5. **`Vianium.Core.Http` reuse**: the `vianium-http` sibling is shared between Vianigram and other Vianium products. Is the connection pool shared? **No** — each app is a separate AppX, they do not share a process. The WinMD provides shared types, but each app creates its own `HttpClient` instance.
6. **Crash reporter privacy**: the dump may include stack traces that leak a path with the username. Sanitize before saving to `crashes/{ticks}.json`.
7. **WinRT type projection**: `ICompositionContainer.Resolve<T>()` requires reflection over the type T. WP8.1 supports `Type.GetType` but we want zero reflection. Solution: the container uses a `Dictionary<Type, ...>` keyed by `typeof(T)`, which is a compile-time constant. There is no real reflection.
8. **Agent + Privacy passcode interaction**: if the app is locked and a push arrives, the Agent **CANNOT** decrypt the content (it does not have the masterKey). It can only emit a toast with "X new message" without a name or body. Documented in [10-notifications.md §HandlePushPayloadUseCase](10-notifications.md).

---

## 9. Anti-patterns in Composition (avoid)

1. **Reflection-based DI** (Autofac/Unity) — WP8.1 overhead + warmup time.
2. **Service locator** — always constructor injection.
3. **Lambda captures of the container in singleton factories** — only necessary here because the DI is manual; document it.
4. **Lifetime mismatch** — a VM (transient) capturing a context API (singleton) is OK; the opposite is not.
5. **A god adapter** — a single `MegaAdapter` that implements 10 ports. Split by target context.
6. **The composition root explodes** with 30+ contexts — split into modules per feature (`StickersModule`, etc.).
7. **Circular dependencies** between contexts — diagnose with a CI script (parsing project references); if A depends on B and B on A, move the common part to the Kernel or an adapter in Composition.

## 10. Risks

1. **Boot time**: 50+ singletons constructed at startup → P95 cold boot on a 512 MB device can exceed 3 seconds. Mitigation: lazy factories (a Singleton evaluates the factory only on the first Resolve), and `LoadInitialAsync` async for the heavy modules (Auth, MTProto).
2. **A native bridge crash** kills the entire process (managed code cannot catch a SEHException directly in WP8.1 release). Mitigation: native code exposes `Result<T, NativeError>` instead of throwing.
3. **Memory pressure mid-call**: WP8.1 can kill the app if memory > limit. Mitigation: `MemoryPressureMonitor` publishes events; contexts purge caches reactively. If `Overlimit` arrives, Stickers closes all the animations and purges 80% of the LRU.
4. **The composition root reads a deprecated API**: WP8.1 `IsolatedStorageSettings` is deprecated in favor of `LocalSettings`. The adapter uses LocalSettings. If we find references to IsolatedStorage in Pivora, migrate them to the adapter.
5. **Background task quota exceeded**: the Agent exceeds 25s wall-clock → the OS kills it; the toast is not shown. Mitigation: `DeadlineGuard` cancels work at 22s and emits a generic fallback toast.
6. **Push channel revoked**: the WNS URI changed silently. Mitigation: detect on the first failed push (the server returns 410); refresh + re-register.

---

## 11. Crosslinks

- [principles.md](principles.md) — DDD+hex, the 6 rules (especially rule 3: anti-corruption).
- [00-overview.md](00-overview.md) — the global vision, project layout.
- [09-stickers.md](09-stickers.md) — `IStickerTlGateway`, `IStickerDecoder` examples.
- [10-notifications.md](10-notifications.md) — `IPushChannelSource`, integration with the Agent.
- [11-settings.md](11-settings.md) — the `ISettingsStorage` adapter.
- [12-search.md](12-search.md) — `ISearchTlGateway`.
- [13-privacy.md](13-privacy.md) — `IPasscodeMaterialStore`, `IAppFocusEvents`, master key wrap.
- [14-presentation.md](14-presentation.md) — VMs resolve from Composition.
- `..\vianium-managed-kernel\docs\managed-architecture\11-shell-and-host.md` — the sibling reference from the managed kernel.
