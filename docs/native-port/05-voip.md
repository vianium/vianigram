# Vianium.VoIP — Telegram 1:1 Voice Calls

**Status:** in progress (native DH + reflector UDP + packet crypto built; audio pending) · **Last reviewed:** 2026-05-04

> Required prior reading:
> - [architecture-principles.md](architecture-principles.md) — toolchain, DDD+hex, WinMD ABI rules, memory budgets.
> - [00-overview.md](00-overview.md) — context map.
> - [03-crypto.md](03-crypto.md) — AES primitives and key-safety invariants.
> - [04-media.md](04-media.md) — shared Opus codec (decoder; the encoder is exclusive to Voip).
> - `..\security\mtproto-policy.md` — session key invariants.

`Vianium.VoIP` implements Telegram's 1:1 voice call protocol. **It is not standard SIP/WebRTC**: at layer 92 it uses a custom protocol over a UDP/TCP relay. The short relay packet carries `peer_tag + msg_key + AES-IGE(ciphertext)` and derives `aes_key/aes_iv` with KDF2 over the initial DH coordinated via MTProto (see `phone.requestCall`, `phone.acceptCall`, `phone.confirmCall` in TL layer 214).

The context is designed voice-only for the MVP (Phase 6). Video (H.264) is deferred to Phase 7+ behind a feature flag.

---

## 1. Bounded context

### Ubiquitous language

| Term | Definition |
|---------|------------|
| **call_id** | A unique identifier assigned by the server when the call starts. Long. |
| **call_session** | The runtime state of an active call: peers, codec config, jitter buffer, AEC state. |
| **voip_packet** | A Telegram UDP/TCP relay packet: `peer_tag`, `msg_key`, an AES-IGE payload, and an internal type (`PKT_INIT`, `PKT_STREAM_DATA`, etc.). |
| **jitter_buffer** | An adaptive buffer that absorbs variation in the inter-arrival times of Opus frames. |
| **plc** | Packet Loss Concealment — audio synthesis for lost frames. |
| **aec** | Acoustic Echo Cancellation — removes echo from the speaker captured by the mic. |
| **stun_candidate** | A candidate endpoint (IP+port) for NAT traversal: host, server-reflexive, relay. |
| **dh_session_key** | A key derived from the MTProto DH during setup; derives `msg_key`, `aes_key`, `aes_iv` for VoIP packets. |
| **rekey** | A DH renegotiation mid-call for PFS (perfect forward secrecy). |

### Aggregate root

`VoipSession` (in `domain/aggregates/voip_session.h`):
- Identified by `call_id`.
- Owns: a codec instance, jitter buffer, AEC state, RTP socket, encryption key, sequence counters, statistics.
- Life cycle: `Idle` → `Connecting` → `Active` → `Disconnecting` → `Idle`.
- Invariants:
  - Only one `VoipSession` active simultaneously (Telegram clients do not support multi-call).
  - Encryption key destroyed (CtZeroize) on `Disconnect`.
  - Jitter buffer cap 500 ms (defensive — more is a bug).

### Domain events

- `Connecting{ call_id, peer_id }`.
- `Connected{ call_id, codec, sample_rate, latency_estimate_ms }`.
- `Disconnected{ call_id, reason, duration_seconds }`.
- `Muted{ call_id }` / `Unmuted{ call_id }`.
- `RemoteAudioFrame{ samples_int16_count }` (to the UI/audio device adapter).
- `LocalLevelMeasured{ peak_dbfs }` (visual feedback to the user).
- `BandwidthShifted{ old_bitrate, new_bitrate, reason }`.
- `JitterBufferAdapted{ old_target_ms, new_target_ms }`.

---

## 2. Telegram VoIP protocol overview

**No SIP, no WebRTC.** A custom protocol designed for low-latency voice with strong encryption. Setup goes via MTProto:

```
Caller side:
  1. messages.requestCall  → server
       g_a_hash:bytes32 = SHA-256(g_a)    // commits to caller's DH public
  2. server forwards to callee
  3. Callee: messages.acceptCall →
       g_b:bytes256                        // callee's DH public
  4. Caller computes shared = g_b^a mod p
  5. Caller: messages.confirmCall →
       g_a:bytes256                        // reveals (callee verifies SHA-256(g_a) match)
  6. Callee computes shared = g_a^b mod p
  7. Both sides have same shared secret 256 bytes
  8. Derive per-direction keys via SHA-256 of shared:
       caller_send_key = SHA-256(shared || "send_caller")[0..31]
       caller_recv_key = SHA-256(shared || "send_callee")[0..31]
       (similar for callee, symmetric)
```

**Encryption:** Telegram VoIP MTProto2-short: `msg_key = SHA256(shared_key[88+x..120+x] || inner_packet)[8..24]`; `KDF2(msg_key, x)` derives the AES-256 key/IV; the `inner_packet` is encrypted with AES-IGE. `x=0/8` depends on the direction and the call initiator.

**Transport:** UDP (a dynamic port negotiated at setup). The server acts as a STUN-style relay — the clients discover the server's reflexive endpoints (`phone.connection`).

**Frame size:** 20 ms Opus frames @ 48 kHz mono = 960 samples = 16-32 kbps.

---

## 3. Telegram relay packet format

```
UDP relay packet:
  peer_tag[16]
  msg_key[16]
  aes_ige(inner_packet_padded)

inner_packet_padded:
  len:uint16 little-endian
  packet bytes
  random padding so total encrypted length is a multiple of 16
```

**Notes:**
- Layer 92 peers use the short `uint16` length form.
- Relay discovery uses special unencrypted peer-tag packets before encrypted
  media begins.
- Internal packet types follow Telegram VoIP constants (`PKT_INIT`,
  `PKT_INIT_ACK`, `PKT_STREAM_DATA`, `PKT_PING`, `PKT_PONG`, etc.).

---

## 4. Jitter buffer

### Adaptive target

The jitter buffer keeps a `target_ms` that adjusts to network conditions:

```
on_packet_arrived(seq, arrival_time):
    inter_arrival = arrival_time - last_arrival_time
    expected = 20 ms (frame size)
    jitter = abs(inter_arrival - expected)

    smoothed_jitter = 0.95 * smoothed_jitter + 0.05 * jitter
    target_ms = clamp(60, 180, 1.5 * RTT_estimate + 4 * smoothed_jitter)
```

**Bounds:**
- Min target: 60 ms (the minimum acceptable latency).
- Max target: 180 ms (anything more = audible delay).
- Hard defensive cap: 500 ms (bug guard).

### Late packet drop

If a packet arrives when its `playout_deadline` has already passed, it is discarded (counter `voip.jitter.late_drops_total`).

### PLC (Packet Loss Concealment)

If a slot in the jitter buffer is empty at the `playout_deadline`, generate synthetic audio:

- **Method 1**: internal Opus PLC (`opus_decoder_decode(... NULL, 0, ...)` with a NULL packet → libopus generates a frame by loss concealment using its internal state).
- **Method 2 (advanced)**: linear extrapolation with the last 2 frames; useful when Opus PLC is not sufficient.

The Vianigram MVP uses Method 1 (libopus built-in).

### Implementation

```
src/domain/services/jitter_buffer.{h,cpp}
```

```cpp
class JitterBuffer {
public:
    JitterBuffer(IAllocator* alloc);

    void Insert(uint16_t seq, uint32_t timestamp, Span<const uint8_t> opus_packet,
                int64_t arrival_monotonic_ms);

    Result<JitterFrame, MediaError> NextPlayoutFrame(int64_t now_monotonic_ms);
    // JitterFrame contains: opus_packet (or null if PLC), seq, timestamp.

    int CurrentTargetMs() const;
    int CurrentDepthMs() const;
    int LateDropsTotal() const;
};
```

---

## 5. Audio codec — Opus 48 kHz mono

### Encoder (Voip-specific)

`Vianigram.Core.Media` only decodes Opus for voice notes. The encoder is additionally vendored in `Vianium.VoIP` specifically for calls:

```
src/infrastructure/codec/opus_encoder_voip.{h,cpp}
```

**Configuration:**
- Sample rate: 48 kHz.
- Channels: 1 (mono).
- Frame size: 20 ms = 960 samples.
- Bitrate: 16-32 kbps adaptive.
- Application: `OPUS_APPLICATION_VOIP` (optimizes for speech).
- Complexity: 5 (medium; reduces CPU on a Snapdragon S4).
- DTX: disabled (constant bitrate; simplifies the jitter buffer).
- FEC (Forward Error Correction): enabled; ~10% overhead but tolerates isolated packet loss.
- VBR: disabled (a constant bitrate is more predictable for jitter management).

### Decoder

Reuses `Vianigram::media::infrastructure::OpusDecoderAdapter` but with sample rate 48000.

### Memory budget

- Encoder state: ~30 KB.
- Decoder state: ~24 KB.
- Codebooks: ~70 KB (shared with the media decoder; static).
- Total: ~125 KB per active session.

---

## 6. Echo cancellation (AEC)

### Approach

A WebRTC AEC port (BSD-3-clause via the WebRTC project). Subset:

```
src/infrastructure/codec/webrtc_aec/
├── aec3/                  ← AEC3 (newer, better)
├── aec/                   ← AEC2 (older fallback if AEC3 unavailable)
├── aec_resampler.{h,c}
└── (selective compile: only what is needed for single-channel mono 48 kHz)
```

**Configuration:**
- Single-channel (mono).
- Frame size 256 samples (separate from Opus 960 — AEC processes in 16ms blocks at 48 kHz).
- Internal resampler if the input ≠ 48 kHz (some Windows audio hardware uses 44.1 kHz).

**Memory budget:** ~256 KB AEC state.

**Implementation:**
```
src/infrastructure/codec/aec_adapter.{h,cpp}
```

```cpp
class AecAdapter {
public:
    AecAdapter(IAllocator* alloc, int sample_rate = 48000);
    ~AecAdapter();

    // Far-end is what we render to speaker (incoming remote audio).
    void PushFarEnd(Span<const int16_t> far_samples);

    // Near-end is what mic captures (echo + local voice).
    // Returns echo-cancelled output ready to encode and send.
    Result<Span<int16_t>, MediaError> ProcessNearEnd(
        Span<const int16_t> near_samples, IAllocator* outAlloc);

    // Convergence indicator (UI can show "echo training" if low).
    float ErleEstimate() const;     // Echo Return Loss Enhancement, in dB

private:
    void* m_aecState;          // WebRTC AEC opaque state
};
```

---

## 7. Network adaptation

### Bandwidth probing

Telegram VoIP does not use formal congestion control (no Google Congestion Control, no GCC). A simple approach:

```
on_packet_loss_observed(loss_rate):
    if loss_rate > 0.05:
        downshift codec bitrate by 2 kbps (min 8 kbps)
        emit BandwidthShifted{ reason: HighLoss }
    elif loss_rate < 0.01 and current_bitrate < max_bitrate:
        upshift codec bitrate by 2 kbps (max 32 kbps)
        emit BandwidthShifted{ reason: GoodLink }
```

Window: a 5-second moving average of the loss rate. Update every 5 seconds.

### Pacing

Simple outbound packet pacing: send each frame exactly when its 20 ms slot expires (no bursting). Reduces UDP buffer congestion at the gateway.

### RTT estimation

RTT estimated via piggyback over the Telegram extension header `flags`/ack bytes:

```
on_packet_sent(seq, timestamp_now):
    track in m_unacked map
on_ack_received(seq, ack_timestamp):
    sample = ack_timestamp - m_unacked[seq]
    smoothed_rtt = 0.875 * smoothed_rtt + 0.125 * sample
```

Used for the jitter buffer target (`target_ms = 1.5 * RTT + 4 * smoothed_jitter`).

---

## 8. NAT traversal — STUN-style ICE

### Candidate gathering

The Telegram server acts as a STUN-style relay. Setup flow:

```
1. Client: phone.getCallConfig → the server returns a list of relay endpoints (host:port × N).
2. Client: for each endpoint, send a "ping" UDP packet → if there is a response, the candidate is viable.
3. Client: orders the candidates by preference (host > srflx > relay) and by RTT.
4. Client sends the list to the peer via MTProto signaling.
5. The peer does the same process, the lists are exchanged.
6. Both sides connectivity-check the crossed pairs; uses the first that responds in both directions.
```

### Fallback to TCP relay

If UDP is completely blocked (a corporate firewall, restrictive NAT), fall back to a TCP relay:

```
phone.connection
  - flags.0 specifies UDP candidate
  - flags.1 specifies TCP candidate
```

A TCP relay has higher latency (not as low-latency as UDP), but it guarantees connectivity.

### Implementation

```
src/infrastructure/transport/voip_udp_socket.{h,cpp}
src/infrastructure/transport/voip_tcp_fallback.{h,cpp}
src/domain/services/ice_candidate_picker.{h,cpp}
```

---

## 9. Latency target

| Source | Estimated latency |
|--------|-------------------|
| Mic capture buffer (`Windows.Media.Capture`) | 20-40 ms |
| AEC processing | 5-10 ms |
| Opus encode | 5 ms |
| Network one-way (LTE typical) | 50-100 ms |
| Jitter buffer | 60-180 ms |
| Opus decode | 2 ms |
| Audio render buffer | 20-40 ms |
| **Total mouth-to-ear** | **160-380 ms typical, 100-250 ms LTE good** |

**Vianigram MVP target:** mouth-to-ear ≤ 250 ms on good LTE, ≤ 400 ms on 3G. Compatible with the perception of a "real-time conversation" (the usual cap is 400 ms).

**Telemetry watches:** `voip.latency.mouth_to_ear_ms` p50/p95.

---

## 10. Memory budget — 8 MB hot

| Component | Memory |
|-----------|---------|
| Opus encoder + decoder + codebooks | 125 KB |
| Jitter buffer (180 ms × 16-bit × 48 kHz) | ~17 KB raw + slot metadata + Opus packet storage ≈ 80 KB |
| AEC state (WebRTC AEC3) | 256 KB |
| RTP packet pool (32 packets × ~200 B) | 6 KB |
| Network TX/RX buffers + UDP socket state | 64 KB |
| Statistics + per-second telemetry buffer | 16 KB |
| Encryption AES state (round keys, counter) | 1 KB |
| Stack / scratch / arena per packet | 64 KB |
| **Subtotal** | **~700 KB** |
| **Buffer for misc + safety margin** | **~7 MB** |
| **Total** | **~8 MB** |

Phase 7+ video would add ~10 MB extra (H.264 decoder state). Out of scope for the MVP.

---

## 11. File structure under `src/`

```
src/
├── pch.h, pch.cpp
├── domain/
│   ├── value_objects/
│   │   ├── call_id.h
│   │   ├── call_state.h           ← enum: Idle, Connecting, Active, Disconnecting
│   │   ├── ssrc.h
│   │   ├── rtp_packet.h
│   │   ├── ice_candidate.h
│   │   └── voip_stats.h           ← POD with packet loss, RTT, bitrate, ERLE, etc.
│   ├── policies/
│   │   ├── jitter_target_policy.{h,cpp}    ← min 60, max 180, hard cap 500 ms
│   │   ├── bitrate_adaptation_policy.{h,cpp}
│   │   └── max_session_duration_policy.h   ← cap 4 hours defensive
│   ├── events/
│   │   ├── connecting.h
│   │   ├── connected.h
│   │   ├── disconnected.h
│   │   ├── muted.h
│   │   ├── unmuted.h
│   │   ├── remote_audio_frame.h
│   │   ├── local_level_measured.h
│   │   ├── bandwidth_shifted.h
│   │   └── jitter_buffer_adapted.h
│   ├── aggregates/
│   │   └── voip_session.{h,cpp}   ← root: state machine + sub-aggregates
│   └── services/
│       ├── jitter_buffer.{h,cpp}
│       ├── ice_candidate_picker.{h,cpp}
│       ├── rtt_estimator.{h,cpp}
│       ├── loss_estimator.{h,cpp}
│       └── rtp_seq_tracker.{h,cpp}
├── application/
│   ├── use_cases/
│   │   ├── start_call_use_case.{h,cpp}
│   │   ├── accept_call_use_case.{h,cpp}
│   │   ├── reject_call_use_case.{h,cpp}
│   │   ├── disconnect_call_use_case.{h,cpp}
│   │   ├── mute_self_use_case.{h,cpp}
│   │   ├── render_remote_frame_use_case.{h,cpp}      ← jitter pop → decode → AEC far-end push → output
│   │   ├── capture_local_frame_use_case.{h,cpp}      ← mic → AEC near-end → encode → encrypt → send
│   │   └── handle_signaling_message_use_case.{h,cpp}
│   └── command_handlers/
│       └── call_state_machine.{h,cpp}
├── ports/
│   ├── inbound/
│   │   └── i_voip_session.h       ← shape consumed by winrt_shim
│   └── outbound/
│       ├── i_audio_capture.h      ← Windows.Media.Capture wrapper port
│       ├── i_audio_render.h       ← Windows.Media.Audio wrapper port
│       ├── i_voip_udp_transport.h ← UDP socket port
│       ├── i_voip_tcp_transport.h ← TCP fallback port
│       ├── i_voip_crypto.h        ← MTProto2-short KDF2 + AES-IGE packet crypto
│       ├── i_signaling_channel.h  ← MTProto-based signaling for call setup
│       ├── i_clock.h
│       ├── i_logger.h
│       ├── i_telemetry.h
│       └── i_event_bus.h
├── infrastructure/
│   ├── codec/
│   │   ├── opus_encoder_voip.{h,cpp}
│   │   ├── opus/                                ← libopus subset (encoder paths added vs Media)
│   │   ├── aec_adapter.{h,cpp}
│   │   └── webrtc_aec/                          ← WebRTC AEC subset vendored
│   ├── transport/
│   │   ├── voip_udp_socket.{h,cpp}              ← Windows::Networking::Sockets::DatagramSocket
│   │   ├── voip_tcp_fallback.{h,cpp}            ← StreamSocket fallback
│   │   └── rtp_codec.{h,cpp}                    ← parse/build 12-byte RTP + 4-byte ext
│   ├── audio/
│   │   ├── audio_capture_winrt.{h,cpp}          ← Windows.Media.Capture mic input
│   │   └── audio_render_winrt.{h,cpp}           ← Windows.Media.Audio output
│   ├── crypto_adapter.{h,cpp}                    ← wraps Vianium::Crypto::v1::CryptoSession (sibling vianium-crypto)
│   └── platform/
│       └── (empty; the WinRT APIs live in transport/ and audio/)
├── api/
│   └── v1/
│       ├── pch.h
│       ├── c_api.cpp                             ← vng_voip_api.h impl
│       └── winrt_shim.cpp                        ← VoipSession sealed ref class
└── internal/
    ├── voip_log.h
    └── self_test.{h,cpp}
```

---

## 12. Public API surface (WinMD)

```cpp
namespace Vianium::Voip::v1 {       // sibling vianium-voip WinMD

public enum class VoipCallState {
    Idle = 0,
    Connecting,
    Ringing,
    Active,
    Disconnecting,
    Ended,
};

public enum class VoipDisconnectReason {
    Normal = 0,
    PeerHangup,
    PeerBusy,
    PeerNoResponse,
    NetworkLost,
    Timeout,
    LocalError,
    RemoteError,
};

public ref struct VoipStatsWin sealed {
    property int32_t PacketsSent;
    property int32_t PacketsReceived;
    property int32_t PacketsLost;
    property double LossRate;
    property int32_t RttMs;
    property int32_t JitterMs;
    property int32_t CurrentBitrateKbps;
    property double ErleEstimateDb;       // AEC quality
    property int32_t MouthToEarMs;
};

public ref struct AudioFrameWin sealed {
    property Windows::Storage::Streams::IBuffer^ PcmInt16;
    property int32_t SampleRate;
    property int32_t Channels;
};

public ref class VoipSession sealed {
public:
    static VoipSession^ Create(
        Vianium::Crypto::v1::CryptoSession^ crypto,
        Windows::Foundation::TimeSpan inactivityTimeout);

    Windows::Foundation::IAsyncOperation<bool>^ ConnectAsync(
        int64_t callId,
        int64_t peerId,
        Windows::Storage::Streams::IBuffer^ sharedSecret256,
        Windows::Foundation::Collections::IVector<Platform::String^>^ candidateEndpoints);

    Windows::Foundation::IAsyncAction^ DisconnectAsync();

    void MuteSelf();
    void UnmuteSelf();
    bool IsMuted();

    VoipStatsWin^ GetStats();

    property VoipCallState State { VoipCallState get(); }

    event Windows::Foundation::TypedEventHandler<VoipSession^, VoipCallState>^ StateChanged;
    event Windows::Foundation::TypedEventHandler<VoipSession^, AudioFrameWin^>^ RemoteAudioReady;
    event Windows::Foundation::TypedEventHandler<VoipSession^, double>^ LocalLevelChanged;     // peak dBFS
    event Windows::Foundation::TypedEventHandler<VoipSession^, VoipStatsWin^>^ StatsUpdated;
    event Windows::Foundation::TypedEventHandler<VoipSession^, VoipDisconnectReason>^ Disconnected;
};

}
```

---

## 13. Self-tests (DEBUG)

At the first `VoipSession::Create`:

1. **OpusEncoderRoundtrip**: encode a synthetic 1 kHz sine wave → decode → verify amplitude within tolerance.
2. **AecConvergence**: feed an identical signal as far-end and near-end → after 500 ms, the AEC output should be near-zero (ERLE > 20 dB).
3. **JitterBufferAdaptation**: simulate a steady 20 ms inter-arrival, then random ±30 ms jitter; verify `target_ms` adjusts within bounds.
4. **JitterBufferLateDrop**: insert a packet > 500 ms past playout → verify the drop counter increments.
5. **PlcContinuity**: insert 10 packets, drop packet 5, verify PLC produces non-zero output (not silence).
6. **RtpEncodeDecodeRoundtrip**: build an RTP packet, encrypt, decrypt, parse → verify byte-equal payload.
7. **IcePickerOrder**: feed candidates in random order → verify the picker prefers host > srflx > relay → preferred RTT.
8. **CounterCtrAesUniqueness**: encrypt 1000 packets with a monotonic seq → verify all CTR counters are unique (no nonce reuse).
9. **MaxSessionDurationCap**: simulate a 4h+ session → verify the auto-disconnect triggers.
10. **MemoryBudgetCheck**: assert live session memory ≤ 8 MB during stress (1000 RTP packets in flight).

If any of them fails → `VoipSession::Create` returns null + a selftest counter. The managed layer shows "calls unavailable" on failure.

---

## 14. Telemetry

| Metric | Type | When |
|---------|------|--------|
| `voip.session.duration_seconds` | Histogram | Each call ended |
| `voip.session.total_started` | Counter | Each call started |
| `voip.session.disconnect_total{reason}` | Counter | Each disconnect |
| `voip.network.packets_sent_total` | Counter | |
| `voip.network.packets_received_total` | Counter | |
| `voip.network.packets_lost_total` | Counter | |
| `voip.network.loss_rate` | Gauge | |
| `voip.network.rtt_ms` | Histogram | |
| `voip.network.bytes_sent_total` | Counter | |
| `voip.network.bytes_received_total` | Counter | |
| `voip.codec.bitrate_kbps` | Gauge | |
| `voip.codec.bandwidth_shifts_total{reason}` | Counter | |
| `voip.jitter.target_ms` | Gauge | |
| `voip.jitter.depth_ms` | Gauge | |
| `voip.jitter.late_drops_total` | Counter | |
| `voip.aec.erle_db` | Gauge | |
| `voip.aec.convergence_time_ms` | Histogram | First time ERLE > 15 dB |
| `voip.latency.mouth_to_ear_ms` | Histogram | (estimated) |
| `voip.transport.fallback_to_tcp_total` | Counter | UDP failed, fell back |
| `voip.ice.candidate_count{kind}` | Histogram | kind ∈ {host, srflx, relay} |
| `voip.encryption.packets_encrypted_total` | Counter | |
| `voip.plc.frames_synthesized_total` | Counter | |

---

## 15. Risks and mitigations

| Risk | Mitigation |
|--------|------------|
| AEC does not converge → audible echo for the peer | Self-test convergence; UI hint "echo training, please use earphones"; auto-fallback to half-duplex if ERLE < 5 dB after 5s. |
| UDP completely blocked | TCP relay fallback. Telemetry counter to detect prevalence. |
| Continuous jitter buffer underrun (network dying) | After 3 consecutive empty pops, disconnect with `NetworkLost`. |
| VoIP packet crypto mismatch (catastrophic interop/security) | Self-tests cover MTProto2-short KDF2, AES-IGE roundtrip, and tamper rejection; the packet pump must reject a wrong `msg_key` before decode. |
| The Opus encoder consumes too much CPU on a 512 MB ARMv7 device | Complexity setting 5 (medium); fallback to complexity 3 if CPU usage > 70% sustained. |
| WebRTC AEC license drift | BSD-3-clause stable; track WebRTC project upstream commits; the vendored commit hash documented in `THIRD_PARTY_NOTICES.md`. |
| Audio capture format mismatch (44.1 kHz hardware) | The AEC adapter has a resampler; converts on the fly. |
| Memory budget overrun in long calls (4h+) | `max_session_duration_policy` 4h cap; auto-end with reason `Timeout`. |
| Battery drain on a continuous call | Telemetry watches `Windows.System.Power`; if user-low, suggest ending the call. |
| MTProto signaling channel disconnect mid-call | Call signaling is one-shot at setup; an ongoing call only uses direct UDP/TCP. Signaling reconnect is not required mid-call. |
| Privacy: leak voice frames to logs | Macros filter; no audio samples are ever logged. |

---

## 16. Phasing

**Built now:**
- `Vianium.VoIP` native WinRT component is wired into the solution.
- Telegram DH for calls is native and opaque to managed code.
- `Vianigram.Calls` drives `phone.requestCall`, `phone.acceptCall`, and
  `phone.confirmCall` through hex ports.
- `messages.getDhConfig` is fetched/cached by the composition adapter.
- Telegram reflector endpoints (`phoneConnection#9cc123c7`) decode in
  `Vianigram.Calls` and project to native endpoint selection.
- UDP reflector discovery/self-info packet shapes are implemented and a
  WinRT `DatagramSocket` reflector probe is wired behind an outbound port.
- Telegram VoIP MTProto2-short relay packet crypto is implemented:
  `peer_tag`, `msg_key`, KDF2, AES-IGE encrypt/decrypt, and tamper rejection.
- A basic jitter buffer emits packet/PLC slots and tracks late-drop stats.

**Remaining for production call audio:**
- Encrypted packet pump (`PKT_INIT`, `PKT_INIT_ACK`, `PKT_STREAM_DATA`,
  ping/pong, retransmit and endpoint switch).
- Opus encoder/decoder integration.
- Jitter buffer integration with decoded Opus frames and packet-loss
  concealment.
- Audio capture/render via WP8.1 WinRT.
- Echo cancellation and speaker routing.
- End-to-end device validation between two Telegram accounts.

**Phase 7+:**
- Video (H.264) — feature-flagged. Adds an H.264 decoder + camera capture.
- Group calls (Telegram supports up to N peers in voice chats).
- Better congestion control (GCC-style if observation justifies it).
- TGS-like Lottie animations for the call UI (out of scope for native).

**Deferred / out of scope:**
- Screen sharing.
- WebRTC interop (the Telegram protocol is custom; no compatibility planned).
- Call recording (legal and product complexity; out of scope for the MVP).
- Spatial audio / multichannel (mono is fine for MVP voice).
- Hardware echo cancellation via WP8.1 platform APIs (some Windows Phone 8.1 devices have it; if available, the AEC software adapter checks and skips it).

## 13. tgcalls 2.x interop status

Live device testing (Apr 2026) confirms the classic VoIP pipeline at
the sibling `..\vianium-voip\` is operationally complete:

- Phase signaling: ✅ phone.requestCall → phoneCallAccepted → phone.confirmCall
- DH-2048 key exchange: ✅ fingerprint match
- UDP transport + reflector registration: ✅ selfInfo confirmed
- AES-IGE per-packet encryption: ✅ decryptFailures=0
- INIT packet send: ✅ txPackets=8 per endpoint
- INIT_ACK reception: ❌ peer never responds on classic ports

The peer (any modern Telegram client circa 2024+) advertises layer=92 in
the protocol descriptor for backwards compatibility but actually runs
tgcalls 2.x (WebRTC + DTLS-SRTP). Their reflector listener is on port
1400 with DTLS, not on the classic ports 597-599.

Evidence: during the same call window, MTProto delivers
`updatePhoneCallSignalingData` packets totaling 3+ KB. These are
tgcalls 2.x SDP/ICE candidates exchanged out-of-band. A pure classic
libtgvoip peer (the legacy stack vendored at
`..\vianium-voip\third_party\libtgvoip\`) never emits signalingData.

### What works today

Voice calls between two Vianigram clients (both classic). To test:
deploy Vianigram on two WP8.1 devices, log in different Telegram
accounts, call one to the other.

### What's pending

See `05-voip-strategy.md` Path C for the multi-week vendoring work
required to interop with modern Telegram peers (libtgcalls + libwebrtc
subset + DTLS + libsrtp + Opus + ICE + json11). Estimated 7-11 weeks
focused work.

### What's available now (Path B — this milestone)

Sibling `..\vianium-voip\src\domain\tgcalls_signaling_codec.cpp` decrypts
inbound signalingData using the call's shared key. The plaintext is
JSON v2 Signaling format (see `05a-tgcalls-wire-format.md`).
Logged via `[Tgcalls.Signaling]` OutputDebugString lines for diagnostic.

The decryption path validates the algorithm + provides a foothold for
Phase 4.3 implementation.
