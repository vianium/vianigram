# Managed Architecture — Overview (Vianigram, C# layer)

> **Required prior reading:** [principles.md](principles.md). Defines DDD + hexagonal applied to C#, the 6 non-negotiable rules, the 9 enforced rules, the 6 messenger-specific extensions, the 13 performance rules, the managed Kernel, and the C# patterns (Result, EventBus, ViewModel-as-adapter).

## Architecture summary

**Vianigram = DDD + Hexagonal per bounded context, dual-language (C# managed + C++ native), on top of the transport foundation of the `vianium-*` siblings.** Each functional subsystem of the messenger is a bounded context with its own `Domain/Application/Ports/Infrastructure/Api/Vn`, communicating by domain events via `IEventBus` and by typed interfaces in `Api/V1`. The critical data plane (MTProto, TL, crypto, media decode, voip) lives mostly in the `vianium-mtproto` (with its `src\tl\` and `src\mtproto\` subcomponents), `vianium-crypto`, and `vianium-voip` siblings; the only native context that Vianigram keeps in its own repo is `Vianigram.Core.Media`. The transport plane (C++ Kernel, TLS 1.3, HTTP/1.1+H2, Net policies) is reused by project reference from the sibling repos `..\vianium-kernel\`, `..\vianium-tls\`, `..\vianium-http\`, `..\vianium-net\`. Zero duplication of transport code; fixes in TLS or HTTP flow automatically. PivoraTelegram is consulted as a read-only reference to understand protocol invariants and product flows, but no code is ported.

## Purpose

This document orchestrates the **complete construction** of Vianigram's C# layer. It is a **clean rewrite** from day 1 — there is no legacy to refactor; PivoraTelegram remains a reference repo.

Today (the state of PivoraTelegram, what we do **not** want):
- God-object `TelegramClient` with 14 fields.
- Singleton `DatabaseManager.Instance` with `.Result` blocks blocking the UI thread.
- Single-socket TCP per DC; chunk size hardcoded at 512 KB without parallelism.
- No FLOOD_WAIT backoff — retry storms possible.
- Key material (auth keys, secret root keys) in plain `byte[]` crossing layers.
- HTTP/TLS hand-rolled via `PivoraHTTP` without reuse between projects.

Tomorrow (the Vianigram target, Phase 6 close):
- 14 managed bounded contexts (Account, Chats, Messages, Contacts, Media, Sync, Calls, SecretChats, Stickers, Notifications, Settings, Search, Privacy, Storage), each with `Domain/Application/Ports/Infrastructure/Api/V1`.
- 1 native C++ bounded context in its own repo: `Vianigram.Core.Media`.
- 4 native bounded contexts reused via project reference from siblings of the `vianium` org: `vianium-crypto`, `vianium-mtproto` (with its `src\tl\` and `src\mtproto\` subcomponents), and `vianium-voip` (with `src\tgcalls\` and `third_party\libtgvoip\`).
- 4 native foundation contexts reused via project reference (siblings `vianium-kernel`, `vianium-tls`, `vianium-http`, `vianium-net`).
- 1 cross-cutting managed Kernel (`Vianigram.Kernel`).
- 1 Composition root (`Vianigram.Composition`).
- ViewModels as the inbound UI adapter, with no business logic.
- XAML Pages with minimal code-behind (DataContext wiring + UI event handlers).
- A background agent (`Vianigram.Agent`) that shares storage but not in-memory state with the foreground.

## Bounded context inventory

A complete inventory of **all** the bounded contexts (managed + native in dedicated siblings + reused native foundation).

| # | Context | Lang | Aggregate root | Storage | Notes |
|---|---|---|---|---|---|
| K | Vianigram.Kernel | C# | — (kernel; structural clone of the managed kernel sibling `vianium-managed-kernel\`) | — | Result, EventBus, Capability, Telemetry, Identity |
| C | Vianigram.Composition | C# | — (DI root) | — | Manual DI, ACL adapters, OCAP providers |
| 01 | Vianigram.Account | C# | `AccountIdentity` | LocalSettings (encrypted) | Phone/SMS/2FA SRP-2048/QR/multi-account |
| 02 | Vianigram.Chats | C# | `Dialog` | SQLite | 1:1, group, supergroup, channel |
| 03 | Vianigram.Messages | C# | `MessageStream` | SQLite | Send/edit/delete/forward/reactions |
| 04 | Vianigram.Contacts | C# | `ContactBook` | SQLite | Sync, blocked list |
| 05 | Vianigram.Media | C# | `MediaTransfer` | LocalFolder + SQLite | Up/download orchestration, thumbs |
| 06 | Vianigram.Sync | C# | `SyncCursor` | LocalSettings | pts/qts/seq/date state machine |
| 07 | Vianigram.Calls | C# | `CallSession` | (memory) | Voice/video signaling |
| 08 | Vianigram.SecretChats | C# | `SecretSession` | LocalSettings (encrypted) | DH, key fingerprints, E2E |
| 09 | Vianigram.Stickers | C# | `StickerLibrary` | LocalFolder | Packs, animated cache |
| 10 | Vianigram.Notifications | C# | `NotificationProfile` | LocalSettings | Toast, tile, badge, mute |
| 11 | Vianigram.Settings | C# | `UserPreferences` | LocalSettings | Preferences |
| 12 | Vianigram.Search | C# | `SearchSession` | (compute, low storage) | Global + per-chat |
| 13 | Vianigram.Privacy | C# | `PrivacyProfile` | LocalSettings | Passcode, last-seen, blocked |
| 14 | Vianigram.Storage | C# | (infra) | SQLite | Repos shared across contexts |
| 15 | Vianigram.ViewModels | C# | — (presentation) | — | BaseViewModel, AsyncCommand |
| 16 | Vianigram.App | C# | — (XAML pages) | — | 26 pages, 16+ controls |
| 17 | Vianigram.Agent | C# | — (background) | — | Push, sync |
| N1 | Sibling `vianium-mtproto` (`src\mtproto\`) | C++ | — (native infra) | — | TCP framing, AES-IGE, DH, sessions |
| N2 | Sibling `vianium-mtproto` (`src\tl\`) | C++ | — (native infra) | — | TL serialize/deserialize |
| N3 | Sibling `vianium-crypto` | C++ | — (native infra) | — | Curve25519, AES-IGE, AES-GCM, SRP |
| N4 | Vianigram.Core.Media (local) | C++ | — (native infra) | — | Opus, WebP, scaling, thumbs |
| N5 | Sibling `vianium-voip` | C++ | — (native infra) | — | RTP, jitter buffer, tgcalls v2 (`src\tgcalls\`), libtgvoip legacy (`third_party\libtgvoip\`) |

**Reused via project reference from sibling repos:**

| # | Context | Lang | Origin | Notes |
|---|---|---|---|---|
| R1 | Vianium.Core.Kernel | C++ | Sibling `vianium-kernel` | Result<T>, arena, base abstractions |
| R2 | Vianium.Core.Tls | C++ | Sibling `vianium-tls` | TLS 1.3 Mozilla Modern; HTTP API to Telegram |
| R3 | Vianium.Core.Http | C++ | Sibling `vianium-http` | HTTP/1.1+H2 with connection pool |
| R4 | Vianium.Core.Net | C++ | Sibling `vianium-net` | HSTS, pinning, OCSP, CT, DoH |

**Total:** 17 managed Vianigram contexts + 1 local native context (Media) + 4 native contexts in dedicated siblings (Crypto, Tl, MTProto, Voip) + 4 reused native foundation contexts (Kernel, Tls, Http, Net) = **26 bounded contexts**.

## Final project layout

```
vianigram/
├── Vianigram.sln                           # MSBuild 14.0, target WP8.1, toolset v120_wp81
├── README.md
├── ROADMAP.md
├── ARCHITECTURE.md
├── THIRD_PARTY_NOTICES.md
├── TRADEMARKS.md
├── LICENSE
├── NOTICE
├── SECURITY.md
├── CONTRIBUTING.md
├── CODE_OF_CONDUCT.md
├── AUTHORS.md
├── build-validate.cmd                      # msbuild /p:Configuration=Debug /p:Platform=x86
├── docs/
│   ├── managed-architecture/               # principles.md, 00-overview.md, 01-15
│   ├── native-port/                        # architecture-principles.md, 00-05
│   └── security/                           # tls-policy.md, mtproto-policy.md, at-rest-encryption.md
├── templates/
│   ├── managed-context.template/           # Domain/Application/Ports/Infrastructure/Api/V1
│   └── native-context.template/            # src/{domain,application,ports,infrastructure,api,internal}
├── Clients/
│   ├── Vianigram.App/                      # XAML pages (26) + DataContext wiring
│   ├── Vianigram.Agent/                    # Background task (push, sync)
│   └── Vianigram.SmokeTests/               # Tests against Telegram test DCs
├── Core/
│   # Managed
│   ├── Vianigram.Kernel/                   # structural clone of the vianium-managed-kernel sibling
│   ├── Vianigram.Composition/              # manual DI root + ACL + OCAP
│   ├── Vianigram.ViewModels/               # BaseViewModel, AsyncCommand
│   ├── Vianigram.Account/
│   ├── Vianigram.Chats/
│   ├── Vianigram.Messages/
│   ├── Vianigram.Contacts/
│   ├── Vianigram.Media/
│   ├── Vianigram.Sync/
│   ├── Vianigram.Calls/
│   ├── Vianigram.SecretChats/
│   ├── Vianigram.Stickers/
│   ├── Vianigram.Notifications/
│   ├── Vianigram.Settings/
│   ├── Vianigram.Search/
│   ├── Vianigram.Privacy/
│   ├── Vianigram.Storage/                  # SQLite + at-rest encryption
│   # Native (the only context owned by Vianigram)
│   └── Vianigram.Core.Media/               # C++/CX
└── tools/
    └── tl-codegen/                         # C# build-time tool: scheme.tl → tl_layer_NNN.h/cpp
```

(The native contexts `vianium-crypto`, `vianium-mtproto`, and `vianium-voip` each live in their own sibling repo under the `vianium` org.)

`Vianigram.sln` adds project references to sibling repos:
- `..\vianium-kernel\Vianium.Core.Kernel.vcxproj`
- `..\vianium-tls\Vianium.Core.Tls.vcxproj`
- `..\vianium-http\Vianium.Core.Http.vcxproj`
- `..\vianium-net\Vianium.Core.Net.vcxproj`
- `..\vianium-crypto\Vianium.Crypto.vcxproj`
- `..\vianium-mtproto\Vianium.Mtproto.vcxproj`
- `..\vianium-voip\Vianium.Voip.vcxproj`

## Dependency view

```
                                          ┌──────────────────────┐
                                          │   Vianigram.Kernel   │
                                          └──────────┬───────────┘
                                                     │
   ┌───────────┬───────────┬───────────┬───────────┬─┴─┬───────────┬───────────┐
   ▼           ▼           ▼           ▼           ▼   ▼           ▼           ▼
┌────────┐ ┌────────┐ ┌─────────┐ ┌──────────┐ ┌─────┐ ┌────────┐ ┌─────────┐ ┌─────────┐
│Account │ │ Chats  │ │Messages │ │ Contacts │ │Media│ │  Sync  │ │  Calls  │ │ Secret  │
└───┬────┘ └───┬────┘ └────┬────┘ └────┬─────┘ └──┬──┘ └────┬───┘ └────┬────┘ │  Chats  │
    │          │           │           │          │         │          │      └────┬────┘
    └──────────┴───────────┴───────────┴──────────┴─────────┴──────────┴───────────┘
                                                  │
            ┌─────────────────────────────────────┴─────────────────────────────┐
            ▼                                                                   ▼
┌────────────────────────┐                                       ┌───────────────────────┐
│  Vianigram.Composition │  ← wires up EVERYTHING + ACL + OCAP    │   Vianigram.Storage  │
└────────────┬───────────┘                                       └──────────┬────────────┘
             │                                                              │
             │ WinMD bridges                                                ▼
             ├─→ Vianium.Mtproto.V1            (TCP, sessions, AES-IGE)        ← sibling vianium-mtproto
             ├─→ Vianium.Tl.V1                 (TL serialize/deserialize)      ← sibling vianium-mtproto (src\tl\)
             ├─→ Vianium.Crypto.V1             (Curve25519, SRP, AES-GCM)      ← sibling vianium-crypto
             ├─→ Vianigram.Core.Media.V1       (Opus, WebP, scaling)           ← local
             ├─→ Vianium.Voip.V1               (RTP, jitter buffer, tgcalls)   ← sibling vianium-voip
             │
             ├─→ Vianium.Core.Net.V1           (HSTS, DoH, pinning) ← sibling vianium-net
             ├─→ Vianium.Core.Http.V1          (HTTP/1.1+H2)        ← sibling vianium-http
             ├─→ Vianium.Core.Tls.V1           (TLS 1.3 modern)     ← sibling vianium-tls
             └─→ Vianium.Core.Kernel.V1        (arena, Result<T>)   ← sibling vianium-kernel
             ▼
┌────────────────────────┐
│  Vianigram.ViewModels  │  ← consumes the Api.V1 of each context
└────────────┬───────────┘
             ▼
┌────────────────────────┐         ┌────────────────────────┐
│  Vianigram.App  (XAML) │         │  Vianigram.Agent (BG)  │
└────────────────────────┘         └────────────────────────┘
```

## Phasing

A schedule with deliverables and acceptance criteria per phase. Dates are a target, not a commitment.

### Phase 0 — Foundation (target 1 week)

**Deliverables:**
- `Vianigram.sln` with project references to the siblings `..\vianium-kernel\`, `..\vianium-tls\`, `..\vianium-http\`, `..\vianium-net\` (and, when they come online, `..\vianium-crypto\`, `..\vianium-mtproto\`, `..\vianium-voip\`).
- The complete doc set: `principles.md`, `00-overview.md`, stubs of `01-15`, `tls-policy.md` (synchronized from the `vianium-tls` sibling), `mtproto-policy.md`, `at-rest-encryption.md`, native-port docs.
- `Vianigram.Kernel` with all the ports + primitive types + EventBus + CapabilityRegistry + Telemetry.
- `Vianigram.Composition` placeholder with a skeleton `VianigramCompositionRoot`.
- `templates/managed-context.template/` and `templates/native-context.template/`.
- `build-validate.cmd`.

**Acceptance:**
- `build-validate.cmd Debug x86` succeeds.
- `Vianigram.Kernel.dll` and `Vianigram.Composition.dll` compile.
- Reused WinMDs from the siblings (`Vianium.Core.Kernel`, `Vianium.Core.Tls`, `Vianium.Core.Http`, `Vianium.Core.Net`) resolve correctly from the `..\vianium-*\` paths.
- CI gate: cross-context references verified (script).

### Phase 1 — Native MTProto core (2-3 weeks)

**Deliverables:**
- Sibling `vianium-mtproto\src\mtproto\`: TCP framing (Abridged + Intermediate), session/salt management, msg_id sequencing, multi-connection per DC.
- Sibling `vianium-mtproto\src\tl\`: TL serializer/deserializer; covers the ~800 types of layer 214.
- `tools/tl-codegen/`: a C# build-time tool that emits `tl_layer_214.h/cpp` from `scheme.tl`.
- Sibling `vianium-crypto`: links the primitives from `..\vianium-tls\src\crypto\` (sha256, sha512, hkdf, hmac, x25519, aes_core, bignum) + an AES-256-IGE wrapper + DH key exchange + SRP-2048 client + a key handle table.

**Acceptance:**
- A SmokeTest sends `auth.sendCode` to the test DC `149.154.167.40:443` and receives a valid `auth.sentCode`.
- Vector tests: AES-IGE NIST vectors pass; DH primes per the Telegram spec; SRP-2048 vectors pass; Curve25519 RFC 7748 vectors pass.
- TL roundtrip: 1000+ messages cover the ~800 types of layer 214.
- Crypto self-test passes 10/10 on a 1 GB device.

### Phase 2 — Read messages MVP (2 weeks)

**Deliverables:**
- `Vianigram.Account`: a complete phone+SMS+2FA flow, multi-account.
- `Vianigram.Chats`: dialog list, metadata, folders.
- `Vianigram.Messages`: history fetch, lazy scroll.
- `Vianigram.Sync`: pts/qts/seq/date state machine, getDifference, channel diff.
- Three minimal XAML pages: SignIn, DialogList, ChatRead.

**Acceptance:**
- Login in production with a real account (phone + SMS, optional 2FA).
- Fetch the dialog list with a cold cache.
- Open a chat with 100+ historical messages, smooth scroll.
- Receive messages live via the updates loop.
- Persistence: the app survives a restart with an active session.

### Phase 3 — Storage + Contacts + Media download (2 weeks)

**Deliverables:**
- `Vianigram.Storage`: SQLite repos with encryption at rest via `DataProtectionProvider`.
- `Vianigram.Contacts`: contact sync, blocked list.
- `Vianigram.Media` (download path): parallel chunks, IBuffer zero-copy.
- `Vianigram.Core.Media` (local): Opus decode, WebP decode, SIMD image scaling.

**Acceptance:**
- The app survives a restart with a complete local cache (dialogs + messages + media).
- Contacts synchronize; the blocked list is applied.
- Photos, voice, stickers render from the local cache without re-downloading.
- DPAPI verifies: a dump of SQLite does not contain the plaintext of auth keys.

### Phase 4 — Send + Notifications (2 weeks)

**Deliverables:**
- `Vianigram.Messages` (send path): text, media, voice; optimistic UI < 50ms.
- `Vianigram.Media` (upload path): 4-8 parallel chunks, adaptive chunk size.
- `Vianigram.Notifications`: toast, tile, badge, per-chat mute.
- `Vianigram.Agent`: a background task with a push channel + incremental sync.

**Acceptance:**
- Round-trip send across all media types: text, photo, voice, file.
- Optimistic UI: tap → bubble visible P95 < 50ms (blocking smoke test).
- Toast on incoming when the app is suspended; a click navigates to the correct chat.
- Upload a 5 MB photo on LTE: P95 < 8s (parallel chunks).

### Phase 5 — Calls + SecretChats + Stickers (3 weeks)

**Deliverables:**
- `Vianigram.Calls` (managed) + the sibling `vianium-voip`: voice signaling, RTP, jitter buffer, Opus.
- `Vianigram.SecretChats`: DH per session, key fingerprints (QR + emoji), layer-aware message encryption.
- `Vianigram.Stickers`: pack install, recently used, animated cache.

**Acceptance:**
- 1:1 voice call to another Telegram client: setup < 3s, clean audio, no drops in 5min.
- Secret chat: key fingerprint match QR + emoji on both clients.
- Animated sticker (.tgs) plays back at 30 fps.

### Phase 6 — Privacy + Settings + Search + UI port (2-3 weeks)

**Deliverables:**
- `Vianigram.Privacy`: passcode, last-seen scopes, blocked list, active sessions.
- `Vianigram.Settings`: complete preferences.
- `Vianigram.Search`: global + per-chat.
- 26 XAML pages rewritten with the shape of the PivoraTelegram pages as a reference.

**Acceptance:**
- Functional parity with PivoraTelegram (all the features that PivoraTelegram supports work in Vianigram).
- Active session visible in other Telegram clients with the label "Vianigram".

### Phase 7 — Performance audit (1 week)

**Deliverables:**
- ETW traces captured on a 1 GB device.
- Memory profiling with the Windows Performance Toolkit.
- Parallel-download tuning (adaptive chunks validation).
- FLOOD_WAIT backoff verification.

**Acceptance:**
- Cold start < 2s on 1 GB-class hardware.
- Resident < 120 MB in normal use.
- Voice call setup < 3s.
- Chat list scroll 60 fps.
- The 13 performance rules of [principles.md §13 performance rules](principles.md) measurable and within budget.

## Composition root pattern

`Vianigram.Composition` is **the only** place where:
1. All the concrete aggregate roots are instantiated.
2. All the outbound ports are wired to their adapters.
3. The per-account / per-secret-session `IObjectCapabilityProvider` instances are built.
4. The cross-context ACL adapters are registered.
5. Dependencies are injected into the `App` and `Agent`.

```csharp
namespace Vianigram.Composition.Roots
{
    public sealed class VianigramCompositionRoot
    {
        // Wires up the entire app from native WinMDs + storage adapters + context handlers.
        // Called once from App.xaml.cs OnLaunched and once from Agent OnRun.
        public static async Task<VianigramRuntime> BuildAsync(CancellationToken ct)
        {
            // 1. Kernel singletons (stateless)
            var clock = new SystemClock();
            var logger = new CompositeLogger(new DebugLogger(), new FileLogger());
            var bus = new AsyncEventBus(logger);
            var telemetry = new CompositeTelemetry(new DebugTelemetry(), new FileTelemetry());
            var capabilities = new CapabilityRegistry();
            RegisterDefaultCapabilities(capabilities);

            // 2. Storage (DPAPI + SQLite)
            var secretStore = new DataProtectionSecretStore(scope: "LOCAL=user");
            var settingsStore = new LocalSettingsStore();
            var sqliteRepo = await SqliteRepositoryFactory.OpenAsync("vianigram.db", secretStore, ct);

            // 3. Native bridges (WinMD; vianium-* siblings except local Media)
            var crypto = new MtProtoCryptoBridge(Vianium.Crypto.V1.CryptoFactory.Create());
            var tlCodec = new TlCodecBridge(Vianium.Tl.V1.CodecFactory.Create());
            var mtprotoChannel = await MtProtoChannelBridge.ConnectAsync(crypto, tlCodec, settingsStore, ct);
            var mediaCodec = new MediaCodecBridge(Vianigram.Core.Media.V1.MediaFactory.Create());
            var voip = new VoipBridge(Vianium.Voip.V1.VoipFactory.Create());

            // 4. Bounded context handlers
            var account = new AccountModule(mtprotoChannel, secretStore, bus, clock, telemetry);
            var sync    = new SyncModule(mtprotoChannel, settingsStore, bus, clock, telemetry);
            var chats   = new ChatsModule(sqliteRepo, bus, clock, telemetry);
            var messages = new MessagesModule(mtprotoChannel, sqliteRepo, bus, clock, telemetry);
            var contacts = new ContactsModule(mtprotoChannel, sqliteRepo, bus, clock, telemetry);
            var media    = new MediaModule(mtprotoChannel, mediaCodec, sqliteRepo, bus, clock, telemetry);
            var calls    = new CallsModule(mtprotoChannel, voip, bus, clock, telemetry);
            var secret   = new SecretChatsModule(mtprotoChannel, crypto, secretStore, bus, clock, telemetry);
            var stickers = new StickersModule(mtprotoChannel, sqliteRepo, bus, clock, telemetry);
            var notif    = new NotificationsModule(bus, settingsStore, telemetry);
            var settings = new SettingsModule(settingsStore, bus, telemetry);
            var search   = new SearchModule(sqliteRepo, bus, telemetry);
            var privacy  = new PrivacyModule(mtprotoChannel, secretStore, bus, telemetry);

            // 5. Cross-context ACL adapters (no context references another directly)
            bus.Subscribe<MessageReceived>(notif.OnIncomingMessage);
            bus.Subscribe<MessageReceived>(search.IndexMessage);
            bus.Subscribe<UpdatesApplied>(messages.ApplyServerUpdates);
            bus.Subscribe<UpdatesApplied>(chats.ApplyServerUpdates);
            // ... ACL wiring continues ...

            // 6. OCAP providers per-account (multi-account isolation, see principles §M3, §2-B)
            var ocap = new ObjectCapabilityProviderBuilder()
                .Grant<Vianigram.Account.Api.V1.IAccountApi>(account.Api)
                .Grant<Vianigram.Chats.Api.V1.IChatsApi>(chats.Api)
                .Grant<Vianigram.Messages.Api.V1.IMessagesApi>(messages.Api)
                // ... etc ...
                .Build();

            return new VianigramRuntime(ocap, bus, telemetry, /* ... */);
        }
    }
}
```

**Composition root rules:**
- It is the **only** place with `using Vianigram.<AllTheContexts>` simultaneously.
- It builds top-down: Kernel → Storage → Native bridges → Handlers → ACL wiring → OCAP.
- Idempotent: calling `BuildAsync` twice throws `InvalidOperationException`.
- Ordered disposes at shutdown: ACL subs → handlers → bridges → storage → kernel.

## Cross-context dependencies

**Canonical rule:** the `Application/` of each context talks only to its `Ports/`. The `Ports/` define schemas that the context owns. The **ACL adapters** that connect a context's outbound ports to another's inbound API live exclusively in `Vianigram.Composition`.

### Who can talk to whom

| From → To | Direct allowed? | Via |
|---|---|---|
| `Domain/` → another context | ❌ Never | — |
| `Application/` → another context | ❌ Never | Define an outbound port; an ACL adapter in Composition |
| `Application/` → Kernel | ✅ Yes | Direct reference |
| `Application/` → `Vianigram.Core.Media` or WinMDs of `vianium-*` siblings | ❌ Only via an outbound port | Bridge in Infrastructure |
| `Application/` → `vianium-*` siblings (foundation) | ❌ Only via an outbound port | Bridge in Infrastructure |
| `Infrastructure/` → `Vianigram.Core.Media` | ✅ Yes | Direct WinMD reference |
| `Infrastructure/` → WinMDs of `vianium-*` siblings | ✅ Yes | Direct WinMD reference |
| `Composition` → all | ✅ Yes | It is the only orchestration point |
| `ViewModels` → a context's `Api/V1/` | ✅ Yes | Inbound consumer |
| `App` (XAML) → ViewModels | ✅ Yes | DataContext binding |
| `Agent` → a context's `Api/V1/` (subset) | ✅ Yes | A limited subset via the OCAP provider |

### Common cross-context patterns

**`Notifications` needs info from `Messages`:**
```csharp
// In Vianigram.Notifications/Ports/Outbound/IIncomingMessageProbe.cs
public interface IIncomingMessageProbe {
    PeerId GetPeer(MessageId id);
    MessageBody GetBody(MessageId id);
}

// In Vianigram.Composition (ACL adapter)
internal sealed class IncomingMessageProbeAdapter : IIncomingMessageProbe {
    private readonly Vianigram.Messages.Api.V1.IMessagesApi _messages;
    public IncomingMessageProbeAdapter(Vianigram.Messages.Api.V1.IMessagesApi messages) { _messages = messages; }
    public PeerId GetPeer(MessageId id) => _messages.GetSnapshot(id).Peer;
    public MessageBody GetBody(MessageId id) => _messages.GetSnapshot(id).Body;
}
```

**`Sync` distributes updates to all contexts:**
```csharp
// In Vianigram.Sync/Domain/Events
public sealed class UpdatesApplied : DomainEventBase { /* typed projections */ }

// Each interested context subscribes in its Module at boot
bus.Subscribe<UpdatesApplied>(messages.ApplyServerUpdates);
bus.Subscribe<UpdatesApplied>(chats.ApplyServerUpdates);
```

**Sync state is exclusive to `Vianigram.Sync`** — see [principles.md §M6](principles.md). No other context reads pts/qts/seq/date.

**Key material is exclusive to the `vianium-crypto` sibling** — see [principles.md §M3](principles.md). Managed handles only opaque `AuthKeyHandle` values.

## Build instructions

### Prerequisites

- Windows 10 with Visual Studio 2015 Update 3 (build 14.0.25431+).
- Windows Phone 8.1 SDK installed.
- The sibling repos `..\vianium-kernel\`, `..\vianium-tls\`, `..\vianium-http\`, `..\vianium-net\` (foundation) and, depending on the phase, `..\vianium-crypto\`, `..\vianium-mtproto\`, `..\vianium-voip\` cloned and buildable. Vianigram **requires** access to the sibling repos during a build to resolve project references.
- Platform toolset `v120_wp81` registered.

### Local build

```cmd
cd <path-to>\vianigram\
build-validate.cmd Debug x86
```

`build-validate.cmd` invokes:
```cmd
msbuild Vianigram.sln ^
    /p:Configuration=Debug ^
    /p:Platform=x86 ^
    /p:VisualStudioVersion=14.0 ^
    /p:PlatformToolset=v120_wp81 ^
    /m
```

### Project reference resolution

`Vianigram.sln` has entries with relative paths:
```
Project("{...}") = "Vianium.Core.Kernel", "..\vianium-kernel\Vianium.Core.Kernel.vcxproj", "{GUID}"
Project("{...}") = "Vianium.Core.Tls",    "..\vianium-tls\Vianium.Core.Tls.vcxproj",       "{GUID}"
Project("{...}") = "Vianium.Core.Http",   "..\vianium-http\Vianium.Core.Http.vcxproj",     "{GUID}"
Project("{...}") = "Vianium.Core.Net",    "..\vianium-net\Vianium.Core.Net.vcxproj",       "{GUID}"
```

If any of the `vianium-*` siblings is not at the expected path, the build fails with `MSB3202: project file does not exist`.

### Smoke tests

```cmd
msbuild Clients\Vianigram.SmokeTests\Vianigram.SmokeTests.csproj /p:Configuration=Debug /p:Platform=x86
# Then deploy and run on device or emulator.
```

Smoke tests target Telegram **test DCs** (`149.154.175.10`, `149.154.167.40`), never production.

## Current state vs target per C# project

| Project | State today (Phase 0) | Target (Phase 6 close) |
|---|---|---|
| `Vianigram.sln` | ❌ To be created | ✅ With project refs to `vianium-*` siblings + ~18 Vianigram projects |
| `Vianigram.Kernel` | ❌ To be created | ✅ Structural clone of the `vianium-managed-kernel` sibling |
| `Vianigram.Composition` | ❌ To be created | ✅ With `VianigramCompositionRoot` wiring up everything |
| `Vianigram.{Account,Chats,…,Privacy}` | ❌ To be created | ✅ 13 contexts with Domain/Application/Ports/Infrastructure/Api/V1 |
| `Vianigram.Storage` | ❌ To be created | ✅ SQLite repos + DPAPI |
| `Vianigram.Core.Media` (local) | ❌ To be created | ✅ Native context with a WinMD ABI |
| Siblings `vianium-crypto`, `vianium-mtproto`, `vianium-voip` | ❌ To be created (in their respective repos) | ✅ WinMDs consumed by Vianigram |
| `Vianigram.App` | ❌ To be created | ✅ 26 thin pages + DataContext binding |
| `Vianigram.Agent` | ❌ To be created | ✅ Background task with push + sync |
| `Vianigram.SmokeTests` | ❌ To be created | ✅ Mirror of the shape of the browser sibling's SmokeTests |
| Siblings `vianium-{kernel,tls,http,net}` | ✅ Exists in the sibling repo | ✅ Resolved via project reference |

## Risks

1. **Project reference fragility** — if any `vianium-*` sibling changes the `vcxproj` shape or renames folders, Vianigram breaks. Mitigation: document the contract in `..\vianium-docs\native-port\06-integration.md` and a cross-repo CI gate.
2. **WP8.1 deprecation** — Microsoft does not support the SDK; some BCL APIs do not exist. Mitigation: test early on 512 MB-1 GB Windows Phone 8.1 hardware for each new context, parity with PivoraTelegram which is already validated.
3. **TL layer drift** — Telegram updates the layer (today 214); when rev 215+ appears, the codegen must be regenerated and breaking changes reviewed. Mitigation: `Api/V1/`/`V2/` versioning isolates consumers; the codegen tool regenerates deterministically.
4. **GC pressure from value type boxing** — a poorly-designed `Result<T, TError>` can box. Test with allocation benchmarks in Phase 7.
5. **Async/await context capture** — `.ConfigureAwait(false)` mandatory outside the UI; a Roslyn analyzer enforces it.
6. **`Vianigram.Composition` becomes a God class** — manual DI without a container, with 14+ contexts can explode. Mitigation: split by feature area (`CompositionRoot.Identity`, `CompositionRoot.Messaging`, `CompositionRoot.Media`, etc.).
7. **Multi-account state leak** — without rigorous OCAP, one session could read another's data. Mitigation: each `AccountIdentity` has its own `IObjectCapabilityProvider`; a smoke test verifies isolation.

## Where to read next

### Per-context docs

- [01-account.md](01-account.md) — `AccountIdentity`, phone/SMS/2FA SRP-2048/QR/multi-account.
- [02-chats.md](02-chats.md) — `Dialog`, dialog list, folders, archive.
- [03-messages.md](03-messages.md) — `MessageStream`, send/edit/delete/forward, reactions, optimistic UI.
- [04-contacts.md](04-contacts.md) — `ContactBook`, sync, blocked.
- [05-media.md](05-media.md) — `MediaTransfer`, parallel chunks, IBuffer zero-copy, thumbs.
- [06-sync.md](06-sync.md) — `SyncCursor`, pts/qts/seq/date, getDifference, channel diff.
- [07-calls.md](07-calls.md) — `CallSession`, voice/video signaling state machine.
- [08-secret-chats.md](08-secret-chats.md) — `SecretSession`, DH, key fingerprints, layer negotiation.
- [09-stickers.md](09-stickers.md) — `StickerLibrary`, packs, animated cache.
- [10-notifications.md](10-notifications.md) — `NotificationProfile`, toast/tile/badge, mute.
- [11-settings.md](11-settings.md) — `UserPreferences`.
- [12-search.md](12-search.md) — `SearchSession`, global + per-chat.
- [13-privacy.md](13-privacy.md) — `PrivacyProfile`, passcode, last-seen, sessions.
- [14-presentation.md](14-presentation.md) — XAML pages + ViewModels (inbound adapter).
- [15-shell-and-host.md](15-shell-and-host.md) — Composition root + native bridges + agent.

### Native side

- [../native-port/architecture-principles.md](../native-port/architecture-principles.md) — the C++ mirror of [principles.md](principles.md).
- [../native-port/00-overview.md](../native-port/00-overview.md) — native contexts.
- [../native-port/01-mtproto.md](../native-port/01-mtproto.md) — TCP framing, sessions, AES-IGE.
- [../native-port/02-tl.md](../native-port/02-tl.md) — TL serializer + codegen.
- [../native-port/03-crypto.md](../native-port/03-crypto.md) — Curve25519, SRP, AES-GCM, reused primitives.
- [../native-port/04-media.md](../native-port/04-media.md) — Opus, WebP, scaling.
- [../native-port/05-voip.md](../native-port/05-voip.md) — RTP, jitter buffer.

### Security policies

- [../security/tls-policy.md](../security/tls-policy.md) — TLS 1.3 Mozilla Modern (inherited from the `vianium-tls` sibling, a synchronized snapshot).
- [../security/mtproto-policy.md](../security/mtproto-policy.md) — MTProto 2.0 invariants (nonce uniqueness, server-salt rotation, msg_id ordering, replay window, 2FA SRP-2048, secret-chat key-fingerprint, perfect-forward-secrecy).
- [../security/at-rest-encryption.md](../security/at-rest-encryption.md) — `DataProtectionProvider` scope, key derivation, what is encrypted.

### Sibling repos

- `..\vianium-managed-kernel\docs\managed-architecture\principles.md` — the original source (C# managed kernel) that [principles.md](principles.md) adapts.
- `..\vianium-managed-kernel\docs\managed-architecture\00-overview.md` — the mirror overview of the managed kernel.
- `..\vianium-tls\docs\security\tls-policy.md` — the canonical TLS policy (lives in the `vianium-tls` sibling).

## Quick cross-link

- [principles.md](principles.md) — patterns, kernel, 6 non-negotiable rules, 9 enforced rules, 6 messenger-specific extensions, 13 performance rules.
- [../native-port/architecture-principles.md](../native-port/architecture-principles.md) — the C++ mirror.
- [../security/mtproto-policy.md](../security/mtproto-policy.md) — MTProto invariants.
- [../security/at-rest-encryption.md](../security/at-rest-encryption.md) — DPAPI scope.
- [../security/tls-policy.md](../security/tls-policy.md) — TLS inherits from the `vianium-tls` sibling.
