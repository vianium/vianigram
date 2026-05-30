# Roadmap — Vianigram

Phases 0-7. Each phase has verifiable deliverables and explicit exit
criteria. A phase is not closed until all criteria are
verified (green build + smoke tests + manual acceptance where applicable).

The phases are sequential in general, but phases 1-3 allow for some
parallelism between native (C++) and managed (C#) if two hands are available.
Phases 4+ are sequential.

The tentative dates assume a single developer working full-time without
external blockers. Expected slippage on the order of 30-50% — recalibrate after Phase
1 with real data.

---

## Current alpha target — 0.1.2.0

**Status date:** 2026-05-30.

### Goal

Stabilize and package the first practical Windows Phone alpha path as a
`Release|ARM` APPX for the `v0.1.2.0` tag: sign in, open the chat list
quickly, read real chats, receive live updates, fetch media on intentional
user action, and connect through MTProxy before authentication when the
network requires it.

### In scope

- Package manifest, assembly versions, visible release text, and release
  notes aligned to `0.1.2.0`.
- Pre-login MTProxy access from welcome, phone-number, and QR-login flows.
- Telegram login paths kept active: phone/SMS, 2FA/SRP, and QR token polling.
- Dialog snapshot loading, avatar resolution, avatar disk cache, and stable
  generated placeholders for peers without photos.
- Message thumbnails, document fetch, and progressive audio/video buffering
  through the media bounded context.
- Media DC prewarm, imported authorization reuse, persisted DC options, and
  endpoint health so media fetches avoid repeating expensive bootstrap work.
- Background sync, live updates, foreground notifications, live tiles, and
  unread badges remain wired for alpha validation.
- QR-login compatibility smoke coverage and auth-key reuse policy
  documentation.

### Known limitations

- Full document and large media downloads must remain user-triggered; only
  lightweight previews/thumbnails should happen automatically.
- Closed-app notifications are still limited by the Windows Phone
  notification platform and can be delayed or unavailable.
- Calls have MTProto/update plumbing and a fallback poller, but the product
  UI is not release-complete yet.
- MTProxy needs real-device validation across direct, secret-based, and
  restricted-network scenarios before it can be considered fully complete.
- Secret chats, stickers, search, privacy, and settings still need product
  hardening beyond the current skeletons and ports.

### Release gates

- `Release|ARM` APPX build succeeds for `Vianigram.sln`.
- The generated APPX reports package identity version `0.1.2.0`.
- Latest verification: `MSBuild Vianigram.sln /p:Configuration=Release
  /p:Platform=ARM /m:1` generated
  `Vianigram.App_0.1.2.0_ARM.appx` on 2026-05-30.
- Smoke login with phone/SMS and QR on a physical device.
- Confirm MTProxy can be configured before sign-in and persists across app
  restarts.
- Confirm document/media downloads start only from a direct tap, while
  thumbnails/previews remain bounded.
- Confirm chat list cold start can paint from snapshot before network
  refresh.

---

## Phase 0 — Foundation

**Tentative timeline:** 1 week.

### Goal

Establish the project's complete chassis: solution file, architectural
documentation, managed kernel, composition root, project references to the
`vianium-*` sibling repos, templates for new bounded contexts, and a build
validation script. **Zero domain code in this phase** — documentation
and skeleton only.

### Deliverables

- `Vianigram.sln` with MSBuild 14.0, target WP8.1, toolset `v120_wp81`,
  Debug/Release × x86/ARM configurations.
- Project references to sibling repos:
  - `..\vianium-kernel\Vianium.Core.Kernel.vcxproj`
  - `..\vianium-tls\Vianium.Core.Tls.vcxproj`
  - `..\vianium-http\Vianium.Core.Http.vcxproj`
  - `..\vianium-net\Vianium.Core.Net.vcxproj`
- `Core\Vianigram.Kernel\` — structural clone of the C# managed kernel
  `Vianigram.Browser.Kernel` (which lives in `..\vianium-managed-kernel\`):
  Result/Maybe/Error, Clock, Logger, EventBus,
  CapabilityRegistry+WellKnown, ObjectCapabilityProvider, Telemetry,
  Persistence ports. Without browser-specifics.
- `Core\Vianigram.Composition\Roots\VianigramCompositionRoot.cs` —
  manual DI root with a shape cloned from `BrowserCompositionRoot`.
- `templates\managed-context.template\` — DDD+hex skeleton ready to
  copy (`Domain/Application/Ports/Infrastructure/Composition/Api/V1`).
- `templates\native-context.template\` — `src/{domain,application,ports,
  infrastructure,api,internal}/` + `vng_<ctx>_api.h` template.
- Complete documentation:
  - `docs\managed-architecture\principles.md` — adapted from the managed kernel
    sibling `vianium-managed-kernel` (864 lines) + 6 messenger-specific rules
    (optimistic-UI, FLOOD_WAIT respect, key material isolation,
    message-history immutability, server-truth wins on sync conflict,
    sync state invariant).
  - `docs\managed-architecture\00-overview.md` — bounded-context table
    and phase plan.
  - `docs\managed-architecture\01-account.md` … `15-shell-and-host.md`
    — stubs per bounded context.
  - `docs\native-port\architecture-principles.md` — clone of the
    native principles from the `vianium-kernel` sibling + `Result<T,Error>` everywhere,
    no exceptions cross-WinMD ABI, arena allocators, zero-copy `IBuffer`.
  - `docs\native-port\00-overview.md` — native context map.
  - `docs\native-port\01-mtproto.md` — TCP framing (Abridged/
    Intermediate/Full), AES-256-IGE, DH key exchange, multi-connection
    (all in the `vianium-mtproto` sibling).
  - `docs\native-port\02-tl.md` — schema codegen, layer flag (sibling
    `vianium-mtproto\src\tl\`).
  - `docs\native-port\03-crypto.md` — Curve25519, AES-IGE, AES-GCM,
    HKDF/PBKDF2-SHA512 in the `vianium-crypto` sibling; partial reuse of
    `vianium-tls/src/crypto/`.
  - `docs\native-port\04-media.md` — Opus, WebP, image scaling, video
    thumbs.
  - `docs\native-port\05-voip.md` — RTP, jitter buffer, Opus, opt H.264
    (sibling `vianium-voip`).
  - `docs\security\tls-policy.md` — verbatim copy of the TLS policy
    defined in the `vianium-tls` sibling
    (Mozilla Modern also applies to the Telegram API).
  - `docs\security\mtproto-policy.md` — MTProto 2.0 invariants (nonce
    uniqueness, server-salt rotation, msg_id ordering, replay window,
    SRP-2048, secret-chat fingerprints, PFS rekey).
  - `docs\security\at-rest-encryption.md` — DataProtectionProvider
    scope per data class, what is encrypted (auth keys, secret-chat root
    keys, optionally bodies behind a passcode).
- Root pointers: `README.md`, `ROADMAP.md`, `ARCHITECTURE.md`,
  `THIRD_PARTY_NOTICES.md`, `TRADEMARKS.md`, `LICENSE`, `NOTICE`,
  `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, `AUTHORS.md`.
- `build-validate.cmd` — wrapper over MSBuild 14.0 with path
  validation, default Debug/x86 configuration, dependency check of the
  sibling repos.

### Acceptance criteria

- `build-validate.cmd Debug x86` exit 0.
- `build-validate.cmd Release ARM` exit 0.
- `Vianigram.Kernel.dll` and `Vianigram.Composition.dll` generated.
- WinMDs of the `vianium-tls`, `vianium-http`, `vianium-net` siblings
  resolve correctly via project references (no broken `<HintPath>`
  entries).
- Each bounded-context doc has at least: bounded context (ubiquitous
  language, aggregates, events, capabilities), goal, phases (with exit
  criteria), cross-context dependencies, risks, crosslinks.
- 0 domain code (placeholders only in `Domain/` directories).

---

## Phase 1 — Native MTProto core

**Tentative timeline:** 2-3 weeks.

### Goal

Implement the native data plane: TL serialization, MTProto 2.0 transport
over TCP (without TLS — MTProto encrypts at its own level), crypto primitives.
Validate against the Telegram **test DC** (`149.154.167.40:443`). **Never against
production in automated tests.**

### Deliverables

- Sibling `vianium-mtproto\src\tl\` — C++ TL serializer/deserializer. Schema
  codegen tool `tools\tl-codegen\` that takes `tl\scheme-layer-214.tl` and
  emits `tl_layer_214.h/cpp`. The layer is a build flag. Vianigram consumes the
  resulting WinMD.
- Sibling `vianium-crypto\` — AES-256-IGE, AES-256-GCM,
  Curve25519/X25519, SHA-256/512, HKDF, HMAC, PBKDF2-SHA512, SRP-2048
  bignum. Reuse of `..\vianium-tls\src\crypto\`
  (sha256, sha512, hkdf, hmac, x25519, aes_core, bignum) via
  `<AdditionalIncludeDirectories>` or selective copy.
- Sibling `vianium-mtproto\src\mtproto\` — TCP framing (Abridged + Intermediate +
  Full), DH key exchange (`req_pq_multi`, `req_DH_params`,
  `set_client_DH_params`), session/salt management, msg_id monotonicity,
  replay window, multi-connection per DC with multiplexing by msg_id.
- C ABI: `vng_mtproto_api.h`, `vng_tl_api.h`, `vng_crypto_api.h` with
  namespaced error codes (ranges per context, e.g. 1xxx MTProto, 2xxx
  TL, 3xxx Crypto).
- WinMD shims published by the siblings: `Vianium.Mtproto.winmd`,
  `Vianium.Tl.winmd`, `Vianium.Crypto.winmd` — Vianigram consumes them.
- Vector tests:
  - AES-IGE NIST + Telegram-published test vectors.
  - DH primes per the Telegram MTProto spec.
  - SRP-2048 vectors (RFC 5054 test cases).
  - TL roundtrip of ~800 types of layer 214.
- `Clients\Vianigram.SmokeTests\TelegramHandshakeSmokeTest.cs` — replica
  of the `HowsMySslSmokeTest.cs` pattern validating the DH handshake against the test
  DC in < 2 s and an `auth.sendCode` round-trip.

### Acceptance criteria

- The SmokeTest sends `auth.sendCode` to the test DC (`149.154.167.40`) and receives
  `auth.sentCode` parsed correctly.
- AES-IGE vectors 100% pass.
- TL roundtrip: 1000+ randomly-generated messages, byte-equal after
  serialize → deserialize → serialize.
- Full DH handshake in < 2 s on 1 GB-class hardware.
- Zero exceptions crossing the WinMD ABI; all errors via
  `Result<T, Error>` in the public API.
- 0 cross-context includes except in the `vianium-mtproto` sibling, which
  consumes the `vianium-crypto` sibling (and its own `src\tl\`) through its public
  API.

---

## Phase 2 — Read messages MVP

**Tentative timeline:** 2 weeks.

### Goal

Login + dialog list + reading messages in real time. Clean rewrite
of `Vianigram.Account`, `Vianigram.Chats`, `Vianigram.Messages`,
`Vianigram.Sync`, reading `D:\Projects\2026\WP\PivoraTelegram\src\
PivoraTelegram.Core\` to understand invariants (not to port code).

### Deliverables

- `Core\Vianigram.Account\` — phone + SMS + 2FA SRP-2048 + QR login +
  multi-account. Aggregate `AccountIdentity`. Active sessions listable
  via `account.getAuthorizations`.
- `Core\Vianigram.Chats\` — dialog list, metadata. Aggregate
  `Dialog` (1:1, group, supergroup, channel).
- `Core\Vianigram.Messages\` — history, scroll, receive messages via the
  updates loop. Aggregate `MessageStream`.
- `Core\Vianigram.Sync\` — pts/qts/seq/date, `updates.getDifference`,
  channel diff. Aggregate `SyncCursor`. Sync state invariant enforced.
- ViewModels for LoginPage, DialogListPage, ChatPage (3 initial
  pages).

### Acceptance criteria

- Full login in production with a real account (phone + SMS, optional 2FA).
- The dialog list loads in < 2 s with a cold cache.
- Opening a 1:1 chat with 100+ historical messages: smooth scroll without
  freezes.
- Incoming messages appear live via the updates loop without a manual reload.
- Server-truth wins on sync conflicts (messenger-specific rule).
- 0 `.Result` / `.Wait()` in production code.

---

## Phase 3 — Storage + Contacts + Media download

**Tentative timeline:** 2 weeks.

### Goal

Local at-rest encrypted persistence, contact synchronization, download
and caching of media (photos, voice, stickers). Native context `Vianigram.Core.Media`
for Opus decode + WebP decode.

### Deliverables

- `Core\Vianigram.Storage\` — SQLite repos with at-rest encryption via
  `DataProtectionProvider`. Auth keys, secret-chat root keys, optionally
  message bodies behind a passcode.
- `Core\Vianigram.Contacts\` — `contacts.importContacts`,
  `contacts.getContacts`, blocked list. Aggregate `ContactBook`.
- `Core\Vianigram.Media\` — orchestration of the download path with parallel
  chunks (default 4, adaptive 1-8 depending on RTT and FLOOD_WAIT). Thumbnails,
  gallery cache. Aggregate `MediaTransfer`.
- `Core\Vianigram.Core.Media\` — Opus decoder, WebP decoder, image
  scaling (bilinear, SIMD where available), video thumbnail extraction.
  For call voice the `vianium-voip` sibling is also invoked (Opus
  encoder + jitter buffer).
- ViewModels for GalleryPage, ContactsPage.

### Acceptance criteria

- The app survives a restart with a complete local cache (dialog list,
  the last N messages per chat, contacts).
- Contacts synchronize in < 5 s with incremental sync.
- JPG/WebP photos render from cache without re-downloading.
- Voice (Opus) plays back from cache.
- Sticker pack visible and individual stickers render.
- Total memory with 50 chats open: < 100 MB resident.

---

## Phase 4 — Send + Notifications

**Tentative timeline:** 2 weeks.

### Goal

Close the bidirectional loop: sending messages (text, media, voice) with
optimistic UI; upload path with parallel chunks; toast/tile/badge
notifications; background agent for push and sync.

### Deliverables

- Send path in `Vianigram.Messages`: text, photo, voice, document,
  forward, edit, delete. Optimistic UI (< 50 ms render, reconciled with the
  `messages.sendMessage` response). Message-history immutability post
  server ACK.
- Upload path in `Vianigram.Media`: 4-8 parallel chunks,
  adaptive chunk size 64 KB → 1 MB, FLOOD_WAIT respect at the port boundary.
- `Core\Vianigram.Notifications\` — toast, tile (count), badge, per-chat
  mute. Aggregate `NotificationProfile`.
- `Clients\Vianigram.Agent\` — background task for the push channel and sync
  when the app is suspended. Rewrite using `PivoraTelegram.Agent` as a
  reference for invariants.

### Acceptance criteria

- Round-trip send for all media types (text, photo 5 MB, voice
  10 s, document 20 MB, forward, reply).
- Uploading a 5 MB photo completes in < 8 s on LTE.
- Toast on incoming when the app is suspended, latency < 5 s from the server.
- Badge updates correctly on the lock screen.
- FLOOD_WAIT signal propagated correctly; backoff applied;
  retry without a retry-storm.

---

## Phase 5 — Calls + SecretChats + Stickers

**Tentative timeline:** 3 weeks.

### Goal

Heavy features: voice (with the Opus codec + RTP + jitter buffer), secret chats
(DH, key fingerprints, PFS rekey), animated stickers.

### Deliverables

- `Core\Vianigram.Calls\` — signaling, UI state. Aggregate `CallSession`.
- Sibling `vianium-voip\` — RTP, jitter buffer, Opus encode/decode,
  optional H.264 for video calls (future phase, behind a feature flag).
  For layer 92 (libtgvoip-equivalent) the legacy path lives in
  `vianium-voip\third_party\libtgvoip\`; for tgcalls v2 the modern path
  lives in `vianium-voip\src\tgcalls\`. Vianigram consumes the WinMD; it
  contains no VoIP sources of its own.
- `Core\Vianigram.SecretChats\` — DH establishment, key fingerprints,
  AES-256-IGE message encryption with derived keys, PFS rekey every
  N messages / T time. Aggregate `SecretSession`.
- `Core\Vianigram.Stickers\` — packs, recently used, animated cache
  (TGS/TGV). Aggregate `StickerLibrary`.

### Acceptance criteria

- 1:1 voice call to another Telegram client: setup < 3 s, clean audio without
  perceptible drops on stable LTE.
- Secret chat established with another Telegram client; the visual key
  fingerprint (QR + emoji) matches between both ends.
- PFS rekey occurs transparently to the user.
- Animated sticker (Lottie/TGS) plays back at the target frame rate.
- Sticker pack import + recently-used tracking.

---

## Phase 6 — Privacy + Settings + Search + UI port

**Tentative timeline:** 2-3 weeks.

### Goal

Remaining product features + a complete port of PivoraTelegram's 26 XAML pages
to the Vianigram shape (ViewModels + DDD + Composition). Full functional
parity with PivoraTelegram at the close of this phase.

### Deliverables

- `Core\Vianigram.Privacy\` — passcode, last-seen, blocked, active
  sessions. Aggregate `PrivacyProfile`.
- `Core\Vianigram.Settings\` — typed preferences with
  `PreferenceKey<T>`, schema versioning. Aggregate `UserPreferences`.
- `Core\Vianigram.Search\` — global search + per-chat search. Aggregate
  `SearchSession`.
- `Clients\Vianigram.App\` — 26 XAML pages rewritten with the shape of
  `D:\Projects\2026\WP\PivoraTelegram\src\PivoraTelegram.App\Pages\`
  as a reference (not a copy). Thin pages (< 30 lines of code-behind);
  all logic in ViewModels.
- Custom controls port (16+ controls).

### Acceptance criteria

- Functional parity with PivoraTelegram (a verifiable checklist of the
  26 pages).
- Passcode lock/unlock works; bodies encrypted behind a passcode.
- Last-seen privacy respects `Everybody/Contacts/Nobody`.
- Global search returns results for messages + chats + users.
- Active session visible in other Telegram clients with the label
  "Vianigram".

---

## Phase 7 — Performance audit

**Tentative timeline:** 1 week.

### Goal

Validate that the 13 performance principles documented in `principles.md`
are measurable and within budget. ETW traces, memory profiling,
parallel-download tuning, cold-start optimization.

### Deliverables

- ETW trace report for cold start, dialog list load, scroll, send,
  receive.
- Memory profiling report (resident peak, GC pressure, native arena
  utilization).
- Parallel-download tuning: measure 1/2/4/6/8 parallel chunks on LTE,
  3G, WiFi; pick the adaptive optimum.
- FLOOD_WAIT backoff verification: simulate 420 responses; validate that
  there is no retry-storm.
- Voice-call setup time measurement.
- Smoke regression suite run on a physical 1 GB device.

### Acceptance criteria (perf gates)

- Cold start < 2 s on 1 GB-class hardware.
- Resident memory < 120 MB with 50 chats open.
- Voice-call setup < 3 s.
- Chat list scroll: sustained 60 fps on a list of 100+ dialogs.
- Send optimistic UI < 50 ms.
- 5 MB photo upload completes < 8 s on LTE.
- Connection pool per DC: 4 sockets default, correct multiplexing;
  single-socket fallback on low RAM.
- 0 `.Result` / `.Wait()` in hot paths (verified by grep in CI).
- Encryption at rest verified for auth keys and secret-chat root keys
  (test: dump `LocalState` and verify there is no plaintext).

---

## Cross-phase invariants

Apply in ALL phases, non-negotiable:

- **Green build**: `build-validate.cmd Debug x86` and `Release ARM` must
  pass at the close of each phase.
- **0 cross-context using statements** outside of Composition adapters.
- **0 exceptions across WinMD ABI** — always `Result<T, Error>`.
- **0 `byte[]` marshal** in the file/media path — `IBuffer` only.
- **0 XOR encryption** — `DataProtectionProvider` only.
- **English in code**, Spanish-first in docs and commit messages.
- **Domain Event Bus** from day 1 — every mutation emits an `IDomainEvent`.
- **Smoke tests against test DCs** only (149.154.175.10,
  149.154.167.40); production only for manual acceptance.
