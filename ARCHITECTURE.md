# Architecture — Vianigram (one-pager)

One-page view. For the complete detail:

- Managed (C# WP8.1): [`docs/managed-architecture/00-overview.md`](docs/managed-architecture/00-overview.md)
  + [`docs/managed-architecture/principles.md`](docs/managed-architecture/principles.md).
- Native (C++/CX WinRT): [`docs/native-port/00-overview.md`](docs/native-port/00-overview.md)
  + [`docs/native-port/architecture-principles.md`](docs/native-port/architecture-principles.md).
- Security: [`docs/security/tls-policy.md`](docs/security/tls-policy.md),
  [`docs/security/mtproto-policy.md`](docs/security/mtproto-policy.md),
  [`docs/security/at-rest-encryption.md`](docs/security/at-rest-encryption.md).

## Pattern

**DDD + Hexagonal per bounded context**, identical to the pattern validated in
the `vianium-*` sibling repos (13 managed bounded contexts, 9 native). Every context has
a standardized internal structure:

- **Managed (C#)**: `Domain/`, `Application/`, `Ports/`, `Infrastructure/`,
  `Composition/`, `Api/V1/`.
- **Native (C++)**: `src/{domain,application,ports,infrastructure,api,internal}/`
  + public header `vng_<ctx>_api.h` with a flat C ABI (no C++ types,
  no exceptions, no `std::` crossing the ABI).

`Domain/` is pure: no `Windows.h`, no WinRT, no I/O, no frameworks.
`Application/` orchestrates use cases. `Ports/` declares inbound and
outbound interfaces. `Infrastructure/` implements the outbound ports
(`StreamSocket`, `SQLite`, `DataProtectionProvider`, etc.). `Api/V1/` is
the versioned public surface — `V1` never breaks; `V2` adds in
parallel.

## Non-negotiable rules

Inherited from the `vianium-kernel` sibling + 6 messenger-specific:

1. **A cross-context API is always Result/POD** (managed: `Result<T, Error>`;
   native: C ABI with `int32_t` error codes, no `bool`, no `std::function`,
   no `std::shared_ptr` crossing the ABI).
2. **Domain Event Bus from day 1** — every mutation emits an `IDomainEvent`.
3. **Two-layer capability model**: `ICapabilityRegistry` (global feature
   flag) + `IObjectCapabilityProvider` (per-consumer OCAP).
4. **Process isolation seam**: design as if the bounded contexts were
   already in separate processes. Opaque DTOs/handles, no shared
   mutable references.
5. **C ABI mandatory cross-context** in native (`vng_<ctx>_api.h` flat,
   error codes namespaced per context).
6. **API versioning** (`Api/V1`, `Api/V2`).
7. **Optimistic UI mandatory for sends** (messenger-specific).
8. **FLOOD_WAIT respected at the port boundary** (messenger-specific).
9. **Key material isolation**: only the `vianium-crypto` sibling may touch
   auth-key bytes (messenger-specific). Vianigram consumes it via WinMD.
10. **Message-history immutability post server ACK** (messenger-specific).
11. **Server-truth wins on sync conflicts** (messenger-specific).
12. **Sync state (pts/qts/seq/date) is an invariant of the Sync bounded context**
    (messenger-specific).

Anti-patterns that break these rules: `BrowserDatabase.Instance`-style
singletons, heavy code-behind in XAML, `using <other-context>` outside of
Composition, `async void` outside of event handlers, `.Result` / `.Wait()`,
exceptions crossing WinMD/C ABI, `bool` in a C ABI, god objects in `Domain/`,
XOR encryption.

## Bounded context inventory

| # | Context | Lang | Layer | Reuse Vianium siblings |
|---|---|---|---|---|
| - | Vianium.Core.Kernel | C++ | Native kernel | **Project reference** (sibling `vianium-kernel`) |
| - | Vianium.Core.Tls | C++ | Native infra | **Project reference** (sibling `vianium-tls`) |
| - | Vianium.Core.Http | C++ | Native infra | **Project reference** (sibling `vianium-http`) |
| - | Vianium.Core.Net | C++ | Native infra | **Project reference** (sibling `vianium-net`) |
| 01 | Vianigram.Core.MTProto | C# managed shell on top of sibling `vianium-mtproto` (`src\mtproto\`) | NEW (links `vianium-net` for TCP to the DC) |
| 02 | Vianigram.Core.Tl | Sibling repo `vianium-mtproto` (`src\tl\`) — Vianigram references it, contains no sources | NEW (C# codegen tool in `tools/tl-codegen/`) |
| 03 | Vianigram.Core.Crypto | Sibling repo `vianium-crypto` — Vianigram references it, contains no sources | Reuses `vianium-tls/src/crypto/{sha256,sha512,hkdf,hmac,x25519,aes_core,bignum}` |
| 04 | Vianigram.Core.Media | C++ | Native infra | NEW (Opus, WebP, image scale) |
| 05 | Vianium.VoIP | Sibling repo `vianium-voip` — Vianigram references the WinRT projection, contains no sources (legacy `Vianigram.Core.Voip` removed in vianigram cleanup; `vianium-voip` is canonical) | NEW (RTP, Opus, jitter; see also `vianium-voip\src\tgcalls\`) |
| K | Vianigram.Kernel | C# | Managed kernel | Structural clone of the C# managed kernel that lives in `vianium-managed-kernel\` |
| C | Vianigram.Composition | C# | DI root | Structural clone of the C# composition root from the `vianium-managed-kernel\` sibling |
| V | Vianigram.ViewModels | C# | Presentation infra | NEW (BaseViewModel, AsyncCommand) |
| 06 | Vianigram.Account | C# | App | NEW — phone+SMS+2FA+QR+multi-account |
| 07 | Vianigram.Chats | C# | App | NEW — dialogs, metadata |
| 08 | Vianigram.Messages | C# | App | NEW — history, send/edit/delete/forward |
| 09 | Vianigram.Contacts | C# | App | NEW — contact sync, blocked |
| 10 | Vianigram.Media | C# | App | NEW — up/download orchestration, gallery |
| 11 | Vianigram.Sync | C# | App | NEW — pts/qts/seq/date, getDifference |
| 12 | Vianigram.Calls | C# | App | NEW — voice/video signaling |
| 13 | Vianigram.SecretChats | C# | App | NEW — DH, fingerprints, encryption |
| 14 | Vianigram.Stickers | C# | App | NEW — packs, recently used, animated |
| 15 | Vianigram.Notifications | C# | App | NEW — toast, tile, badge |
| 16 | Vianigram.Settings | C# | App | NEW — typed preferences |
| 17 | Vianigram.Search | C# | App | NEW — global + per-chat |
| 18 | Vianigram.Privacy | C# | App | NEW — passcode, last-seen, sessions |
| 19 | Vianigram.Storage | C# | App infra | NEW — encrypted SQLite repos |
| P | Vianigram.App | C# | Presentation | NEW — XAML pages + ViewModels (26 pages) |
| A | Vianigram.Agent | C# | Background | NEW — push channel + sync |
| T | Vianigram.SmokeTests | C# | Test | Mirror of the SmokeTests from the `vianium-managed-kernel` sibling (`Clients\Vianigram.Browser.SmokeTests\`) |

## Reuse strategy with sibling repos

Vianigram **does not duplicate** transport or the native kernel. Project references by
relative path to the sibling repos of the `vianium` org:

- `..\vianium-kernel\Vianium.Core.Kernel.vcxproj` — Result/Maybe, Span/StringView, allocators
  (Arena/Pool/Heap), Clock, Logger, thread-safe EventBus, CapabilityRegistry,
  IObjectCapabilityProvider, Telemetry, TaskScheduler, CancellationToken,
  Containers, HResultMapper.
- `..\vianium-tls\Vianium.Core.Tls.vcxproj` — TLS 1.3 Mozilla Modern (does not apply to MTProto
  directly, but its crypto subset does: SHA-256/512, HKDF, HMAC, X25519,
  AES-core, bignum). The full TLS stack is used for HTTP/HTTPS to
  non-MTProto auxiliary services (CDN for HTTP downloads, etc.).
- `..\vianium-http\Vianium.Core.Http.vcxproj` — HTTP/1.1 + H2, connection pool, cookies,
  redirects, compression. Used by `Vianigram.Core.Media` for CDN
  downloads.
- `..\vianium-net\Vianium.Core.Net.vcxproj` — HSTS, pinning, OCSP, CT, DoH, socket
  primitives. The MTProto layer (sibling `vianium-mtproto`) uses the `socket_transport` from
  `vianium-tls/src/core/` as a reference for its own TCP wrapper
  (MTProto does not use TLS in its transport; it encrypts at its own level).

Structurally cloned (not a project reference; new code with an identical
shape):

- `Vianigram.Kernel` ← `Vianigram.Browser.Kernel` (the C# managed kernel that lives in `..\vianium-managed-kernel\`).
- `Vianigram.Composition` ← C# composition root from the `vianium-managed-kernel\` sibling.

PivoraTelegram remains a **read-only reference** only — to
understand protocol invariants and product flows. No code is ported.

## Layer diagram

```
┌──────────────────────────────────────────────────────────────────┐
│  Clients\Vianigram.App  (XAML pages, ViewModels)                 │
│  Clients\Vianigram.Agent  (background task)                      │
│  Clients\Vianigram.SmokeTests                                    │
└──────────────────────────────────────────────────────────────────┘
              │
              ▼
┌──────────────────────────────────────────────────────────────────┐
│  Vianigram.Composition  (DI root, ACL adapters)                  │
└──────────────────────────────────────────────────────────────────┘
              │
              ▼
┌──────────────────────────────────────────────────────────────────┐
│  C# Bounded Contexts (Account, Chats, Messages, Contacts,        │
│  Media, Sync, Calls, SecretChats, Stickers, Notifications,       │
│  Settings, Search, Privacy, Storage)                             │
│  Each one: Domain / Application / Ports / Infrastructure / Api/V1│
└──────────────────────────────────────────────────────────────────┘
              │
              ▼
┌──────────────────────────────────────────────────────────────────┐
│  Vianigram.Kernel  (Result, EventBus, Capability, Telemetry)     │
└──────────────────────────────────────────────────────────────────┘
              │ WinMD shims via Api/V1
              ▼
┌──────────────────────────────────────────────────────────────────┐
│  Vianigram.Core.Media  (C++/CX)                                  │
│  src/{domain,application,ports,infrastructure,                   │
│        api,internal} + vng_media_api.h                           │
│  (MTProto, Tl, Crypto, Voip extracted to sibling repos:          │
│   vianium-mtproto, vianium-crypto, vianium-voip)                 │
└──────────────────────────────────────────────────────────────────┘
              │ project references (sibling repos)
              ▼
┌──────────────────────────────────────────────────────────────────┐
│  ..\vianium-kernel\     ..\vianium-tls\                          │
│  ..\vianium-http\       ..\vianium-net\                          │
│  ..\vianium-crypto\     ..\vianium-mtproto\                      │
│  ..\vianium-voip\                                                │
└──────────────────────────────────────────────────────────────────┘
```
