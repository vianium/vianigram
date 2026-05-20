# VoIP Strategy — Classic stack vs. modern tgcalls

**Status:** strategy / scoping · **Last reviewed:** 2026-05-03 · **Audience:** native-port leads, agents W1..W5

> Companion documents:
> - [05-voip.md](05-voip.md) — bounded context, layer 92 protocol details (already implemented).
> - [05a-tgcalls-wire-format.md](05a-tgcalls-wire-format.md) — tgcalls v2 outer envelope, signaling JSON, DTLS-SRTP, reflector protocol, ABI deltas.
> - [architecture-principles.md](architecture-principles.md) — DDD+hex, WinMD ABI rules, memory budgets.

This document captures the strategic decision tree for VoIP on Windows Phone 8.1 ARM after the live device run on 2026-05-03 demonstrated that we successfully **transmit** to a modern (tgcalls v2) peer but never get audio because the peer never speaks layer-92 to us.

The goal is to lock in three things before any more code changes land:

1. The diagnostic facts that proved the peer is modern.
2. The three viable forward paths (A: classic-vs-classic; B: decrypt incoming v2 signaling; C: full tgcalls vendoring).
3. Which path each agent (W1..W5) is doing today and which is deferred.

---

## 1. Current state

### 1.1 What works (classic stack — layer 92 / `tgvoip 2.4.4`)

The classic Telegram VoIP profile is fully implemented in-process under `..\vianium-voip\`:

| Concern | File |
|---|---|
| Inbound API (WinMD/IDL boundary) | `..\vianium-voip\src\api\v1\voip_api.cpp` · `voip_api.h` |
| Application orchestration (DH, key fingerprint, media start/stop) | `..\vianium-voip\src\application\voip_engine.cpp` · `voip_engine.h` |
| Aggregate root + media state | `..\vianium-voip\src\domain\voip_media_session.cpp` · `voip_media_session.h` |
| Layer-92 packet crypto (msg_key + AES-IGE) | `..\vianium-voip\src\domain\voip_packet_crypto.cpp` · `voip_packet_crypto.h` |
| Reflector packet wrapping (`peer_tag` + `msg_key` + payload) | `..\vianium-voip\src\domain\voip_reflector_packet.cpp` · `voip_reflector_packet.h` |
| Stream-data / control / RTP packet codecs | `..\vianium-voip\src\domain\voip_stream_data_packet.cpp` · `voip_control_packet.cpp` · `voip_rtp_packet.cpp` |
| Endpoint selection (UDP P2P → UDP relay → TCP relay) | `..\vianium-voip\src\domain\voip_endpoint_selector.cpp` |
| Adaptive jitter buffer | `..\vianium-voip\src\domain\voip_jitter_buffer.cpp` |
| UDP reflector transport (WinRT DatagramSocket) | `..\vianium-voip\src\infrastructure\datagram_socket_reflector_transport.cpp` |
| Opus encode/decode | `..\vianium-voip\src\infrastructure\opus_voip_codec.cpp` |
| Audio capture/render (WASAPI via WinRT) | `..\vianium-voip\src\infrastructure\winrt_voip_audio_device.cpp` |
| Outbound port for a future modern engine | `..\vianium-voip\src\ports\outbound\i_tgcalls_media_graph.h` |
| C ABI placeholder DLL | `..\vianium-voip\src\tgcalls\src\tgcalls_backend.cpp` · `tgcalls_backend.h` |
| C ABI public header | `..\vianium-voip\src\ports\outbound\tgcalls_native_abi.h` |

The classic stack reaches the reflector, sends Opus stream-data packets that decrypt cleanly server-side, and would deliver audio to any peer that runs a layer-92 / `tgvoip 2.4.4`-only client.

### 1.2 What is broken on the live device

The peer (today: an Android 11.x client with up-to-date Telegram) advertises `maxLayer=92` in the TL response but actually drives the call with tgcalls v2 (modern). The live trace from the 2026-05-03 device run shows the symptom precisely:

```
txPackets=8   rxPackets=2   selfInfo=2   decryptFailures=0
peer also supplied WebRTC endpoints and delivered 5 signaling packet(s)/3039B
```

Reading from left to right:

- `txPackets=8` — we sent 8 layer-92 stream-data / init packets to the reflector.
- `rxPackets=2` — we received exactly 2 packets back. Both decrypted (`decryptFailures=0`) and were `selfInfo` echoes from our own writes (the reflector loops un-routed packets back when no peer is registered for the session).
- The peer **never sent us a layer-92 stream-data packet**, even though it had a registered session on the same reflector.
- The peer **did** send 5 packets via `updatePhoneCallSignalingData` totalling 3039 B. Those updates carry tgcalls v2 outer-envelope frames (msg_key + AES-256-CTR over a JSON `@type` body — see [05a §2.1, §2.2](05a-tgcalls-wire-format.md)).
- The peer's `phone.phoneCall` payload included WebRTC endpoints in addition to the legacy reflectors, confirming the peer expected a tgcalls v2 ICE handshake.

### 1.3 Why the diagnosis is correct

Two independent facts pin the diagnosis to "peer runs modern":

1. **Crypto and transport are healthy on our side.** The reflector echoes our packets back unchanged (visible as `selfInfo`) and they decrypt without error. If our msg_key derivation, AES-IGE, or reflector framing were wrong we would see `decryptFailures` ≥ 1 on the round trip. We do not. Therefore packets that *would* arrive from a layer-92 peer would parse correctly.

2. **Peer signals via `updatePhoneCallSignalingData`.** That update only carries data from `tgcalls::Instance::Descriptor::signalingDataEmitted` (see [`Instance.h:240`](../../../_references/telegram-android/TMessagesProj/jni/voip/tgcalls/Instance.h)). Layer-92 / `tgvoip 2.4.4` does not emit signaling data — it sends everything through the reflector data path. A peer that emits 5 signaling blobs and 3039 B of JSON is a tgcalls v2 peer that ignored our `maxLayer=92` advertisement and started its v2 networking anyway.

The reflector is acting correctly; the layer-92 stack is acting correctly; the peer is acting modernly. The gap is between us and the peer's expectations.

---

## 2. Three forward paths

### Path A — Two-Vianigram-instance test (≈ 1 hour)

**Goal:** prove end-to-end audio works against a peer that *does* speak layer 92, decoupling the client implementation from the peer-modernity question.

**Setup:**

1. Side-load Vianigram into a second WP8.1 device (or a second user account on the same physical device using a separate isolated-storage profile).
2. Sign both instances into different Telegram accounts.
3. Place a call from instance A to instance B over Wi-Fi (no cellular variability).
4. Capture device logs from both sides; compare `txPackets`/`rxPackets` and `decryptFailures`.

**Expected outcome:**

- Symmetric counters (`txPackets ≈ rxPackets` on both sides, `decryptFailures=0`).
- Audible Opus playback both ways through `WinrtVoipAudioDevice`.
- `selfInfo` count drops to ≤ 1 because the peer immediately registers on the reflector and the loop-back path stops being the only return.

**Why this matters:** it validates the classic stack as production-quality independent of the modern-peer problem. If two Vianigram clients can call each other, we have a working VoIP product for the WP-to-WP case while we plan Path C. It also gives us a known-good packet capture to diff against the failing case from §1.2.

### Path B — Decrypt incoming `signalingData` (this session)

**Goal:** read the peer's tgcalls v2 signaling packets *without* yet generating any of our own. This does not enable calls. It produces the diagnostic data we need to scope Path C accurately and removes a major unknown ("what is actually inside those 3039 bytes?").

**Why we can do this today:** the outer envelope in `tgcalls::EncryptedConnection::decryptRawPacket` reuses the same primitives already shipped by the sibling `vianium-crypto`:

- AES-256 in CTR mode over the body.
- SHA-256 for `msg_key` derivation and verification.
- 256-byte shared key derived from the same MTProto DH that the classic stack already negotiates (`..\vianium-voip\src\application\voip_engine.cpp` already produces it).

The algorithm sits in 36 lines:

- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\EncryptedConnection.cpp:119-155` — `decryptRawPacket` (msg_key check, x-offset selection, CTR call, 4-byte seq strip).
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\CryptoHelper.cpp:8-58` — `PrepareAesKeyIv` (SHA-256 KDF that produces the 32-byte AES key + 16-byte IV from `key`, `msg_key` and the `x` offset) and `AesProcessCtr`.

The key insight, also documented in [05a §2.1](05a-tgcalls-wire-format.md), is the offset:

```
x = (peer_isOutgoing ? 8 : 0) + (Type == Signaling ? 128 : 0)
```

For decrypting *incoming signaling* on our side, the peer's `isOutgoing` is the inverse of our own (one side initiated the call; tgcalls keys are direction-asymmetric), and the `Type == Signaling` arm always adds `128`. That is the source of the much-debated "+128 for signaling" magic number.

**Agents covering Path B today:**

- W1 — wire `TgcallsSignalingCodec::Decrypt` (`..\vianium-voip\src\domain\tgcalls_signaling_codec.h`) to the existing AES/SHA-256 primitives.
- W2 — feed the decoded plaintext into a JSON-aware logger that classifies messages by `@type` (see [05a §2.2](05a-tgcalls-wire-format.md)).
- W4 — surface the per-message stats (`@type` histogram, `exchangeId`, candidate counts) into the same trace channel as the existing `txPackets/rxPackets` line.
- W5 — guard with the `Voip.IngestSignalingData` feature flag so this work cannot regress the classic flow.

**What Path B does *not* enable:** we still cannot place or answer a call against a modern peer because we do not generate a matching `InitialSetup` (DTLS fingerprints, ufrag/pwd), we do not produce ICE candidates, we have no DTLS handshake, and we have no SRTP key derivation. Audio remains silent. The win is purely diagnostic — but it converts an opaque 3039 B blob into a structured trace ("peer sent: 1× InitialSetup, 2× Candidates with N candidates, 2× MediaState"), which is the single biggest input to Path C scoping.

### Path C — Vendor full tgcalls + WebRTC + DTLS-SRTP (multi-week)

**Goal:** make calls to modern peers work. This means reaching feature parity with the tgcalls v2 protocol on the wire, which transitively means dragging in libwebrtc's ICE/DTLS/SRTP/codec subset, plus libsrtp, plus a TLS library that implements DTLS, plus Opus.

#### Vendoring inventory

Approximate sizes (LOC of vendored sources, not binary footprint) drawn from the upstream Telegram Android tree under `_references\telegram-android\TMessagesProj\jni\voip\`:

| Component | Approx LOC | Why we need it |
|---|---:|---|
| **tgcalls v2** | ~25 k | the protocol layer itself: `EncryptedConnection`, `Instance`, `Manager`, `MediaManager`, `NetworkManager`, `v2/InstanceV2Impl`, `v2/Signaling`, `v2/ReflectorPort`, `v2/NativeNetworkingImpl`, `v2/ContentNegotiation`. |
| **libwebrtc subset** | ~250 k | ICE (`p2p/base/p2p_transport_channel`), DTLS (`p2p/base/dtls_transport`, `pc/dtls_transport`), RTP/RTCP (`modules/rtp_rtcp`), audio engine (`audio/`), `rtc_base` threading + sockets, `api/` headers tgcalls depends on. We can drop video, screen-share, data channels (SCTP), echo canceller v3 if we accept worse audio quality, hardware codecs. |
| **libsrtp** | ~15 k | SRTP/SRTCP after DTLS-SRTP key derivation. Used by libwebrtc's `pc/srtp_session.cc`. |
| **BoringSSL** *(or mbedTLS DTLS)* | ~80 k | DTLS 1.2 handshake. Telegram bundles BoringSSL but we already ship mbedTLS and prefer it; the open question is whether mbedTLS DTLS-SRTP supports `use_srtp` extension and the cipher suites libwebrtc expects (`SRTP_AEAD_AES_128_GCM` / `SRTP_AEAD_AES_256_GCM`). TBD: confirm in Phase C-1. |
| **Opus 1.3.x** | ~50 k | Already vendored under `..\vianium-voip\src\third_party\opus_config\` for the classic stack. We reuse it. |
| **json11** | ~600 | tgcalls v2 signaling is JSON. Header-only, `_references\...\tgcalls\third-party\json11.hpp`. Trivial to vendor. |

Total: ≈ 420 k LOC of new vendored C/C++. By comparison the entire current `..\vianium-voip\src\` tree (excluding `third_party/`) is ~5 k LOC.

#### WP8.1 ARM cross-compile challenges

Concrete known issues in the libwebrtc subset:

- `rtc_base/platform_thread.cc` uses desktop Win32 threading (`SetThreadPriority`, `SetThreadDescription`) and assumes `_WIN32_WINNT >= 0x0600`. WP8.1 has a Store-app-restricted Win32 surface. We need a `platform_thread_winrt.cc` shim using `Windows::System::Threading::ThreadPool` and `WorkItemPriority`.
- `rtc_base/socket_posix.cc` is unused on Windows but `rtc_base/win/winsock_initializer.cc` calls `WSAStartup` from a static initializer; allowed on WP8.1 but the Store app validator flags it as a warning.
- `modules/audio_device/win/` has no UWP backend — only `core_audio_*.cc` desktop files. We must replace the entire `AudioDeviceModule` with our existing `WinrtVoipAudioDevice` (already a working WASAPI-via-WinRT implementation), wrapped behind `webrtc::AudioDeviceModule`.
- `rtc_base/system/file_wrapper.cc` uses `_wfopen_s` which is whitelisted on WP8.1 but logging paths (`Config::logPath` / `statsLogPath`) must be remapped to `ApplicationData::Current::LocalFolder`.
- `api/audio_codecs/opus/` builds fine but pulls in `common_audio/signal_processing/` which needs ARM NEON detection at compile time (no runtime detection on WP8.1). Set `WEBRTC_HAS_NEON=1` unconditionally for ARM.
- `p2p/base/basic_packet_socket_factory.cc` uses `rtc::AsyncUDPSocket` over BSD sockets. Our `DatagramSocketReflectorTransport` already proves we can do UDP over `Windows.Networking.Sockets.DatagramSocket`. We need a `WinRTSocketFactory` that produces `AsyncPacketSocket` adapters around `DatagramSocket` and `StreamSocket`.

There is no realistic path to "drop libwebrtc in and link". Each of the items above is a multi-day shim.

#### Phased breakdown

- **Phase C-1 — foundations (1-2 weeks).** Vendor + cross-compile json11, libsrtp, mbedTLS DTLS (or BoringSSL if mbedTLS DTLS-SRTP turns out to be insufficient — decision gate at end of Phase C-1). Reuse existing Opus build. Smoke test: DTLS handshake between two on-device processes; `srtp_protect`/`srtp_unprotect` round-trip.
- **Phase C-2 — libwebrtc subset and platform shims (3-4 weeks, highest risk).** Bring up `rtc_base` threading (WinRT shim), `rtc_base` sockets (WinRT shim), `p2p/base` ICE, DTLS transport, SRTP transport. Replace `AudioDeviceModule` with our existing WinRT audio device. Smoke test: ICE candidate gathering and connectivity check between two on-device processes (loopback ICE).
- **Phase C-3 — tgcalls v2 vendoring (2-3 weeks).** Build `tgcalls::Instance` against the libwebrtc subset from C-2. Wire `Descriptor::signalingDataEmitted`, `stateUpdated`, `signalBarsUpdated`, `remoteMediaStateUpdated`, `audioLevelsUpdated`, `remoteBatteryLevelIsLowUpdated` callbacks through to the existing `VianiumTgcallsStartDescriptor` C ABI (additions enumerated in [05a §2.5](05a-tgcalls-wire-format.md)). Resolve the project-cycle problem documented in `..\vianium-voip\src\tgcalls\src\tgcalls_backend.cpp:37-45` by extracting the sibling `vianium-voip` domain+infrastructure into a static lib both DLLs can link.
- **Phase C-4 — integration, ICE state machine, audio tuning (1-2 weeks).** First call to a modern peer. Validate ICE state transitions, DTLS handshake, SRTP keying, Opus jitter buffer behaviour, AEC/NS/AGC pipeline, and `signalBarsUpdated` UI signalling. Performance-tune for ARM.

**Realistic total: 7-11 weeks of focused work** for one engineer on this stack. The critical-path risk is Phase C-2 because libwebrtc's WinRT story does not exist upstream.

---

## 3. Recommended sequence

- **Today (this session)** — agents W1..W5 land Path B. We close the session with a structured trace of what tgcalls v2 peers actually send us, gated behind `Voip.IngestSignalingData`.
- **Tomorrow** — perform the Path A two-instance test and lock in a known-good baseline. If Path A works it ships as a Vianigram-to-Vianigram VoIP capability while Path C is in flight.
- **Next sprint after Path B data is reviewed** — open Phase C-1 (vendoring foundations) with concrete numbers from the Path B traces (how many `Candidates` messages, average payload size, observed `setup` strings, observed `hash` algorithms, observed `videoState` transitions on audio-only calls).

**Expectation set with stakeholders:** a call to a modern peer will not work tonight, will not work in this sprint, and is realistically 7-11 weeks away. What lands tonight is diagnostic clarity and a feature-flagged kill switch that lets us study modern peers in production without changing their behaviour.

---

## 4. Critical files (paths only)

Files this work touches or will touch — all paths absolute relative to repo root:

- `..\vianium-voip\src\application\voip_engine.cpp` · `voip_engine.h` — orchestrator; gains a `ReceiveSignalingData` path that delegates to `TgcallsSignalingCodec::Decrypt` when the flag is set.
- `..\vianium-voip\src\domain\tgcalls_signaling_codec.h` — Path B header, already declared.
- `..\vianium-voip\src\domain\tgcalls_signaling_codec.cpp` — *(to be created in Path B)*.
- `..\vianium-voip\src\domain\voip_packet_crypto.cpp` · `voip_packet_crypto.h` — source of the AES-IGE + SHA-256 primitives we reuse for AES-CTR in Path B (CTR variant added there or in a sibling helper).
- `..\vianium-voip\src\ports\outbound\tgcalls_native_abi.h` — current C ABI; Path C extends it (see [05a §2.5](05a-tgcalls-wire-format.md)).
- `..\vianium-voip\src\ports\outbound\i_tgcalls_media_graph.h` — outbound port the engine talks to; Path C makes this real instead of a placeholder.
- `..\vianium-voip\src\tgcalls\src\tgcalls_backend.cpp` · `tgcalls_backend.h` — placeholder DLL; Path C-3 replaces the placeholder with a real `tgcalls::Instance` driver.
- `..\vianium-voip\src\tgcalls\src\tgcalls_stub.cpp` — DLL exports; unchanged in Path B, expanded in Path C-3.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\EncryptedConnection.cpp` — outer envelope reference (lines 101-155).
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\CryptoHelper.cpp` — KDF reference (lines 8-58).
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\Instance.h` — `Descriptor` callbacks Path C-3 must wire up.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\Signaling.h` · `Signaling.cpp` — JSON message variants Path B logs and Path C-3 produces.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\ReflectorPort.cpp` — modern reflector framing; relevant only in Path C-2 when we implement the WebRTC-side reflector port.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\NativeNetworkingImpl.cpp` · `DirectNetworkingImpl.cpp` — DTLS transport setup; relevant only in Path C-2.
- `docs\native-port\05-voip.md` — existing layer-92 protocol notes (background reading).
- `docs\native-port\05a-tgcalls-wire-format.md` — wire-format reference for everything tgcalls v2 (this strategy's companion).
