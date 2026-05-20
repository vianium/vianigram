# Vianigram
[![License: PolyForm Noncommercial 1.0.0](https://img.shields.io/badge/License-PolyForm_Noncommercial_1.0.0-orange.svg)](LICENSE) [![Vianium](https://img.shields.io/badge/org-vianium-00A0F0.svg)](https://github.com/vianium) [![Issues](https://img.shields.io/github/issues/vianium/vianigram.svg)](https://github.com/vianium/vianigram/issues)

> Telegram, native-fast.

Created and maintained by [Angel Careaga](https://github.com/AngelCareaga).

Vianigram is a native Telegram client for Windows Phone 8.1, written from
scratch on top of the Vianium foundation and protocol libraries. The data
plane (MTProto 2.0, TL, crypto, media, VoIP) is C++ with arena allocators
and zero-copy buffers. The product layer (use cases, ViewModels, XAML) is
C# WP8.1 with DDD + Hexagonal architecture per bounded context.

This is a **clean-room** implementation of the publicly-documented
[MTProto 2.0 protocol](https://core.telegram.org/mtproto). See
[`TRADEMARKS.md`](TRADEMARKS.md) for the affiliation notice.

## Repo naming

> The repo is named `vianigram` (lowercase, no `vianium-` prefix) because
> the product brand "Vianigram" predates the Vianium org migration. The
> internal solution file remains `Vianigram.sln`, the C# namespaces stay
> `Vianigram.*`, and the brand color `#00A0F0` and logo are unchanged.
> See [ADR-0003](https://github.com/vianium/vianium-docs/blob/main/adr/0003-vianigram-name.md)
> for the decision record.

## Why it exists

`PivoraTelegram` (the predecessor) was a functionally complete Telegram
client (26 pages, MTProto 2.0 layer 214, voice, secret chats, multi-account)
but architecturally compromised: god-object `TelegramClient`, singleton
`DatabaseManager`, single-socket TCP per DC, `PivoraHTTP` hand-rolled
without reuse, key material in plaintext. Vianigram is the clean rewrite.
PivoraTelegram remains as a read-only reference to understand protocol
invariants and product flows; no code is ported.

The Vianium foundation already solved transport: TLS 1.3 Mozilla Modern,
HTTP/1.1+H2 with connection pool, HSTS + pinning + OCSP + CT + DoH, C++
kernel with `Result<T>` and arena allocators. Vianigram **references**
those `.vcxproj` files by relative path. Zero duplication; fixes in TLS
flow automatically.

## Target

- **Platform**: Windows Phone 8.1, AppContainer.
- **Toolchain**: MSBuild 14.0, Visual Studio 2013/2015 with WP 8.1 SDK,
  platform toolset `v120_wp81`.
- **Hardware**: 512 MB-1 GB Windows Phone 8.1 hardware (512 MB - 1 GB RAM, Snapdragon
  S4/400). Every design decision is validated against this budget.
- **Languages**: C++ 11/14 (the toolset doesn't support C++17+) in
  native contexts; C# 6 WP8.1 (no `record`, no NRT, no `init`) in
  managed contexts.

## Vianium dependencies

Vianigram consumes the following sibling repos via project reference
(`..\<sibling>\<Project>.vcxproj` / `.csproj`):

**Foundation (Apache-2.0):**
- `vianium-kernel` C++ kernel (`Result<T>`, arena, telemetry primitives)
- `vianium-managed-kernel` managed kernel projection
- `vianium-crypto` AES / SHA / bignum / RSA primitives
- `vianium-tls` TLS 1.3 Mozilla Modern
- `vianium-net` socket pool / DC routing layer
- `vianium-http` HTTP/1.1 + H/2 with connection pool

**Protocols (Apache-2.0):**
- `vianium-mtproto` MTProto 2.0 transport + TL schema (layer 214)
- `vianium-voip` VoIP signaling + reflector framing + `libtgvoip` host

The native crypto / Tl / MTProto / VoIP libraries previously lived under
`Core/Vianigram.Core.{Crypto,Tl,MTProto}` and `Core/{VianiumVoIP,
Vianium.Tgcalls,libtgvoip}`. They were extracted to their own Vianium
sibling repos during the migration and are now reached by relative path
from this solution.

## Configuration

Vianigram needs Telegram application credentials (`api_id` + `api_hash`)
issued at <https://my.telegram.org>. Telegram's Terms of Service forbid
publishing these credentials, so the committed copy of
`Core/Vianigram.Composition/Configuration/TelegramAppConfig.cs` ships
with placeholders that fail the auth_key handshake at runtime. Real
credentials live in a `TelegramAppConfig.Local.cs` file that is
**gitignored**.

To configure your fork:

1. Register an app at <https://my.telegram.org>.
2. Copy the template:
   ```cmd
   cd Core\Vianigram.Composition\Configuration
   copy TelegramAppConfig.Local.cs.example TelegramAppConfig.Local.cs
   ```
3. Edit `TelegramAppConfig.Local.cs` and uncomment the two lines
   setting `_apiId` and `_apiHash` to the values Telegram issued.
4. Verify the file is excluded from git (only matters if you cloned and
   intend to commit):
   ```cmd
   git check-ignore -v Core\Vianigram.Composition\Configuration\TelegramAppConfig.Local.cs
   ```
   Output should reference `.gitignore: **/TelegramAppConfig.Local.cs`.

Without a `TelegramAppConfig.Local.cs`, the build still succeeds (the
project file uses `Condition="Exists(...)"` for the conditional
include), but the client cannot authenticate to Telegram. That is the
expected behavior in any public fork until each fork registers its own
app and supplies its own credentials.

See [ADR-0004](https://github.com/vianium/vianium-docs/blob/main/adr/0004-native-tls-winrt-projection.md)
for the broader native/managed configuration story across the Vianium
ecosystem.

## Build

From a Developer Command Prompt for VS2013 (MSBuild 14.0):

```cmd
cd D:\path\to\vianigram
build-validate.cmd Debug x86      :: or Release ARM, etc.
```

The script validates that all required sibling repos exist (see
`build-validate.cmd` for the full list) and builds `Vianigram.sln` with
the requested configuration.

Equivalent manual invocation:

```cmd
MSBuild Vianigram.sln /p:Configuration=Debug /p:Platform=x86
MSBuild Vianigram.sln /p:Configuration=Release /p:Platform=ARM
```

## Layout

```text
vianigram/
  Vianigram.sln
  README.md ROADMAP.md ARCHITECTURE.md THIRD_PARTY_NOTICES.md TRADEMARKS.md
  LICENSE NOTICE SECURITY.md CONTRIBUTING.md CODE_OF_CONDUCT.md AUTHORS.md
  build-validate.cmd
  docs/
    managed-architecture/   principles + 15 bounded-context docs
    native-port/            principles + native context docs
    security/               tls-policy, mtproto-policy, at-rest-encryption
  templates/
    managed-context.template/
    native-context.template/
  Clients/
    Vianigram.App/                  XAML pages + ViewModels
    Vianigram.App.BackgroundTasks/  background task (push, sync, VoIP)
    Vianigram.SmokeTests/           tests against Telegram test DCs
    Vianigram.SmokeRunner.App/      smoke-test host
  Core/
    Vianigram.Kernel/        managed kernel (Result, EventBus, Capability)
    Vianigram.Composition/   manual DI composition root
    Vianigram.{Account,Chats,Messages,Contacts,Media,Sync,Calls,
               SecretChats,Stickers,Notifications,Settings,Search,
               Privacy,Storage}/   managed bounded contexts (C#)
    Vianigram.Core.Media/    C++/CX media WinRT projection
                             (Vianigram references Vianium.VoIP from
                             sibling repo vianium-voip directly; no
                             local voip wrapper)
  tools/
    tl-codegen/              C# build-time tool: scheme.tl -> tl_layer_NNN.h/cpp
```

## Architecture

DDD + Hexagonal per bounded context, the same pattern as the rest of the
Vianium portfolio. Each context exposes
`Domain/Application/Ports/Infrastructure/Composition/Api/V1` (managed) or
`src/{domain,application,ports,infrastructure,api,internal}/` (native).
No cross-context `using` outside of Composition. A flat C ABI
(`vng_<ctx>_api.h` with namespaced error codes) is mandatory at native
cross-context boundaries.

One-page view: [`ARCHITECTURE.md`](ARCHITECTURE.md). Deep dives in
[`docs/managed-architecture/00-overview.md`](docs/managed-architecture/00-overview.md)
and [`docs/native-port/00-overview.md`](docs/native-port/00-overview.md).

## Status

**v0.1.0** initial public release of the Vianium-orchestrated source tree.

The first public tag captures the state at the end of Phase 4 of the
internal roadmap. See [`ROADMAP.md`](ROADMAP.md) for what's done and what
ships next.

## Performance principles (non-negotiable)

1. MTProto serialization is C++. Managed never sees TL bytes.
2. AES-IGE and AES-GCM are C++. Implementation comes from `vianium-crypto`.
3. Connection pool per DC (4 sockets default, multiplexed by `msg_id`).
4. Parallel chunked file ops (4-8 in flight, adaptive 64 KB to 1 MB).
5. Zero-copy data plane via `Windows.Storage.Streams.IBuffer`.
6. Optimistic UI for sends (< 50 ms render, reconciled with server ACK).
7. Incremental sync (`pts`/`qts`/`seq`/`date`; `getDifference` only on gap).
8. `FLOOD_WAIT` honored at port boundary, never with a silent `try/catch`.
9. Memory budget < 120 MB resident, cold start < 2 s.
10. No `.Result` / `.Wait()`. `await ... .ConfigureAwait(false)` at boundaries.

Detail in `docs/managed-architecture/principles.md`.

## License

PolyForm Noncommercial 1.0.0 (source-available, non-commercial).

Vianigram itself ships under [PolyForm Noncommercial 1.0.0](LICENSE).
The Vianium foundation and protocol libraries it depends on ship under
Apache-2.0. See [`NOTICE`](NOTICE) for copyright notices and
[`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md) for vendored
third-party attribution (notably `libtgvoip`, OpenSSL-derived crypto,
and SQLite).

### Commercial use

Commercial use is **not** granted by the PolyForm Noncommercial license.
If you represent a company, or want to integrate this code into a
commercial product, a paid service, or an internal corporate system,
contact **[hello@angelcareaga.com](mailto:hello@angelcareaga.com)** to
arrange a commercial license.

## Trademarks

"Telegram" and "MTProto" are trademarks of Telegram FZ-LLC. Vianigram
is an independent, unofficial client implementation. See
[`TRADEMARKS.md`](TRADEMARKS.md).

## Contributing

Contributions follow the standard Vianium flow: open an issue first
for non-trivial work, sign your commits with the DCO (`git commit -s`),
and target small, reviewable PRs. See [`CONTRIBUTING.md`](CONTRIBUTING.md)
for the full process and code-style notes.

## Security

For security-sensitive issues (auth-key handling, MTProto framing,
privacy leaks, secret-chat KDF, MTProxy obfuscation) please follow
the responsible-disclosure path in [`SECURITY.md`](SECURITY.md). Do
**not** open a public issue.

## Support this project

Vianium is maintained by [Angel Careaga](https://angelcareaga.com) as a
personal open-source effort. If `vianigram` is useful to you, please
consider supporting future work:

- 💬 **Community**: join the Vianium Discord — https://discord.gg/NccbAK6jeb
- 💖 **[GitHub Sponsors](https://github.com/sponsors/vianium)** — recurring or one-time
- ☕ **[Buy Me a Coffee](https://www.buymeacoffee.com/soyangelcareaga)** — one-time tip, no account needed
- 🌐 **[angelcareaga.com](https://angelcareaga.com)** — contact, consulting

Detailed channels and a transparency page live in
[`SUPPORT.md`](SUPPORT.md) and
[vianium-docs/donations.md](https://github.com/vianium/vianium-docs/blob/main/donations.md).

## Author

Copyright (c) 2026 Angel Careaga <hello@angelcareaga.com>
[@AngelCareaga](https://github.com/AngelCareaga) /
[angelcareaga.com](https://angelcareaga.com).
