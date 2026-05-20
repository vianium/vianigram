# Architecture Principles — Vianigram Native (C++/CX)

**Status:** active · **Target:** Windows Phone 8.1 · **Last reviewed:** 2026-04-27

> Required prior reading:
> - `..\vianium-docs\native-port\architecture-principles.md` — the DDD+hex principles that are reused as-is.
> - `..\vianium-docs\native-port\rust-migration-readiness.md` — C ABI FFI discipline; applies from the first line.
> - `..\..\ROADMAP.md` — phases 0-7.

This document is the architectural source of truth for the native bounded contexts that Vianigram consumes — most of them now live as sibling repos in the `vianium` org (`vianium-mtproto` with its `src\tl\` and `src\mtproto\` subcomponents, `vianium-crypto`, `vianium-voip`). Vianigram keeps only `Vianigram.Core.Media` (Opus/WebP/image, specific to media messages) in its own repo. The reused siblings (`vianium-kernel`, `vianium-tls`, `vianium-http`, `vianium-net`) are **not modified** — they are referenced by relative path. If something in a sibling needs to change to support Vianigram, it is discussed first and that sibling's doc is updated.

---

## 1. Toolchain and target

| Axis | Value | Reason |
|-----|-------|--------|
| Platform | Windows Phone 8.1 (AppContainer) | Product constraint. |
| Toolset | MSVC v120 (`v120_wp81`), VS2013/2015 with the WP8.1 SDK | The only official WP8.1 toolset. |
| Standard C++ | `/std:c++14` when the header supports it; `c++11` by default | v120 does not implement C++17. Do not use `std::optional`, `std::variant`, `std::filesystem`, `std::string_view`, `std::any`, structured bindings, fold expressions, `if constexpr`. |
| Application Type | WinRT Component (DLL) per context | Same pattern as `Vianium.Core.Tls`. Each context has its `.winmd`. |
| WinMD | `<GenerateWindowsMetadata>true</GenerateWindowsMetadata>` | Allows consumption from C# managed without P/Invoke. |
| Threading | `Windows::System::Threading::ThreadPool` + `concurrency::create_task` | `std::thread` does not exist on WP8.1. |
| Logging | `OutputDebugString` + a telemetry callback injected via `Vianium.Core.Kernel::ILogger` | Lesson from PivoraTLSNative: massive logging from day 1. |
| Build | MSBuild 14.0 from a Developer Command Prompt for VS2013 | `build-validate.cmd` wrapper. |

**Absolute prohibitions:**
- `std::filesystem`, `std::ifstream` over managed paths → use `Windows::Storage` and pass an `IBuffer`.
- `std::thread`, `std::async`, `std::future` → use `concurrency::task<T>` with `task_continuation_context::use_arbitrary()`.
- STL exceptions (`std::runtime_error`, `std::bad_alloc`) crossing the WinMD ABI → catch at the boundary and map to `HRESULT` or `Result<T, Error>`.
- RTTI crossing a context → `dynamic_cast` in domain is prohibited (cross-context contracts are C ABI).
- `std::shared_ptr` crossing the WinMD → an internal `unique_ptr` or `Microsoft::WRL::ComPtr` for the WinRT surface.

---

## 2. DDD+Hexagonal skeleton per context

Identical to the one adopted by the `vianium-*` sibling repos. Each `Vianigram.Core.<Ctx>/` (or sibling equivalent) looks like this:

```
Vianigram.Core.<Ctx>/
├── Vianigram.Core.<Ctx>.vcxproj          (WinRT Component, v120, WP8.1)
├── Vianigram.Core.<Ctx>.def              (export table when there is a flat native C ABI)
├── README.md                              (location + cross-link to the doc in docs/native-port/)
├── include/
│   └── vianigram/<ctx>/api/v1/
│       ├── <ctx>_api.h                    (flat C ABI header — the only cross-context SoT)
│       ├── <ctx>_errors.h                 (numbered codes, namespaced by range)
│       └── <ctx>_types.h                  (POD types projected to the boundary)
└── src/
    ├── pch.h, pch.cpp
    ├── domain/                             (pure core, translatable to Rust)
    │   ├── value_objects/
    │   ├── entities/
    │   ├── aggregates/
    │   ├── policies/
    │   ├── events/
    │   └── services/
    ├── application/
    │   ├── use_cases/
    │   ├── command_handlers/
    │   └── query_handlers/
    ├── ports/
    │   ├── inbound/
    │   └── outbound/
    ├── infrastructure/
    │   ├── allocators/
    │   ├── codec/                          (parsers, generated TL, codecs)
    │   ├── persistence/
    │   ├── platform/                       (subfolder where <windows.h> IS allowed)
    │   └── transport/
    ├── api/
    │   └── v1/
    │       ├── c_api.cpp                   (C ABI impl; delegates to use cases)
    │       ├── winrt_shim.cpp              (sealed ref class wrapping the C ABI)
    │       └── pch.h                       (C++/CX enabled only here)
    └── internal/                           (helpers shared between application and infra
                                              that do not deserve to be in domain/)
```

**Dependency rules**, identical to those of the `vianium-kernel` sibling (see `..\vianium-docs\native-port\architecture-principles.md` §2):

```
api/v1/c_api.cpp     → application/, ports/inbound/    (NO direct domain, NO infra)
api/v1/winrt_shim.cpp→ ONLY api/v1/<ctx>_api.h          (own C ABI, without touching the domain)
application/         → domain/, ports/                  (NO direct infrastructure)
domain/              → domain/ + Vianium.Core.Kernel    (NO ports, NO infra, NO Windows.h)
infrastructure/      → ports/outbound/, domain/, Kernel
                       infrastructure/platform/ may use Windows.h
```

`domain/` is **pure**: only standard C++ (the subset compatible with v120) + `Vianium.Core.Kernel` (from the `vianium-kernel` sibling). Unit-testable without the emulator. Translatable to Rust in ~1-2 days per context (rule 7 of the `vianium-kernel` sibling).

---

## 3. WinMD ABI — what crosses, what does not

The public WinRT surface lives **only** in `src/api/v1/winrt_shim.cpp`. There the `public ref class sealed` types are declared with a versioned namespace:

```cpp
namespace Vianium::Mtproto::v1 {       // sibling vianium-mtproto WinMD
    public ref class MTProtoChannel sealed {
    public:
        Windows::Foundation::IAsyncOperation<MTProtoConnectResult^>^
            ConnectAsync(int dcId, Windows::Storage::Streams::IBuffer^ authKey);
        Windows::Foundation::IAsyncOperation<MTProtoSendResult^>^
            SendAsync(int constructorId, Windows::Storage::Streams::IBuffer^ tlPayload);
        event Windows::Foundation::TypedEventHandler<MTProtoChannel^, MTProtoMessage^>^ MessageReceived;
    };
}
```

**What crosses the WinMD ABI:**
- `Windows::Foundation::IAsyncOperation<T>^` for async operations.
- `Windows::Storage::Streams::IBuffer^` for zero-copy byte arrays.
- `Platform::String^` for short strings (auth tokens, phone numbers); UTF-8 `IBuffer` for bulk text.
- `Platform::Object^` wrapping projected POD value types (e.g., `MTProtoSendResult^` ref class with fields `int ErrorCode`, `IBuffer^ Payload`).
- Events as `event TypedEventHandler<TSender, TArgs>^`.

**What does NOT cross:**
- Native C++ types (`std::vector`, `std::string`, `std::unique_ptr`, custom structs).
- Templates.
- C++ exceptions (all caught at the boundary and mapped to `Platform::COMException` with an HRESULT encoding `Error::code`).
- Raw pointers to memory.

**Canonical boundary pattern:**
```cpp
// api/v1/winrt_shim.cpp
Windows::Foundation::IAsyncOperation<SendResult^>^
MTProtoChannel::SendAsync(int ctorId, IBuffer^ payload) {
    return concurrency::create_async([this, ctorId, payload]() -> SendResult^ {
        auto bytes = Vianium::Kernel::IBufferToSpan(payload);     // zero-copy view
        auto result = m_channel->Send(ctorId, bytes);             // application::use_cases::SendUseCase
        if (result.IsFailure()) {
            throw ref new Platform::COMException(
                Vianium::Kernel::ErrorToHResult(result.Error()),
                Vianium::Kernel::ToPlatformString(result.Error().Message()));
        }
        return ref new SendResult(result.Value());                // POD projection
    });
}
```

`Vianium::Kernel::IBufferToSpan` and `ErrorToHResult` live in `Vianium.Core.Kernel/include/interop/`. Vianigram consumes them without reimplementing them.

---

## 4. `Result<T, Error>` and `Maybe<T>` everywhere

Reused verbatim from the `..\vianium-kernel\include\result.h` and `maybe.h` sibling. Zero `throw` inside `domain/` and `application/`. Pattern:

```cpp
namespace vianium::mtproto::application {

class HandshakeUseCase {
public:
    using Result = vianium::kernel::Result<AuthKey, MtProtoError>;

    Result Execute(IRandom* rng, IClock* clock, ITcpTransport* tcp) {
        auto pq = SendReqPq(tcp, rng);
        if (pq.IsFailure()) return Result::Fail(pq.Error());

        auto factors = FactorizePq(pq.Value().pq);
        if (factors.IsFailure()) return Result::Fail(factors.Error());

        // ... rest of the DH flow ...

        return Result::Ok(authKey);
    }
};

} // namespace
```

`MtProtoError` (in `domain/errors/mtproto_errors.h`) is POD-like:
```cpp
struct MtProtoError {
    enum class Code : int32_t {
        Ok = 0,
        BadMsgNotification = 1001,
        BadServerSalt = 1002,
        FloodWait = 1003,
        AuthKeyMismatch = 1004,
        TransportClosed = 1005,
        DhPrimeRejected = 1006,
        NonceMismatch = 1007,
        TimeDriftTooLarge = 1008,
        SessionRevoked = 1009,
        // ...
    };
    Code code;
    int32_t aux;        // for FloodWait: seconds. For BadServerSalt: new salt low bits.
    char detail[64];    // short message, without allocations.
};
```

**Mapping to HRESULT at the boundary** (in `Vianium.Core.Kernel/include/interop/hresult.h`):
- `MtProtoError::Code` maps to custom HRESULTS in facility `FACILITY_ITF` with bits 1xxx for MTProto, 2xxx for Tl, 3xxx for Crypto, 4xxx for Media, 5xxx for Voip. It matches the convention used by the `vianium-*` siblings for their own.

---

## 5. Memory: arenas + heap + pooling

Lesson from PivoraTLSNative and from `..\vianium-tls\src\core\memory_pool.h`: **the heap is for long-lived state; arenas are for per-request data**. Mixing the two in the same allocator burns the cache and fragments.

**Recommended allocation per context:**

| Context | Long-lived state (heap) | Per-request scratch (arena) |
|---------|---------------------------|------------------------------|
| MTProto | session, salt, msg_id history (replay window), auth_key cache, connection pool | ClientHello-equivalent DH params, msg encryption buffer, RPC outbox slot |
| Tl | type registry (constructor IDs), schema metadata | reader/writer buffer per RPC call (estimate-then-grow) |
| Crypto | persistent keys (auth_key, secret-chat root keys) loaded on-demand | working set per encryption op (msg_key, AES round keys, DH temporaries) |
| Media | decoder tables (Opus codebooks, WebP huffman trees), thumbnail cache (bounded LRU) | per-frame decoder scratch (PCM out, intermediate planes) |
| Voip | jitter buffer ring, AEC state, codec instance | per-packet decode/encode scratch |

**Rough hot working set budget (peak, not cumulative):**

| Context | Hot RAM budget | Notes |
|---------|----------------|-------|
| MTProto | 12 MB | 4 connections × ~2 MB each + replay window ~500 KB + a small auth_key cache. |
| Tl | 4 MB | type registry ~2 MB + active arenas ~1-2 MB. Zero managed GC allocations. |
| Crypto | 2 MB | round keys, bignum scratch, RNG state. |
| Media | 24 MB | Opus decoder ~100 KB + WebP scratch ~500 KB + thumbnail cache LRU 16 MB cap + working frame buffers ~7 MB. |
| Voip | 8 MB | jitter buffer 180 ms × 16-bit × 48 kHz mono ≈ 17 KB; AEC state ~256 KB; codec ~50 KB; the rest for pacing/probing/metrics. |

**Total native Vianigram budget:** ~50 MB hot. WP8.1 on a 512 MB ARMv7 device (512 MB total, ~256 MB user-process limit) handles it with room to spare as long as the managed layer (XAML cache, ViewModels) stays disciplined.

**Allocator API** (reuse of `..\vianium-kernel\include\alloc\`):
- `IAllocator*` injected by the composition root.
- `ArenaAllocator` with `Reset()` at the end of each use case → zero leaks.
- `PoolAllocator<T>` for recurring fixed-size objects (e.g., `MtProtoMessage` containers).
- `HeapAllocator` (default `new`/`delete`) for everything else.

---

## 6. Zero-copy buffers

`Windows::Storage::Streams::IBuffer^` is the canonical contract for crossing the managed↔native boundary without copying. `..\vianium-kernel\include\interop\winrt_bridge.h` already provides:

```cpp
namespace vianium::kernel::interop {
    // Gives a non-owning view over the bytes of an IBuffer (uses the IBufferByteAccess COM).
    Result<Span<const uint8_t>, Error> IBufferToSpan(Windows::Storage::Streams::IBuffer^ buf);

    // Builds an IBuffer that wraps native memory which outlives the async return.
    // Note: buffer ownership semantics — the memory must live until GC releases the IBuffer.
    Windows::Storage::Streams::IBuffer^ SpanToOwnedBuffer(Span<const uint8_t> data);
}
```

**Zero-copy rules:**
1. Vianigram **does not copy** bytes that come from an `IBuffer` before processing them in MTProto/Tl. AES-IGE encryption overwrites in-place over a writable native buffer that is then projected as an `IBuffer` when it returns to managed.
2. For large chunks (file downloads, voice notes), Vianigram passes the `IBuffer` directly to managed without repackaging.
3. Short strings (phone, SMS code) are copied — they are small and simplicity wins.
4. **Never keep an `IBuffer^` alive beyond the use case that received it.** If the data must persist, copy it to a native buffer whose lifetime Vianigram controls.

---

## 7. Threading model

Each context exposes an async API via `Windows::Foundation::IAsyncOperation<T>^`. Internally it uses `concurrency::create_task` with continuations on `task_continuation_context::use_arbitrary()` (they do not tie to the UI thread).

```cpp
auto task = concurrency::create_task([this]() {
    return m_useCase->Execute();          // CPU-bound or sync I/O on a pool thread
}, m_taskOptions);

task.then([](Result<T, Error> result) {
    // continuation on an arbitrary thread
}, concurrency::task_continuation_context::use_arbitrary());
```

**Rules:**
- `domain/` pure: zero awareness of threading. Pure functions or methods that mutate the local state of an aggregate.
- `application/use_cases/`: blocking sync. If they need I/O, they call an `outbound/` port that internally may be async (wrap with `task::wait()` ONLY if the port exposes it that way, and always on a pool thread).
- `infrastructure/`: implements async ports using `concurrency::create_task`.
- `api/v1/winrt_shim.cpp`: `create_async([]{ ... use case ... })` wraps the synchronous use case in an `IAsyncOperation`.

**Cancellation:** `..\vianium-kernel\include\concurrency\i_cancellation_token.h` is propagated via a port. WinRT `IAsyncOperation::Cancel()` is connected to the token internally.

**Anti-patterns:**
- `task.wait()` on the UI thread → guaranteed freeze.
- synchronous `task.get()` from managed → defeats async.
- `while(!cancelled) { ... }` loops without yielding to the pool → starve other contexts.

---

## 8. Logging and telemetry

Reuse of the macro pattern from the `vianium-tls` sibling. Each context defines its prefix:

```cpp
// vianium-mtproto/src/internal/mtproto_log.h
#pragma once
#include <vianium/kernel/log/i_logger.h>

#if defined(_DEBUG)
#define MTP_DEBUG_LOG(logger, fmt, ...) \
    do { if (logger) (logger)->Debug("[MTProto] " fmt, ##__VA_ARGS__); } while(0)
#else
#define MTP_DEBUG_LOG(logger, fmt, ...) ((void)0)
#endif

#define MTP_INFO_LOG(logger, fmt, ...) \
    do { if (logger) (logger)->Info("[MTProto] " fmt, ##__VA_ARGS__); } while(0)
#define MTP_WARN_LOG(logger, fmt, ...) \
    do { if (logger) (logger)->Warn("[MTProto] " fmt, ##__VA_ARGS__); } while(0)
#define MTP_ERROR_LOG(logger, fmt, ...) \
    do { if (logger) (logger)->Error("[MTProto] " fmt, ##__VA_ARGS__); } while(0)
```

Equivalents per context: `TL_*`, `CRYPTO_*`, `MEDIA_*`, `VOIP_*`. An identical pattern to the `TLS_DEBUG_LOG` that the `vianium-tls` sibling already uses.

**Telemetry** is injected via `Vianium::Kernel::ITelemetry*`. Naming convention `<ctx>.<operation>.<unit>`:
- `mtproto.handshake.duration_ms`
- `mtproto.rpc.error_count{code=...}`
- `mtproto.connection.reconnects_total`
- `tl.deserialize.duration_us{type=...}`
- `crypto.aes_ige.encrypt_throughput_mbps`
- `media.opus.decode_frames_total`
- `voip.jitter.buffer_depth_ms`

---

## 9. Build and cross-context consumption

Each native bounded context (in its corresponding sibling repo or, for `Vianigram.Core.Media`, in this repo) produces **two artifacts**:
- `Vianium.<Ctx>.dll` (WinRT component) or `Vianigram.Core.Media.dll`.
- `Vianium.<Ctx>.winmd` (metadata).

**Solution layout** (`Vianigram.sln`):
```
Vianigram.sln
├── Core\Vianigram.Kernel\                   (managed C# clone of the vianium-managed-kernel sibling)
├── Core\Vianigram.Composition\              (managed C# composition root)
├── Core\Vianigram.Core.Media\               (native C++/CX; the only native context that lives in this repo)
└── (project references to sibling repos)
    ├── ..\vianium-kernel\Vianium.Core.Kernel.vcxproj
    ├── ..\vianium-tls\Vianium.Core.Tls.vcxproj
    ├── ..\vianium-http\Vianium.Core.Http.vcxproj
    ├── ..\vianium-net\Vianium.Core.Net.vcxproj
    ├── ..\vianium-crypto\Vianium.Crypto.vcxproj
    ├── ..\vianium-mtproto\Vianium.Mtproto.vcxproj      (includes src\tl\ and src\mtproto\)
    └── ..\vianium-voip\Vianium.Voip.vcxproj            (includes src\tgcalls\ and third_party\libtgvoip\)
```

**`<AdditionalIncludeDirectories>` permitted** inside `Vianigram.Core.Media` (or vcxproj-equivalent):
```xml
<AdditionalIncludeDirectories>
    $(MSBuildProjectDirectory)\;
    $(MSBuildProjectDirectory)\..\..\..\vianium-kernel\include\;
    $(MSBuildProjectDirectory)\..\..\..\vianium-tls\src\crypto\;     <!-- selective linking of primitives -->
    $(MSBuildProjectDirectory)\..\..\..\vianium-crypto\include\;     <!-- the sibling's C ABI header -->
</AdditionalIncludeDirectories>
```

**Prohibited:** including `..\<sibling>\src\domain\` or `..\<sibling>\src\application\` directly. The only cross-context contract is `include/vianium/<ctx>/api/v1/<ctx>_api.h` published by each sibling.

CI gate replicated from the `vianium-kernel` sibling:
```powershell
# build-validate.cmd → calls ValidateNoContextLeaks.ps1
Get-ChildItem -Recurse "Core\Vianigram.Core.Media\src\domain","Core\Vianigram.Core.Media\src\application" -Filter "*.h","*.cpp" |
    Select-String -Pattern '#include.*Vianium\.(?!Kernel)' |
    Where-Object { $_.Path -notmatch "infrastructure" }
# If it matches, fail.
```

---

## 10. Self-tests at startup (DEBUG)

Pattern inherited from the `vianium-tls::CryptoSelfTest` sibling (run at `DllMain` or the first public call in DEBUG builds):

```cpp
#if defined(_DEBUG)
namespace vianium::mtproto::internal {
    class MtProtoSelfTest {
    public:
        static void RunAll(vianium::kernel::ILogger* log);
    private:
        static void TestMsgIdMonotonicity(vianium::kernel::ILogger* log);
        static void TestSeqNoComputation(vianium::kernel::ILogger* log);
        static void TestSaltRotation(vianium::kernel::ILogger* log);
        static void TestAuthKeyDerivation(vianium::kernel::ILogger* log);
    };
}
#endif
```

Each context ships its battery:
- **Crypto**: AES-IGE NIST vectors, DH 2048-bit primes, SRP-2048 RFC 5054 vectors, PBKDF2-SHA512 RFC 7914 vectors.
- **Tl**: roundtrip of boxed/bare types, vector encoding 0x1cb5c415, flag-based optionals.
- **MTProto**: msg_id monotonicity, seq_no parity, salt mid-bits, auth_key sha1 fingerprint.
- **Media**: Opus reference vectors (RFC 6716 Annex A.5), WebP RIFF parser limits.
- **Voip**: jitter buffer adaptation curve, AEC convergence time on synthetic input.

If any self-test fails, the context logs ERROR and **refuses to offer service** (any call to the WinMD returns `E_VIANIGRAM_SELFTEST_FAILED`). A loud failure at startup beats protocol breakage in production.

---

## 11. Reuse of the `vianium-*` sibling repos

The ROADMAP Phase 1 states it explicitly: Vianigram **does not duplicate** primitives that the siblings already provide. The strategy:

| Sibling primitive | How Vianigram consumes it | Mechanism |
|---------------------------|----------------------------|-----------|
| `vianium-kernel` (Result, Maybe, ILogger, IAllocator, IClock, ITelemetry) | Project reference + `<AdditionalIncludeDirectories>` to `include/` | Direct static lib link (Kernel is not WinRT). |
| `vianium-tls/src/crypto/sha256.h` | Reused inside `vianium-crypto` (sibling) | Source inclusion: `<ClCompile Include="..\..\..\vianium-tls\src\crypto\sha256.cpp" />` in the `vianium-crypto` sibling's vcxproj. Same binary, without duplicating logic. |
| `vianium-tls/src/crypto/sha512.{h,cpp}` | Same | Same. |
| `vianium-tls/src/crypto/hmac.{h,cpp}` | Same | Same. |
| `vianium-tls/src/crypto/hkdf.{h,cpp}` | Same | Same. |
| `vianium-tls/src/crypto/x25519.{h,cpp}` | Same (for secret chats) | Same. |
| `vianium-tls/src/crypto/aes_core.{h,cpp}` | Same (base of the new AES-IGE) | Same. |
| `vianium-tls/src/crypto/aes_gcm.{h,cpp}` | Same (optional at-rest encryption) | Same. |
| `vianium-tls/src/crypto/bignum.{h,cpp}` | Same (DH 2048, SRP-2048) | Same. |
| `vianium-net::SocketTransport` | An adapter inside the `vianium-mtproto` sibling in `src/infrastructure/transport/` that wraps the `Vianium.Net.winmd` WinMD | WinRT consumption (consumer of the `.winmd`). |
| `vianium-tls::TlsStream` | NOT consumed by MTProto (MTProto encrypts at its own level), but it is by the Vianigram managed layer for auxiliary HTTPS (CDN downloads, etc.) | WinRT consumption in managed. |
| `vianium-http::HttpClient` | Consumed from managed for CDN photo/file downloads (paths secondary to the main MTProto) | WinRT consumption in managed. |

**If `vianium-tls` changes upstream** (e.g., adding Ed25519, OCSP, CT logs), Vianigram inherits the change in the next build. Zero duplication of maintenance.

**What Vianigram does NOT touch:**
- `vianium-tls`: it only reads the crypto headers via the `vianium-crypto` sibling. Never modifies.
- `vianium-http`: consumes the public API. Never modifies.
- `vianium-net`: same.
- `vianium-kernel`: same. If Vianigram **needs** a new cross-cutting abstraction (e.g., `IRandomAdvanced` for SRP), it is added to the `vianium-kernel` sibling via an upstream PR — not in Vianigram.

---

## 12. Threading + concurrency between managed and native

Vianigram managed (C# WP8.1) consumes the native WinMDs with `await`:

```csharp
public async Task<MTProtoSendResult> SendRpcAsync(int ctorId, IBuffer payload) {
    var channel = _composition.MTProtoChannel;          // native ref class
    return await channel.SendAsync(ctorId, payload).AsTask();
}
```

**Managed-side rules:**
- Never `.Result` or `.Wait()` (rule 0 of ROADMAP Phase 2).
- Cancellation with a `CancellationToken` that is projected to the native `IAsyncOperation::Cancel()`.
- Native errors arrive as a `COMException` with an `HResult` decodable to `MtProtoError::Code` via the `Vianigram.Kernel.Errors.FromHResult(hr)` helper.

---

## 13. WinMD versioning

Same as the `vianium-kernel` sibling, rule 4: each context exposes `api/v1/`, `api/v2/`, etc. v2 may add/change; v1 never breaks the binary once shipped.

```cpp
namespace Vianium::Mtproto::v1 {       // sibling vianium-mtproto
    public ref class MTProtoChannel sealed { /* stable shape */ };
}
namespace Vianium::Mtproto::v2 {
    public ref class MTProtoChannel sealed { /* new shape */ };
}
```

C# consumer:
```csharp
using Vianium.Mtproto.v1;        // explicit; never `using static`.
```

Tl layer bumps (e.g., 214 → 220) **do not break v1** of the WinMD: the new schema coexists as additional factories. Only when the **shape** of the API ref class changes (parameters, return types) is it promoted to v2.

---

## 14. Explicit anti-patterns for native Vianigram

1. **Plaintext auth key in an `IBuffer`** that escapes to the boundary → always encrypt with `DataProtectionProvider` before crossing to managed. Handle in-clear only inside the `vianium-crypto` sibling.
2. **Nonce reuse** in MTProto → each `req_pq` regenerates a 128-bit nonce with `IRandom`. Never cache.
3. **Non-monotonic msg_id** → the `MsgIdGenerator` aggregate centralizes generation. Any path that bypasses it is a critical bug.
4. **`std::mutex` cross-context** → any cross-context synchronization is done with a port + adapter, not with shared primitives. Each context owns its internal lock.
5. **TL schema hardcoded outside the codegen** → the only SoT is `tl/scheme-layer-N.tl`. Any literal `constructorId` in code is reviewed in code review.
6. **Logging of auth_key, server_salt, msg_key, plaintext bodies** → categorically prohibited even in DEBUG. The log macro must filter sensitive fields.
7. **Unbounded replay window** → MTProto requires a fixed window (64 msg_id history recommended); exceeding it emits a warning and discards the oldest.
8. **Decoders without a self-test at startup** → any decoder (Opus, WebP) runs a vector test in DEBUG; failure → the feature is not offered.

---

## 15. Prerequisites to start coding

1. ROADMAP Phase 0 closed: managed kernel clone + composition root + project references to the sibling repos + skeleton vcxproj for `Vianigram.Core.Media`.
2. The `vianium-kernel` sibling's self-tests running green on x86 and ARM.
3. `build-validate.cmd Debug x86` exit 0 with the reused siblings resolving by path.
4. `docs/security/mtproto-policy.md` approved (defines invariants that `vianium-mtproto::domain/policies/` must enforce).
5. CI gate: dependency check (no prohibited cross-context includes).
6. A testing-harness decision for the native code (Catch2 v1 compatible with WP8.1 — inherit the choice from the `vianium-kernel` sibling).

Once all 6 are green, open Phase 1 against `01-mtproto.md`, `02-tl.md`, `03-crypto.md` in parallel (Crypto blocks MTProto; Tl is independent; the internal order is decided in planning).
