# Managed Architecture Principles — Vianigram (C# / WP8.1)

## Why this document

Vianigram's C# layer runs the **messenger product** (account, chats, messages, contacts, media orchestration, sync, calls, secret chats, stickers, notifications, settings, search, privacy, presentation), while the native data plane (MTProto, TL, crypto, media decode, voip) lives in the `vianium-mtproto`, `vianium-crypto`, `vianium-voip` siblings and in the local context `Vianigram.Core.Media`. The native transport plane (C++ Kernel, TLS 1.3, HTTP/1.1+H2, Net policies) is referenced from the `vianium-kernel`, `vianium-tls`, `vianium-http`, `vianium-net` siblings.

Without a shared structure between both sides, we end up with two architectures that do not talk to each other: native a tidy DDD+hex, managed with god-objects (like `TelegramClient` with 14 fields) and singletons (`DatabaseManager.Instance` with `.Result` blocks). This document aligns Vianigram's C# layer to the **same model** as [native-port/architecture-principles.md](../native-port/architecture-principles.md) and the C# mirror of the `vianium-managed-kernel\docs\managed-architecture\principles.md` sibling, adapted to the particularities of the managed WP8.1 runtime and the specific invariants of a Telegram client.

It is the **architectural source of truth** for Vianigram's C# layer. Any managed feature, refactor, or PR must respect it. If a rule needs to change, this doc is updated first.

---

## Model: DDD + Hexagonal per bounded context (C# version)

Each managed component (`Account`, `Chats`, `Messages`, `Contacts`, `Media`, `Sync`, `Calls`, `SecretChats`, `Stickers`, `Notifications`, `Settings`, `Search`, `Privacy`, `Storage`, etc.) is a **bounded context** with the same internal structure as the native ones:

- Its own ubiquitous language (a `Message` in `Messages` is not the same thing as a `Message` in `SecretChats`).
- Aggregates with invariants (a `Dialog` does not allow a negative unread count, a `MessageStream` does not allow editing a message without a server-id).
- Domain events emitted (`MessageReceived`, `DialogPinned`, `CallStateChanged`, `SecretSessionRekeyed`).
- Inbound ports (what the context offers, e.g. `IChatsApi`) and outbound ports (what it requires, e.g. `IMtProtoChannel`, `IDialogStore`).
- Concrete adapters (storage, MTProto, push, etc.).
- A public API (interfaces consumed by presentation and by other contexts via ACL).

### Key differences vs the native C++ model

| Aspect | Native (C++/CX) | Managed (C# WP8.1) |
|---|---|---|
| Public ABI | WinRT `ref class sealed` versioned in `vN/` | C# `interface` versioned via the namespace `V1`, `V2` |
| Cross-context | Anti-corruption headers; one `.winmd` per context | Anti-corruption interfaces; one assembly per context |
| Memory | Arena/pool allocators, POD value types | Managed GC; use `struct` for value objects where it pays off |
| Cross-cutting events | `IEventBus` (C++ Kernel) | `IEventBus` (C# Kernel) — the same semantics |
| Errors | `Result<T, Error>`, no exceptions | `Result<T, Error>` or `Maybe<T>`; exceptions only at the WinRT boundary |
| Async | `Windows::System::Threading::ThreadPool` | native `async`/`await`, `Task<T>` |
| Storage | outbound `IStorage` port | `IObjectStore<T>` / `ISqliteRepository<T>` port; impls use `Windows.Storage` + SQLite |
| Telemetry | `ITelemetry` port (C++ Kernel) | `ITelemetry` port (C# Kernel) — fan-out to the host |
| Crypto | native (siblings `vianium-crypto`, `vianium-tls`) | **prohibited from touching key-material bytes**; opaque handles only |

---

## Bounded contexts of the messenger product (C# layer)

| Context | Aggregate root | Key events | Critical asset |
|---|---|---|---|
| **Account** | `AccountIdentity` | `SignedIn`, `SignedOut`, `TwoFactorEnrolled`, `SessionRevoked` | phone+SMS+SRP-2048+QR / multi-account |
| **Chats** | `Dialog` | `DialogOpened`, `DialogPinned`, `DialogArchived`, `DialogMuted` | dialog list ordering + folder tags |
| **Messages** | `MessageStream` | `MessageSent`, `MessageReceived`, `MessageEdited`, `MessageDeleted`, `ReactionsChanged` | history + edits + reactions |
| **Contacts** | `ContactBook` | `ContactImported`, `ContactBlocked`, `ContactUnblocked` | sync + blocked list |
| **Media** | `MediaTransfer` | `UploadStarted`, `ChunkProgressed`, `UploadCompleted`, `DownloadStarted`, `DownloadCompleted` | up/down orchestration + parallel chunks |
| **Sync** | `SyncCursor` | `UpdatesApplied`, `GapDetected`, `ConflictResolved` | pts/qts/seq/date + per-channel pts |
| **Calls** | `CallSession` | `CallRequested`, `CallAccepted`, `CallEnded` | voice/video signaling state machine |
| **SecretChats** | `SecretSession` | `SecretSessionEstablished`, `SecretSessionRekeyed`, `KeyFingerprintChanged` | DH state, key fingerprints, layer negotiation |
| **Stickers** | `StickerLibrary` | `StickerPackInstalled`, `StickerUsed` | packs + animated cache |
| **Notifications** | `NotificationProfile` | `ToastShown`, `MuteToggled`, `BadgeUpdated` | toast/tile/badge + per-chat mute |
| **Settings** | `UserPreferences` | `PreferenceChanged` | immutable snapshot + diff |
| **Search** | `SearchSession` | `QueryExecuted`, `ResultsArrived` | global + per-chat |
| **Privacy** | `PrivacyProfile` | `PasscodeArmed`, `LastSeenScopeChanged`, `BlockedListUpdated` | passcode, last-seen, blocked, active sessions |
| **Storage** | (infra) | (no events; internal) | SQLite repos + at-rest encryption |
| **Presentation** | (a view per context, not a single aggregate) | UI events | XAML pages + ViewModels |

Contexts thought-out for the future (not developed now): **CloudPasswords**, **PaymentRequest**, **StoriesFeed**, **Bots-MiniApps**.

---

## Internal structure of each bounded context (C#)

All the C# bounded-context projects follow **the same hexagonal skeleton**:

```
Vianigram.<Context>/
├── Vianigram.<Context>.csproj          (WP8.1, NETFX_CORE, WINDOWS_PHONE_APP)
├── Properties/
│   └── AssemblyInfo.cs
│
├── Domain/                              ← pure core, no external dependencies
│   ├── Entities/                        ← objects with identity
│   ├── ValueObjects/                    ← immutable, without identity (a struct when it pays off)
│   ├── Aggregates/                      ← roots that enforce invariants
│   ├── Events/                          ← domain events emitted
│   ├── Services/                        ← stateless domain logic
│   └── Policies/                        ← business rules
├── Application/                         ← orchestrates the domain, without knowing about infra
│   ├── UseCases/                        ← one case = one public operation
│   ├── Commands/                        ← input DTOs for handlers
│   ├── Queries/                         ← query DTOs
│   └── Handlers/                        ← processes commands/queries
├── Ports/                               ← interfaces (abstract, no impl)
│   ├── Inbound/                         ← what the context offers
│   └── Outbound/                        ← what the context requires
├── Infrastructure/                      ← concrete adapters (implement the ports)
│   ├── Persistence/                     ← storage adapters
│   ├── Native/                          ← bridge to Vianigram.Core.* and Vianium.Core.* WinMDs
│   ├── Logging/
│   └── ...
└── Api/                                 ← versioned public surface
    ├── V1/                              ← namespace Vianigram.<Context>.Api.V1
    └── V2/                              ← future versions
```

### Dependency rules (enforced by code review + checked references)

```
Api/         →  Application/, Ports/Inbound/    (NO direct Domain, NO Infrastructure)
Application/ →  Domain/, Ports/                 (NO direct Infrastructure)
Domain/      →  its own Domain/ + Vianigram.Kernel  (NO Application, NO Ports, NO Infra, NO other bounded contexts)
Infrastructure/ → Ports/Outbound/, Domain/, Kernel  (implements ports)
```

`Domain/` is **pure**: only the BCL + `Vianigram.Kernel`. **No** `using Windows.Storage`, **no** `using System.IO`, **no** `using Vianigram.Core.Media`, **no** `using Vianium.*` (siblings). Compiles without the XAML runtime. Unit-testable without the emulator.

---

## The managed Kernel — `Vianigram.Kernel`

A C# WP8.1 library project that provides cross-cutting abstractions. It is consumed by **all** the other bounded contexts and the Presentation layer. It is a structural clone of the C# managed kernel that lives in the `vianium-managed-kernel\` sibling (`Vianigram.Browser.Kernel`), with content rewritten to have no browser-specifics.

```
Core/Vianigram.Kernel/
├── Vianigram.Kernel.csproj             (WP8.1, NETFX_CORE)
├── Properties/AssemblyInfo.cs
│
├── Result/
│   ├── Result.cs                       (Result<T, TError> — success/error without exceptions)
│   ├── Maybe.cs                        (Maybe<T> — Optional)
│   ├── Error.cs                        (code + message + cause chain)
│   └── ResultExtensions.cs             (Map, Bind, Tap, Match)
├── Time/
│   ├── IClock.cs                       (port — UtcNow(), MonotonicMs())
│   └── SystemClock.cs                  (impl)
├── Logging/
│   ├── ILogger.cs                      (port — Trace/Debug/Info/Warn/Error)
│   ├── LogLevel.cs
│   ├── DebugLogger.cs                  (impl Debug.WriteLine + System.Diagnostics)
│   ├── CompositeLogger.cs              (fan-out)
│   └── NullLogger.cs                   (no-op default)
├── Events/
│   ├── IEventBus.cs                    (port — Publish<T>, Subscribe<T>, Unsubscribe)
│   ├── IDomainEvent.cs                 (marker; TimestampUtc + Kind)
│   ├── DomainEventBase.cs              (abstract base with Timestamp)
│   ├── InMemoryEventBus.cs             (synchronous impl, thread-safe)
│   ├── AsyncEventBus.cs                (impl with queue + async dispatcher)
│   ├── IEventHandler.cs                (typed handler interface)
│   └── SubscriptionToken.cs            (IDisposable for auto-unsubscribe)
├── Capability/
│   ├── CapabilityId.cs                 (struct with a namespaced string)
│   ├── ICapabilityProvider.cs
│   ├── ICapabilityRegistry.cs          (port — IsEnabled, Register, Resolve)
│   ├── CapabilityRegistry.cs           (impl)
│   └── FeatureFlag.cs                  (dynamic toggle)
├── Telemetry/
│   ├── ITelemetry.cs                   (port — Counter, Histogram, Gauge, Event)
│   ├── NullTelemetry.cs                (no-op default)
│   └── CompositeTelemetry.cs
├── Concurrency/
│   ├── ICancellationGate.cs
│   ├── IAsyncCommand.cs
│   ├── ITaskScheduler.cs
│   └── ThreadPoolScheduler.cs
├── Persistence/
│   ├── IObjectStore.cs                 (port — typed store with T : class)
│   ├── ISettingsStore.cs               (port — key/value)
│   ├── ISecretStore.cs                 (port — encrypted key/value via DataProtectionProvider)
│   └── ISqliteRepository.cs            (port — relational repo, T aggregate)
├── Identity/
│   ├── EntityId.cs                     (struct — Guid wrapper typed per context)
│   ├── ContextStringId.cs              (intern table)
│   └── ICorrelationContext.cs          (per-request correlation id for logs)
├── Validation/
│   ├── IValidator.cs
│   ├── ValidationError.cs
│   └── ValidationResult.cs
├── Lifecycle/
│   ├── IStartable.cs
│   ├── IDisposableAsync.cs
│   └── IComponentInitializer.cs
└── Interop/
    ├── HResultMapper.cs                (HRESULT → Error)
    ├── WinRtBridge.cs
    └── NativeCallbackAdapter.cs
```

### Rules of the managed Kernel

1. **No mutable global state.** Everything that carries state is injected as a port.
2. **No allocations in the Kernel's own hot paths.** `Result`/`Maybe` are `struct`.
3. **No exceptions crossing boundaries.** `Result<T, Error>` always. Exceptions are only allowed:
   - Crossing the WinRT boundary (then catch and map to a Result).
   - In `Infrastructure/` when it interacts with APIs that already throw exceptions (System.IO, Windows.Storage, SQLite-net), always wrapped in try/catch + mapping to a Result.
   - In explicit `Throw`s for an **invariant violation** (a bug, not an operational condition).
4. **100% testable without infrastructure** — the Kernel itself should not need `Windows.Storage`.
5. **`async Task` not `async void`** except for UI event handlers.
6. **`ConfigureAwait(false)`** on all the Kernel's and `Application/`'s awaits. In `Domain/` there should be no awaits — the domain is synchronous.

---

## The 6 non-negotiable rules (shared with the `vianium-*` siblings)

### 1. Domain events bus from day 1

Each relevant state mutation emits an `IDomainEvent` to the injected `IEventBus`. Future subsystems subscribe without touching the emitter.

**C# example:**
```csharp
public sealed class MessageStream  // aggregate root of the Messages context
{
    private readonly IEventBus _events;
    private readonly IClock _clock;
    private readonly Dictionary<MessageId, MessageState> _byId = new();

    public Result<MessageId, Error> Send(PeerId peer, MessageBody body)
    {
        var localId = MessageId.NewLocal();
        _byId[localId] = MessageState.Sending(peer, body, _clock.UtcNow);
        _events.Publish(new MessageSent(localId, peer, body, sending: true, _clock));
        return Result.Ok<MessageId, Error>(localId);
    }
}
```

The day you add:
- **Notifications** → subscribes to `MessageReceived` and fires a toast.
- **Search** → subscribes and indexes the body.
- **Telemetry** → counts msg/min received.
- **Sync** → subscribes to `MessageEdited` and reflects it to the server when the origin is local.

Without the event bus, each new subsystem requires modifying `MessageStream`. With the bus, no.

**Rule:** no observable change of an aggregate occurs without emitting its event. Events are immutable (`record`-like via `sealed class` with read-only props in C# WP8.1, without the `record` keyword).

### 2. Capability model — two distinct layers (OCAP + feature flags)

Vianigram **explicitly** distinguishes two concepts that in most messengers are conflated:

#### Layer A — Feature flag (`ICapabilityRegistry`)

**What it is:** a binary build-/install-time toggle. "Is feature X enabled in this build?"

**Examples:** "does it support secret chats?" "does it support video calls?" "does it support animated stickers?" "does it support stories?" "does it support scheduled messages?"

**API:** `bool registry.IsEnabled(CapabilityId)` — global per process.

**What it is for:** A/B testing, build flavors (Lite/Full/Dev), gradual versioning of TL layer N+1 features over layer N.

**Standard capabilities defined in the Kernel:**
```csharp
public static class Capabilities
{
    public static readonly CapabilityId SecretChats = new("messenger.secret_chats");
    public static readonly CapabilityId VoiceCalls = new("messenger.calls.voice");
    public static readonly CapabilityId VideoCalls = new("messenger.calls.video");
    public static readonly CapabilityId AnimatedStickers = new("messenger.stickers.animated");
    public static readonly CapabilityId Reactions = new("messenger.reactions");
    public static readonly CapabilityId ScheduledMessages = new("messenger.scheduled");
    public static readonly CapabilityId Folders = new("messenger.folders");
    public static readonly CapabilityId Stories = new("messenger.stories");                 // future
    public static readonly CapabilityId Payments = new("messenger.payments");                // future
}
```

**Rule:** large new features (>500 LOC or a new bounded context) always come with their `CapabilityId`.

#### Layer B — Object Capability (OCAP) — `IObjectCapabilityProvider` per consumer

**What it is:** a handle (a typed C# reference or an opaque `EntityId`) that **literally represents the right** to invoke an operation. What is not handed over cannot be used — not because there is a check, but because the interface does not exist in the consumer's scope.

**Inspired by:** Pony, Genode OS, KataOS, the Capability-based security model (Mark Miller, Norm Hardy).

**Difference from feature flags:**

| | Feature flag | OCAP |
|---|---|---|
| Granularity | Global per process | Per consumer (per-account, per-secret-session, per-tab UI, per-bot) |
| API | `IsEnabled("foo")` | `provider.TryGet<IFoo>()` returns `Maybe<IFoo>` |
| Bypass | `if (false) { DoFoo(); }` still allows `DoFoo` to exist | `IFoo` is not available if it was not injected |
| Audit | Runtime check | Impossible to call what you do not have |
| Revocation | Reassign the flag | Drop the handle (an IDisposable token) |

**Real use cases in Vianigram:**
- **Multi-account isolation** — each `AccountIdentity` receives a Provider with its own `IMtProtoChannel`, its own `IDialogStore`, its own `IMediaCache`. Account A never sees Account B's data.
- **Secret chat sandboxing** — the `SecretSession` receives its own `ISecretCryptoApi` with the auth key derived from that session's specific DH. It has no access to other secret sessions or to the cloud account's auth key.
- **Bot mini-app boundaries** (future) — a mini-app receives a Provider without `IContactBook`, without `ICredentialVault`, without a direct `IMtProtoChannel`.
- **Test harnesses** — the smoke tests receive a Provider with `IMtProtoChannel` pointing at the test DC, with no access to the production channel.

**Rule:**
- For sub-features and A/B testing → use `ICapabilityRegistry` (Layer A).
- For per-consumer isolation (account/secret-session/bot/test) → use `IObjectCapabilityProvider` (Layer B).
- The two layers are combined: a feature flag globally enabled + an OCAP handed over per-consumer.

### 3. Anti-corruption layer between bounded contexts

A context **never** references another context's assembly. If `Notifications` needs info from `Messages`, it receives **projected DTOs** or an **inverse port**.

```csharp
// WRONG — Notifications depends on Messages
using Vianigram.Messages;
public sealed class NotificationProfile {
    public bool ShouldNotify(Message m) => m.Body.Length > 0;  // ❌
}

// RIGHT — Notifications defines what it needs
namespace Vianigram.Notifications.Ports.Outbound
{
    public interface IIncomingMessageProbe
    {
        bool HasContent(MessageId id);
        PeerId GetPeer(MessageId id);
    }
}
```

An `IncomingMessageProbeAdapter` adapter (in `Vianigram.Composition`) implements `IIncomingMessageProbe` wrapping `MessageStream`. `Notifications` is unaware of what Messages is.

**Rule:** zero `using Vianigram.<OtherContext>` outside of `Vianigram.Composition`.

### 4. API surface versioning via namespace

Each bounded context exposes public interfaces in `Api/V1/`, `Api/V2/`. v2 may add/change; v1 never breaks.

```csharp
namespace Vianigram.Account.Api.V1
{
    public interface IAccountApi
    {
        Task<Result<SignInTicket, Error>> SendCodeAsync(PhoneNumber phone, CancellationToken ct);
        Task<Result<AccountIdentitySnapshot, Error>> SignInAsync(SignInTicket ticket, SmsCode code, CancellationToken ct);
        Task<Result, Error> SignOutAsync(CancellationToken ct);
        IReadOnlyList<ActiveSessionSnapshot> ListSessions();
    }

    public sealed class AccountIdentitySnapshot { /* immutable DTO */ }
}

namespace Vianigram.Account.Api.V2
{
    // breaking changes here, v1 stays intact
    public interface IAccountApi
    {
        Task<Result<SignInTicket, Error>> SendCodeAsync(SignInRequest req, CancellationToken ct);
    }
}
```

**Rule:** a breaking change = a new `Vn/`. Never modify a `Vn/` already shipped in a public release (TL layer changes in Telegram are the canonical example — `messages.sendMessage` in layer 100 is not the same as in 214).

### 5. Process isolation seam (zero shared mutable state aspirational)

Even though today WP8.1 runs everything in the same process (the foreground app + background agent share DLLs but not instances), design as if the bounded contexts were already in separate processes.

- Pass **serializable DTOs** (POCOs with primitives), not mutable references.
- Idempotent operations wherever possible (sync via pts/qts is naturally idempotent).
- No mutable singleton crossing contexts.
- Opaque identities (`MessageId`, `DialogId`, `PeerId` — value types with a namespace).

`Vianigram.Agent` (the background task) and `Vianigram.App` (the foreground) are already a real case of "two processes" — they share storage but not in-memory state. The rule anticipates that tomorrow we could split `Vianigram.Calls` or `Vianigram.Sync` into their own process.

**Rule:** the API between contexts passes value types or opaque handles, not shared mutable classes.

### 6. Telemetry as a port, not a direct dependency

No component calls `Debug.WriteLine` directly. It calls an injected `ILogger`/`ITelemetry`.

```csharp
public sealed class SendMessageHandler
{
    private readonly ITelemetry _telemetry;
    private readonly ILogger _log;
    private readonly IClock _clock;

    public async Task<Result<MessageId, Error>> HandleAsync(SendMessageCommand cmd, CancellationToken ct)
    {
        var t0 = _clock.MonotonicMs();
        var result = await _stream.SendAsync(cmd, ct).ConfigureAwait(false);
        _telemetry.Histogram("messages.send.duration_ms", _clock.MonotonicMs() - t0);
        _telemetry.Counter("messages.send.count");
        if (!result.IsOk)
            _telemetry.Counter("messages.send.error", 1, ("code", result.Error.Code));
        return result;
    }
}
```

**Mandatory naming convention:** `<context>.<operation>.<unit>`. Examples:
- `account.signin.duration_ms`
- `chats.list.size` (gauge)
- `messages.send.duration_ms`
- `messages.received.count`
- `media.upload.chunk_kbps` (gauge)
- `sync.gap.count`
- `mtproto.flood_wait.seconds` (histogram)
- `calls.setup.duration_ms`
- `secret.rekey.count`

Adapters route to Debug, a file in `LocalFolder`, a callback to a XAML overlay devtools, or the cloud when appropriate.

**Rule:** zero direct I/O side-effects in `Domain/`. Everything via ports.

---

## The 9 enforced rules (shared with the `vianium-*` siblings)

1. **`Result<T, Error>` crossing boundaries** — no exceptions between Domain/Application/Ports. Exceptions only in Infrastructure when interacting with APIs that already throw them, and always wrapped + mapped.
2. **`async`/`await` always, never `.Result`/`.Wait()`/`async void`** — the only exception: XAML UI event handlers (`Click_Handler(object s, RoutedEventArgs e)`).
3. **`ConfigureAwait(false)` in the Kernel and Application** — only Presentation may capture the SynchronizationContext.
4. **No mutable singletons** — zero `public static Foo Instance { get; }` with state. Injection via the composition root.
5. **No business logic in ViewModels** — the VMs are the inbound UI adapter; they translate UI → Commands. The logic lives in `Application/UseCases`.
6. **No generic `catch (Exception)` without Result mapping** — each catch must (a) log structured, (b) return a Result with a typed code. Never silently swallow.
7. **The storage format is the adapter's decision** — the contract is `IObjectStore<T>` or `ISqliteRepository<T>`. JSON/SQLite/binary are private details of `Infrastructure/Persistence/`.
8. **Encryption with platform crypto only** — `Windows.Security.Cryptography.DataProtection.DataProtectionProvider` for at-rest. Never custom XOR, never `System.Security.Cryptography.Aes`. For MTProto/secret-chats the crypto lives in the `vianium-crypto` sibling (C++).
9. **`Result<T, TError>` is a `struct`** — a value type with `_value`, `_error`, `_isOk` fields. Does not box by default. No allocations in hot paths.

---

## CQRS-lite in Application/

Each bounded context exposes its API as **commands** (change state) and **queries** (read state).

```csharp
// command (immutable input DTO)
public sealed class SendMessageCommand
{
    public SendMessageCommand(PeerId peer, MessageBody body, MessageId? replyTo = null)
    {
        Peer = peer;
        Body = body;
        ReplyTo = replyTo;
    }
    public PeerId Peer { get; }
    public MessageBody Body { get; }
    public MessageId? ReplyTo { get; }
}

public sealed class SendMessageResult
{
    public MessageId LocalId { get; }
    public MessageId? ServerId { get; }       // null while in the "sending" state
    public DateTime AcceptedUtc { get; }
}

public interface ISendMessageHandler
{
    Task<Result<SendMessageResult, Error>> HandleAsync(SendMessageCommand cmd, CancellationToken ct);
}
```

Benefits:
- Trivial audit trail (log all commands for incident response).
- Replay/undo in devtools (re-execute commands).
- Future split (reads to a dedicated read model, e.g. a SQLite read replica for search).
- Tests over handlers, not over complex orchestration.

---

## The 6 messenger-specific extensions (unique to Vianigram)

These rules **do not apply** to a browser and are specific to a Telegram client. Any PR that violates them blocks a merge.

### M1. Optimistic UI mandatory for sends

**Rule:** a message (text, media, voice) renders in the UI in the **"sending"** state within 50ms of the user's tap. The reconciliation with the server `messages.sendMessage` occurs asynchronously; a failure rolls back to the **"failed to send"** state with a retry affordance.

**Why it matters:** Telegram in other clients (official, Premium) is perceived as instant precisely because the UI does not wait for the ACK. A client that shows a spinner during an MTProto round-trip feels broken, even though it is technically more "honest".

**How to enforce:**
- `MessageStream.Send(...)` returns `MessageId.NewLocal()` synchronously.
- It emits `MessageSent { sending: true }` before awaiting anything.
- The `IMtProtoChannel.InvokeAsync(messages.SendMessage)` runs in the background; on return it emits `MessageSent { sending: false, serverId: X }` or `MessageSendFailed { localId, error }`.
- In `Vianigram.ViewModels`, the chat VM updates the observable collection instantly from the first event; the second event only changes the icon (✓ pending → ✓✓ sent / ⚠ failed).

**Mandatory telemetry:**
- `messages.send.optimistic_to_visible_ms` — the time from the tap to the first pixel of the bubble. Budget: P95 < 50ms.
- `messages.send.ack_duration_ms` — the time from optimistic to server-id.
- `messages.send.fail_rate` — the fraction that end as `MessageSendFailed`.

**Anti-pattern:** a modal spinner blocking the UI during a send. **Prohibited.**

### M2. FLOOD_WAIT respect at the port boundary

**Rule:** the `IMtProtoChannel` port returns `Result<TlResponse, MtProtoError>` where `MtProtoError.FloodWait(int seconds)` is a first-class case of the discriminated union. The Application layer **never** sees a raw `-429` from the server or a `RpcError` with string parsing. Each handler that invokes the port must make the backoff policy explicit.

**Why it matters:** Telegram MTProto returns `FLOOD_WAIT_X` per method, per user, per DC. Ignoring it leads to an account ban. Treating it as a generic exception leads to retry-storms that worsen the flood.

**Structure:**
```csharp
namespace Vianium.Mtproto.Api.V1
{
    public abstract class MtProtoError
    {
        public sealed class FloodWait : MtProtoError { public int Seconds { get; } }
        public sealed class AuthKeyInvalid : MtProtoError { }
        public sealed class MigrateDc : MtProtoError { public int TargetDc { get; } }
        public sealed class Network : MtProtoError { public string Detail { get; } }
        public sealed class ServerError : MtProtoError { public int Code; public string Message; }
        public sealed class Timeout : MtProtoError { public TimeSpan Elapsed { get; } }
    }

    public interface IMtProtoChannel
    {
        Task<Result<TlObject, MtProtoError>> InvokeAsync(TlObject request, InvokeOptions opts, CancellationToken ct);
    }
}
```

**Backoff policy** (defined in the Kernel, consumed by handlers):
- `FloodWait(seconds)` → exponential backoff with jitter, **respecting** the server-provided `seconds` as the minimum. Never retry before `seconds`.
- Per-method tracker: if `messages.sendMessage` received a recent FloodWait, the next sends to the same peer within the window stay in a local queue without touching the wire.
- `MigrateDc` → reconnect to the target DC (not a retry — a redirect).
- `AuthKeyInvalid` → invalidate the `AccountIdentity`, force a re-login.

**Telemetry:**
- `mtproto.flood_wait.seconds` (histogram).
- `mtproto.flood_wait.method` (counter with a `method` tag).

### M3. Key material isolation

**Rule:** only the `vianium-crypto` sibling (C++) may contain raw bytes of key material. **This includes:**
- MTProto auth keys (256 bytes per MTProto session).
- Secret-chat root keys (DH-derived).
- Secret-chat sliding keys (per layer).
- Ephemeral DH session keys (during a handshake or rekey).
- 2FA SRP-2048 password-derived secrets.

Managed C# code **never** holds a `byte[]` of key material. Only opaque handles (`AuthKeyHandle`, `SecretRootKeyHandle`, `SrpSessionHandle`) which are IDs (uint64 or GUID) resolved inside the native WinMD. Operations that need the key (encrypt, decrypt, MAC, fingerprint) are done by passing the handle to the WinMD.

**Why it matters:**
- The WP8.1 GC can relocate a `byte[]` without an opportunity to zeroize the old copy.
- WP8.1 has no `MemoryProtect` accessible from managed.
- AppContainer shares the heap between threads; any buffer overflow in a managed lib exposes the keys of other sessions.
- Audit: if keys never cross the ABI, the leak surface is reduced to the `vianium-crypto` sibling and to `Vianigram.Storage` (which has them encrypted at rest via DPAPI).

**How to enforce:**
- Code review: a `byte[]` with a name containing "key", "auth", "secret", "salt" in `Domain/`/`Application/` → reject.
- WinMD ABI: parameters are never `Platform::Array<uint8>` for key material; always typed handles (`AuthKeyHandle`, etc.).
- `Vianigram.Storage` encrypts at rest via `DataProtectionProvider`, but the blob's plaintext only exists inside the `vianium-crypto` sibling during the operation, it never materializes to managed.

**Anti-pattern (prohibited):**
```csharp
// PROHIBITED in any C# layer of Vianigram
var authKey = await _store.GetAuthKeyBytesAsync(accountId);  // ❌ byte[] crossing
var encrypted = AesIge.Encrypt(authKey, payload);            // ❌ managed AES
```

**Correct pattern:**
```csharp
var handle = await _crypto.ResolveAuthKeyHandleAsync(accountId, ct);  // opaque handle
var encrypted = await _crypto.EncryptAsync(handle, payloadBuffer, ct); // C++ does everything
```

### M4. Message-history immutability post server ACK

**Rule:** once a message has a `ServerId` (the response of `messages.sendMessage` or the `Update` that delivers it), its content is **immutable** from the client's perspective. Edits and deletes do not mutate the original message's aggregate — they emit **new events** (`MessageEdited`, `MessageDeleted`) that the projections consume.

**Why it matters:**
- Audit trail: the event log is the truth. A message was sent as-is; a later edit is a separate event.
- Deterministic sync: if the events are immutable, replay from a pts/qts always produces the same state.
- Search index: it indexes the body of the original `MessageSent`; the edits are indexed as `MessageEdited`, keeping a history.
- UI: the "edited" indicator is rendered based on the presence of a later `MessageEdited` event, not by querying a mutable field of the aggregate.

**Event log structure (in `MessageStream`):**
```csharp
// immutable
public sealed class MessageSent : DomainEventBase {
    public MessageId LocalId { get; }
    public MessageId? ServerId { get; }
    public PeerId Peer { get; }
    public MessageBody Body { get; }
    public bool Sending { get; }
}

// a new event on edit — does not modify the original MessageSent
public sealed class MessageEdited : DomainEventBase {
    public MessageId Id { get; }            // server id of the original
    public MessageBody NewBody { get; }
    public DateTime EditedAtUtc { get; }
    public int EditRevision { get; }        // 1, 2, 3...
}

public sealed class MessageDeleted : DomainEventBase {
    public MessageId Id { get; }
    public DateTime DeletedAtUtc { get; }
    public bool ForEveryone { get; }
}
```

The projection of "current message body" is computed as a left-fold over the events. Storing the current state in SQLite is a cache of the projection, not the source of truth.

**Anti-pattern (prohibited):**
```csharp
public void Edit(MessageId id, MessageBody newBody) {
    var msg = _byId[id];
    msg.Body = newBody;          // ❌ mutation
    msg.EditedAt = _clock.UtcNow; // ❌
}
```

### M5. Server-truth wins on sync conflicts

**Rule:** the local state may be optimistic, but in the face of a conflict (a server-side delete that arrives after a local edit, a server-side edit that arrives after a pending local edit, a reorder of pinned messages, a channel admin changing the title), **the server wins**. The `Sync` bounded context emits a `ConflictResolved` event with enough detail for the UI to re-render and the user to perceive the correction.

**Why it matters:**
- Telegram is server-authoritative. It is not a CRDT, it is not Operational Transform. The server determines the canonical ordering.
- Attempting a client-side merge leads to impossible states (a "edited-deleted-restored" message that the server never allowed).
- Better UX: the user sees a flicker of correction and understands; a persistent divergent state is a bug.

**Structure:**
```csharp
namespace Vianigram.Sync.Domain.Events
{
    public sealed class ConflictResolved : DomainEventBase
    {
        public ConflictKind Kind { get; }              // EditOverwritten, DeletedRemotely, ReorderApplied, ...
        public IReadOnlyList<MessageId> AffectedMessages { get; }
        public IReadOnlyList<DialogId> AffectedDialogs { get; }
        public string ServerStateSnapshot { get; }     // opaque blob for debugging
    }
}
```

Other contexts subscribe to `ConflictResolved` and reconcile:
- `Messages` invalidates its cache of the affected MessageIds.
- `Chats` re-fetches the dialog snapshot.
- `Search` re-indexes.
- `Notifications` cancels pending toasts for messages that the server deleted.

**How to enforce:**
- The `Sync` context is the **only** one that applies server updates. Other contexts only apply *locally-originated* mutations.
- If a pending local edit receives a server `Update` that contradicts it, the local edit is discarded (with telemetry) and the server state is applied.

**Telemetry:**
- `sync.conflict.count` with a `kind` tag.
- `sync.conflict.local_changes_dropped` (counter).

### M6. Sync state is an exclusive invariant of the Sync bounded context

**Rule:** `pts`, `qts`, `seq`, `date`, and the per-channel `pts` values are the **exclusive** property of the `SyncCursor` aggregate in `Vianigram.Sync`. No other context reads, writes, or even has visibility into these values. `Sync` exposes typed incremental events as the only public surface.

**Why it matters:**
- These four counters are the heart of the Telegram updates protocol. Bad manipulation corrupts consistency for the user (lost messages, duplicate messages, dialogs in the wrong order).
- If `Messages` or `Chats` were to touch them directly, the invariant "every update is applied exactly once" stops being verifiable.
- A single choke-point makes incident response trivial: "is the sync corrupted?" → look at `SyncCursor`, not 13 contexts.

**Public exposure pattern:**
```csharp
namespace Vianigram.Sync.Api.V1
{
    public interface ISyncApi
    {
        // starts the sync loop at login
        Task<Result, Error> StartAsync(AccountIdentitySnapshot account, CancellationToken ct);

        // explicit gap recovery (cold start, push wakeup, etc.)
        Task<Result, Error> ReconcileAsync(CancellationToken ct);

        // read-only state for debug/UI; NOT mutable
        SyncHealthSnapshot GetHealth();
    }

    public sealed class SyncHealthSnapshot
    {
        public DateTime LastUpdateUtc { get; }
        public bool HasGap { get; }
        public int PendingChannels { get; }
        // pts/qts/seq/date are NOT exposed
    }
}

namespace Vianigram.Sync.Domain.Events
{
    public sealed class UpdatesApplied : DomainEventBase
    {
        public IReadOnlyList<MessageReceivedProjection> NewMessages { get; }
        public IReadOnlyList<MessageEditedProjection> EditedMessages { get; }
        public IReadOnlyList<MessageDeletedProjection> DeletedMessages { get; }
        public IReadOnlyList<DialogChangedProjection> DialogChanges { get; }
        public IReadOnlyList<UserStatusChangedProjection> StatusChanges { get; }
    }
}
```

The other contexts subscribe to `UpdatesApplied` and apply their respective changes. They **do not** invert it — there is no way to ask `Sync` to "apply this update for me".

**How to enforce:**
- `pts`, `qts`, `seq`, `date` are `internal` in `Vianigram.Sync` and only accessible from within the assembly.
- The SQLite schema for sync state lives in `Vianigram.Storage` but only `Vianigram.Sync` has the corresponding repo (via an OCAP provider).
- Code review rejects any reference to these names outside the Sync context.

---

## The 13 performance rules (a baked-in mandate)

These are the performance bets that differentiate Vianigram from a standard Telegram client. Each rule includes **rule** (what to do), **why** (why it matters), **enforce** (how to verify).

### P1. MTProto serialization is C++

**Rule:** the (de)serialization of the TL protocol occurs in the `vianium-mtproto` sibling (`src\tl\`, C++). Managed never sees TL bytes — only generated types (C# POCOs emitted by the codegen) that are clean DTOs.

**Why:** TL is a binary format with prefix-length, cookies, and vectors. Doing it in managed implies `BinaryReader`/`BinaryWriter` allocations per field, a GC pressure catastrophe in the updates path where it can reach 1 update/100ms. C++ with an arena allocator and a `Span`-equivalent reduces the alloc rate to ~0 in steady state.

**Enforce:** code review; any `BinaryReader` or `BitConverter` in C# managed `Vianigram.*` is a reject. ETW trace: the P95 of `mtproto.serialize.duration_us` < 200µs on 1 GB-class hardware.

### P2. AES-IGE and AES-GCM are C++

**Rule:** AES-256-IGE (MTProto), AES-256-GCM (at-rest, an optional secret-chat layer extension), HKDF, HMAC-SHA-256/512 live in the `vianium-crypto` sibling. They reuse the primitives from `..\vianium-tls\src\crypto\` (sha256, sha512, hkdf, hmac, x25519, aes_core, bignum).

**Why:** `System.Security.Cryptography.AesManaged` on WP8.1 is ~10x slower than an AES-NI / table-based one in C++. And the GC pressure of allocate-per-block worsens it. For a sustained MTProto channel (heartbeat + updates), managed crypto becomes 30%+ of the CPU.

**Enforce:** `using System.Security.Cryptography` prohibited outside of `Vianigram.Storage` (where only `DataProtectionProvider`, which is native under the hood, is used). Microbenchmark: `aes_ige.encrypt_1mb` (measured against the `vianium-crypto` sibling) < 8ms on a 1 GB device.

### P3. Connection pool per DC

**Rule:** default 4 sockets per DC, MTProto multiplexed by `msg_id`. Automatic single-socket fallback on a device with < 512 MB RAM. Each socket handles a TCP MTProto transport (Abridged or Intermediate framing).

**Why:** a single socket serializes everything: a slow update blocks a send. Four sockets allow real parallelism (downloading a chat file does not block the reception of updates in another chat). The official Telegram-iOS uses 4-8 connections per DC.

**Enforce:** an integration test that opens 4 concurrent connections and measures the ping-pong RTT of each; none should exceed the base RTT + 30%. Memory budget: < 4 MB total for the 4 sockets (buffer + state).

### P4. Parallel chunked file ops

**Rule:** default 4 parallel chunks in upload/download, adaptive 1→8 depending on RTT and the FLOOD_WAIT signal. Adaptive chunk size 64 KB → 1 MB depending on the observed bandwidth. **Never hardcode 512 KB single-stream** (which is what PivoraTelegram does today).

**Why:** PivoraTelegram takes ~25s to upload a 5MB photo on LTE because it serves 1 chunk at a time. Vianigram with 4 parallel and adaptive chunks drops to < 8s. For video/long voice the difference is 5x+.

**Enforce:** a smoke test measures the upload of a 5 MB blob, target P95 < 8s on LTE. Adaptive logic: if RTT > 800ms increase the chunks (jitter buffer wins); if a FloodWait is received decrease the chunks (server pushback).

### P5. Zero-copy data plane

**Rule:** native code keeps media buffers (download chunks, upload chunks, voice frames) in an arena allocator and exposes only `Windows.Storage.Streams.IBuffer` handles to managed. **No `byte[]` marshal** in the file/media/voip paths.

**Why:** marshalling a `byte[]` through the WinMD ABI implies a copy (managed heap → native heap or vice versa). For a 256 KB chunk + 4 parallel + 60 chunks per file, that is ~60 MB of unnecessary allocations per upload. With `IBuffer` the ownership is transferred without a copy.

**Enforce:** the ETW trace `media.upload.bytes_copied` must be exactly 0 or the chunk size of the last merge (not multiples of the buffer count). Code review: `Marshal.Copy` or `Array.Copy` in a media path → reject.

### P6. Optimistic UI mandatory for sends

**Rule:** see §M1 above. The UI renders the bubble in the "sending" state in < 50ms; it reconciles when the server ACK arrives.

**Why:** perception of speed. The user associates "Telegram = instant"; any delay > 100ms is perceived as lag, even if the actual network has an 800ms RTT.

**Enforce:** ETW + manual test: tap → first pixel of the bubble. Budget P95 < 50ms.

### P7. Incremental sync, never reload

**Rule:** never a full reload of the chat list at runtime. Always a diff from the last `pts`/`qts`. `getDifference` only on (a) cold start, (b) an explicitly detected gap, (c) a push wakeup after > 5min suspended.

**Why:** a full reload of 200 dialogs implies ~200 KB of TL response + 200 inserts to SQLite + 200 events to the EventBus. With incremental sync the steady state is ~1 update/100ms ~50 bytes. The difference is 1000x less work.

**Enforce:** the counter `sync.full_reload.count`. Budget: < 1 per hour in normal use. Telemetry alert if > 5/hour.

### P8. Lazy scrollback

**Rule:** the chat history scroll reads from SQLite first (an offset+limit window). When the user scrolls beyond the cached window, on-miss a `messages.getHistory` is done to the server, appended to SQLite, rendered.

**Why:** opening a chat with 10000 historical messages must not download 10000 messages. It loads ~50 (what is visible + a buffer), the rest on-demand. Memory budget < 5 MB per open chat.

**Enforce:** the ETW `messages.history.window_size` must be ≤ 100. Memory profiler: no leak when opening/closing 10 chats consecutively.

### P9. FLOOD_WAIT honored at the port boundary

**Rule:** see §M2 above. `IMtProtoChannel` returns `Result<TlResponse, MtProtoError>` with `FloodWait` first-class. Backoff with jitter, a per-method tracker.

**Why:** without this, a retry-storm in `messages.sendMessage` after a FloodWait generates an account ban for the user. With this, the retry respects the server-provided cooldown.

**Enforce:** an integration test against the test DC simulates an artificial FloodWait (some methods return it at a low frequency); the client must not re-invoke before the cooldown.

### P10. Memory budget < 120 MB resident

**Rule:** the working set in normal use (foreground, 1 chat open, sync active) < 120 MB. Arena allocators with explicit reset in native; a buffer pool for media chunks; no leaks when opening/closing 10 chats.

**Why:** a 1 GB device has 1 GB of RAM; WP8.1 kills apps when the system needs memory. > 120 MB = a higher probability of death in the background, loss of notifications, a cold restart on return.

**Enforce:** a Phase 7 audit with the Windows Performance Toolkit. Manual test: chat list scroll for 5min + opening/closing 10 chats; memory diff < 5 MB.

### P11. No `.Result` / `.Wait()` / sync-over-async

**Rule:** all managed boundaries are `async`. `ConfigureAwait(false)` in the Kernel/Application is mandatory. `async void` only in UI event handlers.

**Why:** the WP8.1 UI thread is a single one; a `.Result` in a critical path freezes the app for the duration of the MTProto round-trip (~500ms). PivoraTelegram has > 30 occurrences of `.Result` and it shows.

**Enforce:** a Roslyn analyzer (custom rule) that fails CI if it detects `.Result` or `.Wait()` in `Vianigram.*`. Code review.

### P12. Encryption at rest via DataProtectionProvider

**Rule:** MTProto auth keys, secret-chat root keys, optionally message bodies (gated by a passcode user setting) encrypted at rest with `Windows.Security.Cryptography.DataProtection.DataProtectionProvider` scope `LOCAL=user`.

**Why:** WP8.1 SD card storage is not encrypted by default; a lost device can be triaged and the SQLite blobs read in plaintext. DPAPI scope LOCAL=user binds it to the device's account, infeasible without the OS passcode.

**Enforce:** a test that dumps `LocalState/vianigram.db` and verifies that it does not contain recognizable strings ("auth_key", phone numbers, message bodies when the passcode is active).

### P13. Domain Event Bus from day 1

**Rule:** see §1 of the 6 rules. Each mutation emits an event; other contexts subscribe.

**Why:** already covered. Here is the performance spin: the bus is the cheap mechanism of cross-context wiring (an O(1) dispatch per subscriber) vs polling (which is O(N) per tick).

**Enforce:** already covered.

---

## Specific C# patterns (the same as the `vianium-managed-kernel` sibling)

### `Result<T, TError>` in the Kernel

```csharp
public readonly struct Result<T, TError>
{
    private readonly T _value;
    private readonly TError _error;
    private readonly bool _isOk;

    private Result(T value, TError error, bool isOk) { _value = value; _error = error; _isOk = isOk; }

    public static Result<T, TError> Ok(T v) => new Result<T, TError>(v, default, true);
    public static Result<T, TError> Fail(TError e) => new Result<T, TError>(default, e, false);

    public bool IsOk => _isOk;
    public T Value => _isOk ? _value : throw new InvalidOperationException("Result is in failed state");
    public TError Error => _isOk ? throw new InvalidOperationException("Result is in ok state") : _error;

    public Result<U, TError> Map<U>(Func<T, U> f) => _isOk ? Result<U, TError>.Ok(f(_value)) : Result<U, TError>.Fail(_error);
    public Result<U, TError> Bind<U>(Func<T, Result<U, TError>> f) => _isOk ? f(_value) : Result<U, TError>.Fail(_error);
    public TR Match<TR>(Func<T, TR> ok, Func<TError, TR> fail) => _isOk ? ok(_value) : fail(_error);
}

public static class Result
{
    public static Result<T, TError> Ok<T, TError>(T v) => Result<T, TError>.Ok(v);
    public static Result<T, TError> Fail<T, TError>(TError e) => Result<T, TError>.Fail(e);
}
```

### Domain event base

```csharp
public interface IDomainEvent
{
    DateTime TimestampUtc { get; }
    string Kind { get; }
}

public abstract class DomainEventBase : IDomainEvent
{
    protected DomainEventBase(IClock clock) { TimestampUtc = clock.UtcNow; }
    public DateTime TimestampUtc { get; }
    public string Kind => GetType().FullName;
}

public sealed class MessageReceived : DomainEventBase
{
    public MessageReceived(MessageId id, PeerId peer, MessageBody body, IClock clock) : base(clock)
    {
        Id = id; Peer = peer; Body = body;
    }
    public MessageId Id { get; }
    public PeerId Peer { get; }
    public MessageBody Body { get; }
}
```

### IEventBus

```csharp
public interface IEventBus
{
    void Publish<T>(T evt) where T : IDomainEvent;
    SubscriptionToken Subscribe<T>(Func<T, Task> handler) where T : IDomainEvent;
    SubscriptionToken Subscribe<T>(Action<T> handler) where T : IDomainEvent;
}

public sealed class SubscriptionToken : IDisposable
{
    private readonly Action _unsubscribe;
    internal SubscriptionToken(Action unsubscribe) { _unsubscribe = unsubscribe; }
    public void Dispose() => _unsubscribe?.Invoke();
}
```

### ViewModel as an inbound adapter (no business logic)

```csharp
public sealed class ChatPageViewModel : BaseViewModel
{
    private readonly ISendMessageHandler _send;
    private readonly ILoadHistoryQuery _loadHistory;
    private readonly IEventBus _events;
    private readonly SubscriptionToken[] _subs;

    public ObservableCollection<MessageRowViewModel> Messages { get; } = new();
    public AsyncCommand<string> SendCommand { get; }

    public ChatPageViewModel(
        ISendMessageHandler send, ILoadHistoryQuery loadHistory, IEventBus events, PeerId currentPeer)
    {
        _send = send; _loadHistory = loadHistory; _events = events;
        SendCommand = new AsyncCommand<string>(async text =>
        {
            await _send.HandleAsync(new SendMessageCommand(currentPeer, MessageBody.Text(text)), CancellationToken.None);
        });
        _subs = new[]
        {
            _events.Subscribe<MessageSent>(OnMessageSent),
            _events.Subscribe<MessageReceived>(OnMessageReceived),
            _events.Subscribe<MessageEdited>(OnMessageEdited),
            _events.Subscribe<MessageDeleted>(OnMessageDeleted),
        };
    }

    private void OnMessageSent(MessageSent e) { /* prepend bubble in the sending state */ }
    private void OnMessageReceived(MessageReceived e) { /* append bubble */ }
    private void OnMessageEdited(MessageEdited e) { /* swap the body in the existing bubble */ }
    private void OnMessageDeleted(MessageDeleted e) { /* remove or gray out */ }

    public override void Dispose()
    {
        foreach (var s in _subs) s.Dispose();
        base.Dispose();
    }
}
```

---

## Banned patterns (what never to do)

1. **God objects** — PivoraTelegram's `TelegramClient` with 14 fields is exactly this. In Vianigram it is distributed across 14 bounded contexts.
2. **Mutable global singletons** — `DatabaseManager.Instance`, `TelegramClient.Instance`. Inject.
3. **Reaching into other contexts** — `using Vianigram.Messages` from `Vianigram.Notifications` → reject. Use an ACL adapter.
4. **Heavy code-behind** — XAML pages with > 100 lines of orchestration. Move to a ViewModel + UseCases.
5. **Business logic in ViewModels** — the VMs are the inbound adapter. The logic lives in `Application/UseCases`.
6. **Catching a generic exception** (`catch (Exception)`) without mapping to a `Result` or structured logging.
7. **`async void`** outside of UI event handlers.
8. **`.Result` or `.Wait()`** over a Task — always `await`.
9. **`Task.Run` in hot paths** — you are already in a task context; you do not need another thread pool hop.
10. **Storage coupled to JSON** — the format is the adapter's decision, the contract is `IObjectStore<T>` or `ISqliteRepository<T>`.
11. **Encryption with custom XOR** or managed `Aes`. Use `DataProtectionProvider` or the `vianium-crypto` sibling.
12. **Reusing names between contexts** — `Message` in `Messages` ≠ `Message` in `SecretChats`. Name them `CloudMessage`, `SecretMessage` when both coexist in the same scope (ACL adapters, ViewModels).
13. **`byte[]` of key material in managed** — auth keys, secret root keys, DH keys never cross the managed ABI (§M3).
14. **Mutating history post-ACK** — edits/deletes are new events, not a mutation of the original (§M4).
15. **Other contexts reading pts/qts** — sync state is exclusive to `Vianigram.Sync` (§M6).
16. **Ignoring FLOOD_WAIT** — re-invoking before the server-provided cooldown leads to a ban (§M2, §P9).

---

## Enforcement

### Build-time

- **Project references inspected** — a CI script that verifies that no bounded-context `.csproj` references another bounded context (except `Vianigram.Composition`).
- **Custom Roslyn analyzers** — fails CI if it detects `.Result` / `.Wait()` / `async void` (except in `*.xaml.cs` event handler files) / `byte[] authKey` patterns.
- **`DotnetCAT` rule** — the namespace `System.Security.Cryptography` is only permitted in `Vianigram.Storage/Infrastructure/Persistence/`. For additional crypto use the `vianium-crypto` sibling.

### Code review checklist

Each bounded-context PR must affirmatively answer:
- [ ] Is Domain/ pure (only BCL + Kernel)?
- [ ] Does Application/ not touch Infrastructure directly?
- [ ] Does each aggregate mutation emit a domain event?
- [ ] Do the public APIs live in `Api/V1/` or `Api/V2/`?
- [ ] Do the Result<T,Error> values cover all the failure paths?
- [ ] Is there telemetry with the naming `<context>.<operation>.<unit>`?
- [ ] (If it touches crypto) does it use the `vianium-crypto` sibling and not handle a byte[] of key material?
- [ ] (If it touches sync state) is it in `Vianigram.Sync` and not outside?
- [ ] (If it touches MTProto) does it handle `MtProtoError.FloodWait` explicitly?
- [ ] (If it is a send) does it have optimistic-UI with a < 50ms render path?

### Smoke tests (in `Clients/Vianigram.SmokeTests/`)

They block regressions of invariants:
- **`TelegramHandshakeSmokeTest`** — a DH handshake against the test DC `149.154.167.40:443` completes in < 2s, AES-IGE NIST vectors pass, a TL roundtrip of an `updates.getDifference` response.
- **`FloodWaitRespectSmokeTest`** — invokes a ratelimited method on the test DC, verifies that the client waits the reported cooldown and does not re-invoke before it.
- **`OptimisticSendBudgetSmokeTest`** — sends 100 consecutive text messages to Saved Messages, measures the P95 of tap-to-bubble. Fails if > 50ms.
- **`KeyMaterialIsolationLintTest`** — scans the managed assemblies for key-material symbols in plain `byte[]`. Fails if it finds any outside the allowlist (which only contains `Vianium.Crypto.dll` from the `vianium-crypto` sibling and `Vianigram.Storage.dll`).
- **`SyncStateOwnershipLintTest`** — scans for the strings `"pts"`, `"qts"`, `"seq"`, `"date"` in code outside of `Vianigram.Sync/`. Fails if it finds a match.

---

## References

### Internal (sibling repos `vianium-*`)

- `..\vianium-managed-kernel\docs\managed-architecture\principles.md` — the C# mirror of the managed kernel; Vianigram clones the structure.
- `..\vianium-managed-kernel\docs\managed-architecture\00-overview.md` — the managed kernel overview.
- `..\vianium-kernel\docs\native-port\architecture-principles.md` — the C++ DDD+hex pattern that Vianigram replicates for `Vianigram.Core.Media` and that the native siblings follow.
- `..\vianium-tls\docs\security\tls-policy.md` — the canonical Mozilla Modern TLS policy; Vianigram synchronizes it as a local snapshot.
- `..\vianium-tls\src\crypto\` — the reused crypto primitives (consumed via the `vianium-crypto` sibling).

### Internal (Vianigram)

- [00-overview.md](00-overview.md) — the global vision of the C# messenger product.
- [01-account.md](01-account.md) — the Account context.
- [02-chats.md](02-chats.md) — the Chats context.
- [03-messages.md](03-messages.md) — the Messages context.
- [04-contacts.md](04-contacts.md), [05-media.md](05-media.md), [06-sync.md](06-sync.md), [07-calls.md](07-calls.md), [08-secret-chats.md](08-secret-chats.md), [09-stickers.md](09-stickers.md), [10-notifications.md](10-notifications.md), [11-settings.md](11-settings.md), [12-search.md](12-search.md), [13-privacy.md](13-privacy.md), [14-presentation.md](14-presentation.md), [15-shell-and-host.md](15-shell-and-host.md).
- [../native-port/architecture-principles.md](../native-port/architecture-principles.md) — the C++ mirror.
- [../native-port/01-mtproto.md](../native-port/01-mtproto.md), [../native-port/02-tl.md](../native-port/02-tl.md), [../native-port/03-crypto.md](../native-port/03-crypto.md).
- [../security/mtproto-policy.md](../security/mtproto-policy.md) — MTProto invariants (replay window, msg_id ordering, server-salt rotation, SRP-2048).
- [../security/at-rest-encryption.md](../security/at-rest-encryption.md) — DPAPI scope, what is encrypted.
- [../security/tls-policy.md](../security/tls-policy.md) — the TLS policy (a synchronized snapshot of the `vianium-tls` sibling).

### External — specs and RFCs

- **Telegram MTProto 2.0 spec** — https://core.telegram.org/mtproto
- **TL schema language** — https://core.telegram.org/mtproto/TL
- **Telegram API methods (layer 214)** — https://core.telegram.org/methods
- **Diffie-Hellman key exchange (RFC 2631, 5114)** — to validate the set of primes that Telegram uses.
- **AES-IGE mode** — https://www.links.org/files/openssl-ige.pdf
- **SRP-6a (Telegram 2FA)** — https://core.telegram.org/api/srp ; RFC 5054 baseline.
- **Curve25519 (RFC 7748)** — for secret chats.
- **Opus codec (RFC 6716)** — for voice/video call audio.
- **WebP** — the Google bitstream spec.

### External — papers / architectural references

- **DDD: Tackling Complexity in the Heart of Software** — Eric Evans, 2003.
- **Hexagonal Architecture** — Alistair Cockburn, 2005.
- **Capability-based security** — Mark Miller (E language), Norm Hardy (KeyKOS).
- **Pony language reference capabilities** — for inspiration for `IObjectCapabilityProvider`.

---

## Quick cross-link

- The native mirror: [../native-port/architecture-principles.md](../native-port/architecture-principles.md)
- Overview: [00-overview.md](00-overview.md)
- The canonical TLS policy (inherited from the `vianium-tls` sibling): [../security/tls-policy.md](../security/tls-policy.md)
- The MTProto policy: [../security/mtproto-policy.md](../security/mtproto-policy.md)
- At-rest encryption: [../security/at-rest-encryption.md](../security/at-rest-encryption.md)
