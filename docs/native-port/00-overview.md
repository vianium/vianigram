# Vianigram Native Port — Context Map and Build Order

**Status:** active · **Last reviewed:** 2026-04-27

> Required prior reading:
> - [architecture-principles.md](architecture-principles.md) — toolchain, DDD+hex skeleton, WinMD ABI rules, memory budgets, reuse of the `vianium-*` sibling repos.
> - `..\vianium-docs\native-port\architecture-principles.md` — the principles Vianigram inherits.
> - `..\vianium-docs\native-port\rust-migration-readiness.md` — C ABI FFI discipline; applies from the first line.

This document is the map of the **9 native bounded contexts** that make up Vianigram's data plane: four siblings reused without modification (`vianium-kernel`, `vianium-tls`, `vianium-http`, `vianium-net`), four new siblings (`vianium-crypto`, `vianium-mtproto` with its `src\tl\` and `src\mtproto\` subcomponents, `vianium-voip`), and one (`Vianigram.Core.Media`) that lives in this repo.

---

## 1. Inventory of native contexts

### Reused (sibling repos under `..\`)

| Context | Role | Reuse mechanism | Owner |
|---------|-----|---------------------|-------|
| `Vianium.Core.Kernel` (sibling `vianium-kernel`) | Result/Maybe/Error, IAllocator, IClock, ILogger, IEventBus, ITelemetry, ICancellationToken, WinRT bridge helpers | Project reference + `<AdditionalIncludeDirectories>` to `include/`. Static lib link. | `vianium-kernel` |
| `Vianium.Core.Tls` (sibling `vianium-tls`) | Crypto primitives (sha256, sha512, hkdf, hmac, x25519, aes_core, aes_gcm, bignum) + optional TLS for auxiliary channels (not MTProto) | (a) Source-level inclusion of `src/crypto/*.cpp` in the `vianium-crypto` sibling. (b) WinMD consumption from managed for CDN HTTPS. | `vianium-tls` |
| `Vianium.Core.Http` (sibling `vianium-http`) | HTTP/1.1+H2 client with connection pool. Used for CDN downloads (photos/files via HTTPS, paths auxiliary to the main MTProto). | WinMD consumption from managed. | `vianium-http` |
| `Vianium.Core.Net` (sibling `vianium-net`) | Socket transport, HSTS, pinning, DoH. `SocketTransport` in particular serves as the base adapter for the raw TCP that MTProto needs. | (a) WinMD consumption from managed. (b) An adapter inside the `vianium-mtproto` sibling in `src/infrastructure/transport/` wraps `Vianium::Core::Net::v1::SocketTransport^` for the MTProto flow. | `vianium-net` |

### New (`vianium-*` siblings + one local context)

| Context | Ubiquitous language | Aggregate root | Events | Hot memory |
|---------|-----------------|----------------|---------|-------------|
| `vianium-crypto` (sibling) | auth_key, msg_key, server_salt, root_key, fingerprint, dh_session, srp_session | `CryptoSession` (state per channel or secret-chat) | `KeyDerived`, `FingerprintComputed`, `IntegrityCheckFailed` | 2 MB |
| `vianium-mtproto\src\tl\` (sibling) | constructor_id, boxed_type, bare_type, vector, flag_field, layer | `TlSchema` (registry of constructors per layer) | `SchemaLoaded`, `UnknownConstructor` | 4 MB |
| `vianium-mtproto\src\mtproto\` (sibling) | dc, connection, session, salt, msg_id, seq_no, transport, container, rpc_call | `MTProtoSession` | `HandshakeCompleted`, `RpcDispatched`, `BadServerSalt`, `FloodWait`, `Reconnected` | 12 MB |
| `Vianigram.Core.Media` (local) | opus_frame, webp_image, thumbnail, video_keyframe, sticker | `MediaDecoder` | `DecodeStarted`, `DecodeCompleted`, `DecodeFailed`, `ThumbnailReady` | 24 MB |
| `vianium-voip` (sibling; includes `src\tgcalls\` and `third_party\libtgvoip\`) | rtp_packet, jitter_buffer, codec_session, stun_candidate, voip_session | `VoipSession` | `Connected`, `Disconnected`, `Muted`, `RemoteAudioFrame`, `BandwidthShifted` | 8 MB |

**Total Vianigram hot memory (5 new contexts):** ~50 MB. Adding the ~10 MB combined of the reused TLS/HTTP/NET/KERNEL siblings (which already live in the process when they coexist), Vianigram at peak with an active call fits within the WP8.1 user-process budget on a 512 MB ARMv7 device.

---

## 2. Dependency graph

```
                    vianium-kernel  (static lib)
                            ▲
            ┌───────────────┼───────────────┐
            │               │               │
     vianium-tls       vianium-net      (other siblings)
     (reused: crypto  (reused: socket
      primitives)      transport)
            ▲               ▲
            │               │
            │   ┌───────────┘
            │   │
     vianium-crypto      vianium-mtproto      Vianigram.Core.Media (local)
            ▲          (includes src\tl\,           ▲
            │           src\mtproto\)               │
            └─────────────┬─────────────┐          │
                          │             │          │
                  consume crypto + tl    vianium-voip
                                         (includes src\tgcalls\,
                                          third_party\libtgvoip\;
                                          consumes Media for the
                                          shared Opus codec)
```

### Detailed dependency rules

- Sibling `vianium-crypto` depends on:
  - `vianium-kernel` (Result, allocators, RNG via port).
  - Source-level: `..\vianium-tls\src\crypto\{sha256,sha512,hkdf,hmac,x25519,aes_core,aes_gcm,bignum}.{h,cpp}`.
  - **Does NOT** depend on Tl, MTProto, Media, Voip.
- Sibling `vianium-mtproto\src\tl\` depends on:
  - `vianium-kernel`.
  - **Does NOT** depend on any other Vianium or Vianigram context.
- Sibling `vianium-mtproto\src\mtproto\` depends on:
  - `vianium-kernel`.
  - `vianium-crypto` (via `vianium/crypto/api/v1/crypto_api.h`).
  - The sibling subcomponent `vianium-mtproto\src\tl\` (via `vianium/tl/api/v1/tl_api.h`).
  - `vianium-net` (WinMD for `SocketTransport`).
- `Vianigram.Core.Media` (local) depends on:
  - `vianium-kernel`.
  - **Does NOT** depend on Crypto, Tl, MTProto, Voip.
- Sibling `vianium-voip` depends on:
  - `vianium-kernel`.
  - `Vianigram.Core.Media` (shared Opus codec).
  - `vianium-crypto` (AES-256-CTR for RTP payload encryption).

**Anti-pattern detectable by a CI gate:**
- Includes of `..\vianium-mtproto\src\domain\` from outside the sibling itself.
- Any Vianigram context that does `#include "..\vianium-<X>\src\..."` except the list permitted in `architecture-principles.md` §11.

---

## 3. Build order

Mandatory sequencing (respects the dependency graph):

| Order | Project | ROADMAP Phase | Type |
|-------|----------|---------------|------|
| 1 | Sibling `vianium-kernel` | (pre-existing) | Static lib |
| 2 | Sibling `vianium-tls` | (pre-existing) | WinRT Component |
| 3 | Sibling `vianium-http` | (pre-existing) | WinRT Component |
| 4 | Sibling `vianium-net` | (pre-existing) | WinRT Component |
| 5 | Sibling `vianium-crypto` | Phase 1 | WinRT Component |
| 6 | Sibling `vianium-mtproto\src\tl\` | Phase 1 (parallel to Crypto) | part of the `vianium-mtproto` WinRT Component |
| 7 | Sibling `vianium-mtproto\src\mtproto\` | Phase 1 | part of the `vianium-mtproto` WinRT Component |
| 8 | `Vianigram.Core.Media` (local) | Phase 3 | WinRT Component |
| 9 | Sibling `vianium-voip` | Phase 6 | WinRT Component |

Phase 1 can parallelize **`vianium-crypto` + `vianium-mtproto\src\tl\`** (independent); the `src\mtproto\` subcomponent needs both closed. Phase 3 (Media) and Phase 6 (Voip) are sequential per the ROADMAP timeline, not by technical dependency.

---

## 4. Detailed reuse map — sibling `vianium-*` → Vianigram

### 4.1. Sibling `vianium-kernel`

Headers consumed by **all** the native Vianigram contexts:

| Header | Used by | What for |
|--------|-----------|----------|
| `vianium/kernel/result.h` | all | `Result<T, E>` in domain + application |
| `vianium/kernel/maybe.h` | all | `Maybe<T>` for optionals |
| `vianium/kernel/error.h` | all | error base with code + message + cause chain |
| `vianium/kernel/span.h` | all | non-owning views over buffers |
| `vianium/kernel/string_view.h` | all | strings without alloc |
| `vianium/kernel/alloc/i_allocator.h` | all | injected IAllocator |
| `vianium/kernel/alloc/arena_allocator.h` | MTProto, Tl, Media | per-request scratch |
| `vianium/kernel/alloc/pool_allocator.h` | MTProto (message slots), Voip (RTP packets) | fixed-size objects |
| `vianium/kernel/log/i_logger.h` | all | context-prefixed macro pattern |
| `vianium/kernel/time/i_clock.h` | MTProto (msg_id), Voip (jitter, pacing), Crypto (DH timestamp) | Now, MonotonicMs |
| `vianium/kernel/concurrency/i_cancellation_token.h` | MTProto, Voip, Media | cancel propagation from managed |
| `vianium/kernel/events/i_event_bus.h` | all | domain events |
| `vianium/kernel/telemetry/i_telemetry.h` | all | counters, histograms, gauges |
| `vianium/kernel/interop/winrt_bridge.h` | all the `winrt_shim.cpp` files | IBuffer ↔ Span, Error ↔ HRESULT |
| `vianium/kernel/interop/hresult.h` | all the `winrt_shim.cpp` files | error code mapping |

Source link: since the `vianium-kernel` sibling is a **static lib**, Vianigram links it directly without WinRT overhead.

### 4.2. `..\vianium-tls\src\crypto\`

Compiled source-level inside the `vianium-crypto` sibling (exact list in [03-crypto.md](03-crypto.md) §3):

```xml
<!-- Vianium.Crypto.vcxproj fragment (in the vianium-crypto sibling) -->
<ItemGroup>
  <ClInclude Include="..\vianium-tls\src\crypto\sha256.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\sha256.cpp" />
  <ClInclude Include="..\vianium-tls\src\crypto\sha512.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\sha512.cpp" />
  <ClInclude Include="..\vianium-tls\src\crypto\hkdf.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\hkdf.cpp" />
  <ClInclude Include="..\vianium-tls\src\crypto\hmac.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\hmac.cpp" />
  <ClInclude Include="..\vianium-tls\src\crypto\x25519.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\x25519.cpp" />
  <ClInclude Include="..\vianium-tls\src\crypto\aes_core.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\aes_core.cpp" />
  <ClInclude Include="..\vianium-tls\src\crypto\aes_gcm.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\aes_gcm.cpp" />
  <ClInclude Include="..\vianium-tls\src\crypto\bignum.h" />
  <ClCompile Include="..\vianium-tls\src\crypto\bignum.cpp" />
</ItemGroup>
```

Same binary, same bug fix, same self-test at DEBUG startup. When upstream `vianium-tls` adds Ed25519 or improved RSA-PSS, Vianigram inherits it in the next build via the `vianium-crypto` sibling's WinMD.

### 4.3. Sibling `vianium-net`

Vianigram managed consumes `Vianium.Net.winmd`. The main path is for **auxiliary** channels (DoH, smoke tests, telemetry). For MTProto, the `vianium-mtproto\src\infrastructure\transport\` sibling defines an `MTProtoTcpAdapter` adapter that implements `vianium::mtproto::ports::outbound::ITcpTransport` wrapping the `Vianium::Core::Net::v1::SocketTransport^` ref class:

```cpp
// vianium-mtproto/src/infrastructure/transport/mtproto_tcp_adapter.h
namespace vianium::mtproto::infrastructure {

class MTProtoTcpAdapter : public ports::outbound::ITcpTransport {
public:
    explicit MTProtoTcpAdapter(Vianium::Core::Net::v1::SocketTransport^ inner);
    Result<void, MtProtoError> Connect(const char* host, int port) override;
    Result<size_t, MtProtoError> Send(Span<const uint8_t> data) override;
    Result<size_t, MtProtoError> Recv(Span<uint8_t> out) override;
    void Close() override;
private:
    Platform::Agile<Vianium::Core::Net::v1::SocketTransport^> m_inner;
};

} // namespace
```

`SocketTransport` already manages reconnect, timeouts, telemetry. The `vianium-mtproto` sibling reuses that maturity instead of hand-rolling another socket layer.

### 4.4. Sibling `vianium-tls` (optional WinMD consumption)

For auxiliary HTTPS (downloading assets that do not go through the MTProto file API), the Vianigram managed layer consumes `Vianium.Tls.winmd::TlsStream` indirectly via the `vianium-http` sibling. Vianigram does not construct `TlsStream` directly — always via `HttpClient`.

### 4.5. Sibling `vianium-http` (optional WinMD consumption)

The Vianigram managed layer uses `Vianium.Http.HttpClient` (sibling `vianium-http`) for:
- Public CDN downloads (telegram.org assets, sticker thumbnails without an AccessHash, etc.).
- Push update endpoints (where applicable).
- External smoke tests.

It is NOT used for the main MTProto. MTProto lives on raw TCP + `Vianium::Core::Net::SocketTransport` of the `vianium-net` sibling.

---

## 5. C ABI surface per context

Each context exposes **one and only one** cross-context SoT: the flat C ABI header. Listing:

| Context | C ABI header | Error code range |
|---------|--------------|--------------------|
| Sibling `vianium-crypto` | `include/vianium/crypto/api/v1/crypto_api.h` | 3000-3999 |
| Sibling `vianium-mtproto\src\tl\` | `include/vianium/tl/api/v1/tl_api.h` | 2000-2999 |
| Sibling `vianium-mtproto\src\mtproto\` | `include/vianium/mtproto/api/v1/mtproto_api.h` | 1000-1999 |
| `Vianigram.Core.Media` (local) | `include/vianigram/media/api/v1/media_api.h` | 4000-4999 |
| Sibling `vianium-voip` | `include/vianium/voip/api/v1/voip_api.h` | 5000-5999 |

The 1000-5999 ranges do NOT clash with those reserved by other Vianium products for their own contexts; namespacing by enum (`MtProtoError::Code` etc.) prevents human confusion.

The WinRT shim (`api/v1/winrt_shim.cpp`) is exclusively for WP8.1; in a future Rust migration, the `<ctx>_api.h` stays the same and the WinRT shim is rewritten separately on each platform.

---

## 6. Composition root and wiring

`Core\Vianigram.Composition\Roots\VianigramCompositionRoot.cs` (managed) constructs the native WinRT components in order:

```csharp
public sealed class VianigramCompositionRoot : IDisposable {
    public ILogger Logger { get; }
    public IClock Clock { get; }
    public ITelemetry Telemetry { get; }

    // Native contexts (consumed from sibling repos via their WinMDs)
    public Vianium.Core.Net.v1.SocketTransport NetTransport { get; }
    public Vianium.Core.Http.v1.HttpClient HttpClient { get; }

    public Vianium.Crypto.v1.CryptoSession CryptoSession { get; }       // sibling vianium-crypto
    public Vianium.Tl.v1.TlSchema TlSchema { get; }                     // sibling vianium-mtproto (src\tl\)
    public Vianium.Mtproto.v1.MtprotoChannel MtprotoChannel { get; }    // sibling vianium-mtproto (src\mtproto\)
    public Vianigram.Core.Media.v1.MediaDecoder MediaDecoder { get; }   // local
    public Vianium.Voip.v1.VoipSession VoipSession { get; }             // sibling vianium-voip; lazy

    public VianigramCompositionRoot(VianigramConfig config) {
        // Kernel-managed equivalents
        Logger = new VianigramLogger();
        Clock = new SystemClock();
        Telemetry = new VianigramTelemetry();

        // Inject into native via WinRT factories.
        // Order matters: Crypto and Tl before MTProto; Media before Voip.
        CryptoSession = Vianium.Crypto.v1.CryptoSession.Create(/* deps */);
        TlSchema = Vianium.Tl.v1.TlSchema.LoadLayer(214);
        MtprotoChannel = Vianium.Mtproto.v1.MtprotoChannel.Create(
            CryptoSession, TlSchema, NetTransport, /* logger callback */);
        MediaDecoder = Vianigram.Core.Media.v1.MediaDecoder.Create(/* deps */);
        // VoipSession is constructed on-demand when a call starts.
    }

    public void Dispose() { /* dispose in reverse order */ }
}
```

Each native ref class exposes a `Create(...)` factory that receives the injected ports (logger callback, clock callback, RNG). Internally, the ports are wired to the corresponding managed implementations.

---

## 7. Phasing — what gets built in which ROADMAP Phase

| Phase | Native contexts opened | Contexts closed |
|-------|-----------------------------|---------------------|
| Phase 0 | (skeleton vcxproj of the siblings + Media, no domain code) | — |
| Phase 1 | `vianium-crypto`, `vianium-mtproto\src\tl\`, `vianium-mtproto\src\mtproto\` (Crypto/Tl in parallel first, MTProto on closing) | — |
| Phase 2 | (consumes `vianium-mtproto` from managed; does not touch native) | — |
| Phase 3 | `Vianigram.Core.Media` (Opus + WebP + image scaling) | Crypto, Tl, MTProto |
| Phase 4 | (managed: notifications, settings) | Media |
| Phase 5 | (managed: secret chats — uses existing `vianium-crypto`) | — |
| Phase 6 | `vianium-voip` (RTP + Opus reuse of Media) | — |
| Phase 7 | (managed: stickers TGS placeholder) | Voip |

Each Phase closes its context(s) with: a green `<ctx>_contract.exe` C test + green DEBUG-startup self-tests + a green managed smoke test + telemetry recording the documented counters/histograms.

---

## 8. Cross-link to per-context docs

- [01-mtproto.md](01-mtproto.md) — MTProto 2.0 protocol, framing, DH handshake, session, AES-IGE encryption.
- [02-tl.md](02-tl.md) — TL serialization, codegen tool, layer 214.
- [03-crypto.md](03-crypto.md) — crypto primitives: AES-IGE, DH 2048, SRP-2048, PBKDF2-SHA512, Curve25519 secret-chat.
- [04-media.md](04-media.md) — Opus, WebP, image scaling, video thumb, TGS deferred.
- [05-voip.md](05-voip.md) — RTP + jitter buffer + Opus + AEC + STUN/ICE.

Required prior reading is always [architecture-principles.md](architecture-principles.md).
