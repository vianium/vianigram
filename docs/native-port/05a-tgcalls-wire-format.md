# tgcalls v2 wire-format reference

**Status:** technical reference · **Last reviewed:** 2026-05-03 · **Audience:** anyone touching `TgcallsSignalingCodec`, the `VianiumTgcalls*` C ABI, or future DTLS/SRTP/ICE work.

> Companion: [05-voip-strategy.md](05-voip-strategy.md) — strategy / why this matters; this document is the load-bearing technical detail behind §2 of that doc.

This document collects the on-the-wire formats Vianigram needs to understand to interoperate with tgcalls v2 peers. It is purely reference material: every section cites the upstream Telegram Android source under `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\`.

Sections:

- [2.1 Outer envelope (msg_key + AES-256-CTR)](#21-outer-envelope-from-encryptedconnectioncpp)
- [2.2 Inner payload — v2 Signaling JSON schema](#22-inner-payload--v2-signaling-json-schema)
- [2.3 DTLS-SRTP layer](#23-dtls-srtp-layer)
- [2.4 Reflector port protocol](#24-reflector-port-protocol)
- [2.5 TgcallsBackend C ABI deltas for Path C](#25-tgcallsbackend-c-abi-deltas-for-path-c)

---

## 2.1 Outer envelope (from `EncryptedConnection.cpp`)

The outer envelope is the same shape for control packets, raw media, and signaling — only the `x` offset into the 256-byte shared key changes.

**Reference:** `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\EncryptedConnection.cpp:119-155` (`decryptRawPacket`) and `CryptoHelper.cpp:8-58` (`PrepareAesKeyIv`, `AesProcessCtr`).

### 2.1.1 Packet layout on the wire

```
+----------------+------------------------------+
| msg_key  (16B) | encrypted body (>= 5 bytes)  |
+----------------+------------------------------+
                 ^
                 body decrypts under AES-256-CTR with key/IV derived
                 from (sharedKey, msg_key, x)

After CTR decryption, body is:
+----------------+------------------------------+
| seq      (4B)  | inner plaintext (msg or raw) |
+----------------+------------------------------+
   big-endian       caller-defined; for v2
                    signaling this is JSON
                    (see §2.2)
```

Minimum size before rejection: 21 bytes (16 msg_key + 4 seq + ≥ 1 byte body) — see line 120.

### 2.1.2 The `x` offset (the “+128 for signaling” rule)

From `EncryptedConnection.cpp:124`:

```cpp
const auto x = (_key.isOutgoing ? 8 : 0) + (_type == Type::Signaling ? 128 : 0);
```

- `_key.isOutgoing` is `true` on the side that initiated the call.
- `_type == Type::Signaling` is true for packets that come in via `updatePhoneCallSignalingData` (the modern out-of-band channel) rather than the reflector data path.

This produces four `x` values used as offsets into the 256-byte shared key when deriving AES key + IV:

| isOutgoing | Type      | x   | Notes |
|:---:|:---:|:---:|---|
| false | Control   |   0 | Classic non-initiator, on-reflector. |
| true  | Control   |   8 | Classic initiator, on-reflector. |
| false | Signaling | 128 | Modern non-initiator, off-reflector via TL update. |
| true  | Signaling | 136 | Modern initiator, off-reflector via TL update. |

When **we decrypt incoming signaling** in Path B, we use the *peer's* `isOutgoing` (which is the inverse of ours) plus `+128`. So if we are the caller (`localIsOutgoing = true`), the peer is the callee (`peerIsOutgoing = false`), and our incoming-signaling `x = 0 + 128 = 128`. If we are the callee, our incoming-signaling `x = 8 + 128 = 136`.

### 2.1.3 Decrypt algorithm in pseudocode

```text
fn decrypt_outer(shared_key[256], msg_key[16], encrypted[N], peer_is_outgoing, type) -> plaintext or error:
    x = (peer_is_outgoing ? 8 : 0) + (type == SIGNALING ? 128 : 0)

    # KDF — see CryptoHelper.cpp:8-27
    sha_a = SHA256( msg_key || shared_key[x .. x+36] )                  # 16 + 36 = 52 input bytes
    sha_b = SHA256( shared_key[x+40 .. x+76] || msg_key )

    aes_key  = sha_a[0..8]   || sha_b[8..24]   || sha_a[24..32]         # 32 bytes
    aes_iv   = sha_b[0..4]   || sha_a[8..16]   || sha_b[24..28]         # 16 bytes

    # Decrypt body — CryptoHelper.cpp:29-58
    decrypted = AES256_CTR(aes_key, aes_iv, encrypted)                  # same length as input

    # Verify — EncryptedConnection.cpp:138-143
    msg_key_large = SHA256( shared_key[x+88 .. x+120] || decrypted )    # 32 + N input bytes
    expected_msg_key = msg_key_large[8..24]                             # middle 16 bytes
    if constant_time_eq(expected_msg_key, msg_key) is false:
        return DecryptError::msg_key_mismatch

    # Strip 4-byte big-endian seq prefix — line 152-153
    seq = read_u32_be(decrypted[0..4])
    return Plaintext{ seq, body = decrypted[4..] }
```

Notes:

- AES is **CTR-256** here, not AES-IGE. The classic layer-92 stack (in sibling `..\vianium-voip\src\domain\voip_packet_crypto.cpp`) uses AES-IGE; the modern outer envelope does not. CTR mode is what we need to add for Path B.
- The msg_key check uses the *middle* 16 bytes of a SHA-256 (`[8..24]`), not the first or last 16. This is the same MTProto2 idiom already implemented in the sibling `vianium-crypto`.
- Constant-time compare is required (`ConstTimeIsDifferent`, line 62-70). Do not short-circuit.

### 2.1.4 Encrypt algorithm in pseudocode

(Symmetric to decrypt; needed for Path C-3, not Path B.)

```text
fn encrypt_outer(shared_key[256], plaintext[M], local_is_outgoing, type, seq_counter) -> packet:
    x = (local_is_outgoing ? 8 : 0) + (type == SIGNALING ? 128 : 0)

    body_in = u32_be(seq_counter) || plaintext                          # 4 + M bytes

    msg_key_large = SHA256( shared_key[x+88 .. x+120] || body_in )
    msg_key = msg_key_large[8..24]                                      # 16 bytes

    aes_key, aes_iv = derive_via_KDF(shared_key, msg_key, x)            # same KDF as decrypt

    body_out = AES256_CTR(aes_key, aes_iv, body_in)
    return msg_key || body_out
```

---

## 2.2 Inner payload — v2 Signaling JSON schema

After the outer envelope is removed, the plaintext for `Type::Signaling` packets is **UTF-8 JSON** with a discriminator field `@type`.

**Reference:** `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\Signaling.h:138-185` and `Signaling.cpp:361-810`.

### 2.2.1 Variant catalogue

The `Message::serialize` / `Message::parse` switch (Signaling.cpp:745-810) recognises exactly four `@type` values in the v2 source we vendored:

| `@type` value         | C++ struct                | Direction | Purpose |
|---|---|---|---|
| `"InitialSetup"`      | `InitialSetupMessage`     | both, sent at start | DTLS fingerprints + ICE ufrag/pwd |
| `"NegotiateChannels"` | `NegotiateChannelsMessage`| both, repeated      | media descriptions (audio + optional video) |
| `"Candidates"`        | `CandidatesMessage`       | both, repeated      | ICE candidates for trickle |
| `"MediaState"`        | `MediaStateMessage`       | both, mid-call      | mute/video/screencast/battery state |

Variants observed in the Android client codebase but **not present in the v2 `Signaling.cpp`** we vendored (TBD — investigate before C-3):

- `"connection"` / `"ConnectionState"` — TBD: not located in references; investigate before C-3. Older / newer revisions of tgcalls have a connection-quality message; the current `Stats.h` reports stats out-of-band via callbacks instead, so this may be obsolete.
- `"negotiateChannels"` (lowercase) — the wire spelling in `Signaling.cpp:447` is `"NegotiateChannels"` (PascalCase); a lowercase variant has been seen in older clients. Path B logger should accept both case-insensitively.

### 2.2.2 Pseudo-schema (JSON-with-comments)

```jsonc
// 1) Initial setup — DTLS fingerprints and ICE credentials.
//    Signaling.cpp:361-442
{
  "@type": "InitialSetup",
  "ufrag": "abc123",                       // ICE username fragment
  "pwd": "DEF...",                         // ICE password
  "renomination": false,                   // optional, defaults false
  "fingerprints": [
    {
      "hash": "sha-256",                   // hash algorithm
      "setup": "actpass",                  // DTLS role: "actpass" | "active" | "passive"
      "fingerprint": "AA:BB:CC:..."        // colon-separated hex of cert digest
    }
    // multiple entries allowed
  ]
}

// 2) Candidates — ICE trickle.
//    Signaling.cpp:530-580
{
  "@type": "Candidates",
  "candidates": [
    {
      "sdpString": "candidate:842163049 1 udp 1677729535 1.2.3.4 50000 typ srflx ..."
      // Standard SDP candidate line, opaque to tgcalls
    }
  ]
}

// 3) NegotiateChannels — media description (codecs, SSRCs, RTP extensions).
//    Signaling.cpp:444-500
{
  "@type": "NegotiateChannels",
  "exchangeId": "12345",                   // u32 as string OR number
  "contents": [
    {
      "type": "audio",                     // "audio" | "video"
      "ssrc": "3735928559",                // u32 as string OR number
      "ssrcGroups": [
        { "semantics": "FID", "ssrcs": ["1","2"] }
      ],
      "payloadTypes": [
        {
          "id": 111,
          "name": "opus",
          "clockrate": 48000,
          "channels": 2,
          "feedbackTypes": [ { "type": "transport-cc", "subtype": "" } ],
          "parameters": { "minptime": "10", "useinbandfec": "1" }
        }
      ],
      "rtpExtensions": [
        { "id": 1, "uri": "urn:ietf:params:rtp-hdrext:ssrc-audio-level" }
      ]
    }
  ]
}

// 4) MediaState — mid-call updates.
//    Signaling.cpp:582-743
{
  "@type": "MediaState",
  "muted": false,
  "lowBattery": false,
  "videoState": "inactive",                // "inactive" | "suspended" | "active"
  "videoRotation": 0,                      // 0 | 90 | 180 | 270
  "screencastState": "inactive"            // same enum as videoState
}
```

Path B's logger must (a) accept both string and number for `ssrc` / `exchangeId` (Signaling.cpp:289-296, 470-477), (b) tolerate missing optional fields, and (c) treat unknown `@type` values as "unsupported, count it" rather than as an error.

---

## 2.3 DTLS-SRTP layer

WebRTC requires a DTLS handshake to derive SRTP master keys before any RTP can flow. tgcalls v2 reuses this directly.

**References:**

- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\NativeNetworkingImpl.cpp:11-14` — pulls in `p2p/base/dtls_transport.h`, `p2p/base/dtls_transport_factory.h`, `pc/dtls_transport.h`.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\DirectNetworkingImpl.cpp:11-14` — same set of DTLS imports.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\NativeNetworkingImpl.cpp:517` and `:767` — local certificate generation: `rtc::RTCCertificateGenerator::GenerateCertificate(rtc::KeyParams(rtc::KT_ECDSA), absl::nullopt)`.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\ContentNegotiation.cpp:203, 340, 371, 399, 573, 610, 641, 672` — additional ECDSA certificate generation sites.
- `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\DirectNetworkingImpl.cpp:399, 486` — DTLS cert generation in direct-connection mode.

### 2.3.1 DTLS version

The vendored tgcalls source delegates DTLS version selection to libwebrtc, which since the M120 era of WebRTC defaults to DTLS 1.2 with a forced minimum and tries DTLS 1.3 as the maximum on supported builds. tgcalls v2 does not explicitly pin a version. **TBD: confirm exact min/max DTLS version applied at the libwebrtc DtlsTransport configuration site before Phase C-1 picks a TLS library.**

### 2.3.2 Certificate type

**ECDSA, not RSA-2048.** Every certificate-generation call site we found uses `rtc::KeyParams(rtc::KT_ECDSA)` (the libwebrtc default for `KT_ECDSA` is P-256). The certificates are self-signed and per-call (a new pair is generated on each call setup, not persisted). The fingerprint in the `InitialSetup` message (§2.2.2) is over this ephemeral cert.

### 2.3.3 Fingerprint hash

Path B logs will tell us empirically what `hash` strings real peers send. The known-supported set is `"sha-256"`, `"sha-384"`, `"sha-512"`, `"sha-1"` (last one deprecated; libwebrtc rejects it for outbound). Default for libwebrtc is `"sha-256"`.

### 2.3.4 SRTP cipher

**TBD: not located in references; investigate before C-3.** The vendored tgcalls v2 sources do not call `SetSslMaxProtocolVersion` or configure an explicit SRTP profile; libwebrtc's `pc/srtp_session.cc` defaults are used. As of the libwebrtc revision Telegram tracks, the default SRTP profile preference is:

1. `SRTP_AEAD_AES_128_GCM` (preferred)
2. `SRTP_AEAD_AES_256_GCM`
3. `SRTP_AES128_CM_SHA1_80` (legacy fallback)

Phase C-1 must verify which of these libwebrtc actually negotiates against current Telegram peers, and confirm that mbedTLS DTLS-SRTP exposes the matching `use_srtp` profile codes.

---

## 2.4 Reflector port protocol

Modern tgcalls v2 still uses Telegram-operated reflectors (the same machines listed in `phone.phoneCall.connections`) but now over a **TURN-like custom protocol** rather than the layer-92 raw-Opus framing.

**Reference:** `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\v2\ReflectorPort.cpp`.

### 2.4.1 peer_tag

Each ReflectorPort owns a 16-byte `peer_tag_` constructed from the credentials and a per-port random tag — `ReflectorPort.cpp:160-162, 204-205`:

```cpp
auto rawPeerTag = parseHex(args.config->credentials.password);   // 16 bytes from TL response
peer_tag_.AppendData(rawPeerTag.data(), rawPeerTag.size() - 4);  // first 12 bytes
peer_tag_.AppendData((uint8_t *)&randomTag_, 4);                 // local 32-bit tag at offset 12
```

The first 12 bytes uniquely identify the **call session** (shared with the peer); the trailing 4 bytes uniquely identify **this port** (different on each side). The reflector matches on the first 12 bytes to find the peer, then strips/swaps the last 4 to address the recipient.

### 2.4.2 Hello / ping (registration)

UDP hello — `ReflectorPort.cpp:295-333`:

```text
[ peer_tag (16B) ][ 0xff × 12 ][ 0xfe ][ 0xff × 3 ][ 123 as u64 LE ][ pad to 4-byte boundary ]
```

That is: peer_tag, then `0xffffffff_ffffffff_ffffffff` (12 bytes of FF), then a single `0xfe`, then `0xff_ff_ff`, then the literal `123` as a little-endian u64. It is sent every 500 ms while in `STATE_CONNECTED` and every 10 s while in `STATE_READY` until a peer-tag-matching reply arrives.

TCP hello — `ReflectorPort.cpp:304-314`:

```text
[ peer_tag (16B) ][ 0u32 ][ pad to 4-byte boundary ]
```

### 2.4.3 SendTo (data path)

Outgoing data packet — `ReflectorPort.cpp:579-643`:

```text
[ targetPeerTag (16B) ][ randomTag_ (4B) ][ size as u32 LE ][ payload ][ pad to 4-byte boundary ]

Where targetPeerTag = peer_tag_[0..12]  ||  resolvedPeerTag (4B from synthetic hostname)
```

The "synthetic hostname" is a string of the form `reflector-{serverId}-{remoteRandomTag}.reflector` that libwebrtc's ICE layer hands to `SendTo`; tgcalls parses out the remote peer's `randomTag` from the hostname (lines 593-617) and substitutes it into the peer_tag. This is how the same UDP socket multiplexes over multiple peers when ICE has multiple candidate pairs to the same reflector.

### 2.4.4 Receive validation

Incoming packet handling — `ReflectorPort.cpp:650-710`:

- Discard if `size < 16` (too short for peer_tag).
- Discard if remote address is not the configured server (no spoofing).
- Discard if `state == STATE_DISCONNECTED`.
- Compare the first 12 bytes of `peer_tag` against ours; reject if different (line 691). The trailing 4 bytes are the *remote's* randomTag and intentionally differ.

Note: there is **no AES-CTR layer at the reflector port itself** — the reflector is just framing. End-to-end confidentiality comes from the DTLS-SRTP layer (§2.3) once ICE chooses this candidate pair. The peer_tag alone is what the reflector uses to pair the two clients; it is a session-binding token, not a key.

---

## 2.5 TgcallsBackend C ABI deltas for Path C

Current ABI surface — sibling `..\vianium-voip\src\tgcalls\src\tgcalls_backend.h` and `..\vianium-voip\src\ports\outbound\tgcalls_native_abi.h:1-136`:

```c
// Inputs
struct VianiumTgcallsStartDescriptor { /* CallId, SharedKey, Endpoints, ... */ };
typedef void (__stdcall *VianiumTgcallsSignalingDataProducedCallback)(...);

// Methods
TgcallsBackend::Start(descriptor, outResult)
TgcallsBackend::ReceiveSignalingData(callId, data, size, outResult)
TgcallsBackend::Stop(callId, outResult)
TgcallsBackend::SetMuted(callId, muted, outResult)
TgcallsBackend::SetSpeaker(callId, on, outResult)
TgcallsBackend::GetSnapshot(callId, outSnapshot, outResult)
```

When Phase C-3 wraps a real `tgcalls::Instance::construct(Descriptor &&)` (see `_references\telegram-android\TMessagesProj\jni\voip\tgcalls\Instance.h:223-248`), additional callbacks need to cross the C ABI in the **C → managed** direction. Each maps directly to a `Descriptor::*` field in `Instance.h`:

| Proposed callback                               | Maps to (`Instance.h`) | Purpose |
|---|---|---|
| `VianiumTgcallsSignalingDataEmittedCallback(callId, data[], size)` | `signalingDataEmitted` (line 240) | Outbound bytes to ship via `phone.sendSignalingData` (the inverse of the existing `ReceiveSignalingData` input). |
| `VianiumTgcallsStateUpdatedCallback(callId, state: i32)`           | `stateUpdated` (line 234) | Call FSM transitions: `WaitInit=0`, `WaitInitAck=1`, `Established=2`, `Failed=3`, `Reconnecting=4` (matches `enum class State` at line 142). |
| `VianiumTgcallsSignalBarsUpdatedCallback(callId, bars: i32)`       | `signalBarsUpdated` (line 235) | 0..4 bars for UI signal indicator. |
| `VianiumTgcallsRemoteMediaStateUpdatedCallback(callId, audio: i32, video: i32)` | `remoteMediaStateUpdated` (line 238) | Peer mute / video state for UI. `audio: 0=Muted, 1=Active`; `video` per the (forward-declared) `enum class VideoState`. |
| `VianiumTgcallsAudioLevelsUpdatedCallback(callId, inboundDb: f32, outboundDb: f32)` | `audioLevelsUpdated` (line 236) | Visual audio meter; signature matches the `std::function<void(float, float)>` in `Instance.h`. |
| `VianiumTgcallsRemoteBatteryLowCallback(callId, low: u8)`          | `remoteBatteryLevelIsLowUpdated` (line 237) | UI hint that the peer is on low battery. |
| `VianiumTgcallsRemotePreferredAspectRatioCallback(callId, ratio: f32)` | `remotePrefferedAspectRatioUpdated` (line 239) | Video-only; deferred until Phase 7. |

Each new callback must be added as a function-pointer field in `VianiumTgcallsStartDescriptor` (or in a sibling `VianiumTgcallsCallbacks` struct passed alongside it) to keep the ABI version-bumped (`VIANIUM_TGCALLS_ABI_VERSION` increments to 2). The `userData` opaque pointer pattern already used by `VianiumTgcallsSignalingDataProducedCallback` should be reused so each callback can carry the same context.

The Phase C-3 backend implementation under sibling `..\vianium-voip\src\tgcalls\src\` will own a `tgcalls::Descriptor` per active call, fill its callbacks with thunks that marshal back to these C ABI function pointers, and call `tgcalls::Meta::Create("3.0.0", std::move(descriptor))` (or the appropriate version string from `descriptor->LibraryVersions`) to construct the `Instance`.

The architectural note in sibling `..\vianium-voip\src\tgcalls\src\tgcalls_backend.cpp:37-45` (project-cycle problem with `Vianium.Voip.dll`) still applies: the recommended resolution is to extract `..\vianium-voip\src\domain\` and `..\vianium-voip\src\infrastructure\` into a static lib that both `Vianium.Tgcalls.dll` (the tgcalls v2 driver inside `vianium-voip`) and `Vianium.Voip.dll` can link, so the modern backend can reuse `WinrtVoipAudioDevice`, `OpusVoipCodec`, and `VoipPacketCrypto` without forming a build cycle.
