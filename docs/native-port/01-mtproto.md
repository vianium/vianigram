# Vianium.Mtproto (`src\mtproto\`) — Telegram MTProto 2.0 Protocol Implementation

**Status:** planned (Phase 1) · **Layer baseline:** 214 · **Last reviewed:** 2026-04-27 · **Sibling repo:** `vianium-mtproto`

> Required prior reading:
> - [architecture-principles.md](architecture-principles.md) — toolchain, DDD+hex, WinMD ABI rules, memory budgets.
> - [00-overview.md](00-overview.md) — context map and dependency graph.
> - [02-tl.md](02-tl.md) — TL serialization that MTProto consumes for its inner messages.
> - [03-crypto.md](03-crypto.md) — crypto primitives (AES-IGE, DH 2048, SHA-256) that MTProto requires.
> - `..\security\mtproto-policy.md` — security invariants enforced by `domain/policies/`.

The `vianium-mtproto` sibling (subcomponent `src\mtproto\`) implements the MTProto 2.0 protocol over TCP. It is the heart of the data plane: every API exchange with Telegram (auth, dialogs, messages, file up/down, channels) goes through this context. Vianigram consumes it via WinMD; this document describes the API and the invariants from Vianigram's perspective.

**Reference spec:** Telegram MTProto 2.0 (https://core.telegram.org/mtproto), validated against the legacy repo `PivoraTelegram/src/PivoraTelegram.Core/` (external reading) to understand which subset is actually used by the app (no code is ported).

---

## 1. Bounded context

### Ubiquitous language

| Term | Definition |
|---------|------------|
| **DC (Data Center)** | Cluster of Telegram servers identified by `dc_id` (1-5 production, +100/+200 test). Each DC has a distinct IP/port and its own `auth_key`. |
| **auth_key** | A 2048-bit (256-byte) key shared between client and DC after the DH handshake. Long-lived. Encrypted at-rest with DPAPI. |
| **auth_key_id** | Lower 64 bits of the SHA-1 of the auth_key. Header of every encrypted MTProto packet. |
| **session** | An MTProto session (random 64-bit `session_id`) within which msg_ids are monotonic and seq_nos are counted. Lasts while the app is alive; reused across reconnects. |
| **server_salt** | A 64-bit value that rotates periodically; the server provides it. Mitigates replay/clock-skew attacks. |
| **msg_id** | Message identifier, monotonic per direction. High bits = unix time × 2^32; low bits derived with a sequence. |
| **seq_no** | Even-only counter for content-related messages, total for acks-only. |
| **container** | `msg_container#73f1f8dc` that groups several messages into a single encrypted packet. |
| **transport** | A TCP framing variant: Abridged, Intermediate, Full. |
| **handshake** | The 2048-bit DH flow that produces `auth_key` (req_pq → req_DH_params → set_client_DH_params). |
| **rpc_call** | A message from the client to the server with a TL constructor that expects an `rpc_result` with the same `msg_id` as `req_msg_id`. |

### Aggregate root

`MTProtoSession` (in `domain/aggregates/mtproto_session.h`):
- Identified by `(dc_id, session_id)`.
- Owns: `auth_key`, `auth_key_id`, `current_salt`, `last_msg_id_sent`, `last_msg_id_received`, `seq_no_outbound`, `replay_window`.
- Invariants:
  - `last_msg_id_sent` strictly increasing.
  - `seq_no_outbound` even-only for content-related, total for acks.
  - `replay_window` rejects an already-seen msg_id (default size 64).
  - `current_salt` updated only in response to `bad_server_salt`.

### Domain events

- `HandshakeCompleted{ dc_id, auth_key_id, server_time, latency_ms }`.
- `RpcDispatched{ msg_id, ctor_id, payload_size }`.
- `RpcAcked{ msg_id, server_time }`.
- `BadServerSalt{ old_salt, new_salt }` — fired on receiving `bad_server_salt#edab447b`.
- `FloodWaitObserved{ method, seconds }`.
- `Reconnected{ dc_id, attempt, backoff_ms }`.
- `ReplayDetected{ msg_id }` — silent drop, but the counter is incremented.

---

## 2. TCP transport variants

MTProto supports several frame variants over TCP. The client and server negotiate at the first byte of the connection.

### Abridged (1 byte length, with optional 4-byte extension)

```
First byte sent (handshake): 0xEF
Subsequent frames:
  - Length byte L:
    - L < 0x7F: payload length = L * 4
    - L == 0x7F: next 3 bytes = uint24 LE, payload length = uint24 * 4
  - Payload: <length> bytes
```

Pros: ~3 bytes overhead per frame in the common case. Minimal wire footprint.
Cons: only allows payloads that are multiples of 4 bytes (consistent with MTProto, which already aligns); a 1-byte length caps at ~508 bytes for small frames.

### Intermediate (4-byte length)

```
First 4 bytes sent (handshake): 0xEEEEEEEE
Subsequent frames:
  - Length: uint32 LE (must be multiple of 4)
  - Payload: <length> bytes
```

Pros: cleanly framed, no length-extension corner case. Trivial to parse correctly.
Cons: 4 bytes overhead vs 1 byte for Abridged.

### Full (with seq + CRC)

```
Frame format:
  - Length: uint32 LE (includes length, seq_no, payload, CRC32)
  - Frame seq_no: uint32 LE (separate from msg seq_no)
  - Payload: variable
  - CRC32: uint32 LE
```

Pros: detects TCP-level corruption (rare but possible on flaky links).
Cons: 12 bytes overhead + CRC32 computation per frame.

### Vianigram decision

**Default: Intermediate.** Reasons:
- Cleaner parsing → fewer bugs.
- Acceptable overhead (4 bytes vs 1) to save complexity.
- WP8.1 over 3G/LTE does not benefit significantly from Abridged's 1-byte relative to the total volume of an encrypted MTProto message (a minimum of 80 bytes).

**Fallback: Abridged.** If a DC reports no support for Intermediate (rare), the transport adapter retry-connects with `0xEF`.

**Full: optional, behind the feature flag** `mtproto.transport.useFullFraming`. Only if telemetry detects repeated TCP corruption on a specific link.

**Implementation files:**
```
src/infrastructure/transport/
├── mtproto_tcp_adapter.{h,cpp}      ← wraps Vianium::Core::Net::v1::SocketTransport^
├── frame_codec.{h,cpp}              ← IFrameCodec interface
├── intermediate_frame_codec.{h,cpp} ← default
├── abridged_frame_codec.{h,cpp}     ← fallback
└── full_frame_codec.{h,cpp}         ← feature-flag, opt-in
```

---

## 3. Connection model

### Connection pool per DC — IMPLEMENTED (Phase 7.1)

Each DC maintains a pool of N independent TCP connections. Default `N=4`. The implementation lives in:
- Sibling `vianium-mtproto\src\mtproto\application\mtproto_session_pool.h`
- Sibling `vianium-mtproto\src\mtproto\application\mtproto_session_pool.cpp`
- Wired into `MtProtoChannel::OpenAsync` at sibling `vianium-mtproto\src\mtproto\api\v1\mtproto_api.cpp`.

**Why multi-connection:**
- Telegram Cloud allows parallelizing file uploads/downloads in blocks (`upload.saveFilePart` per chunk). A single connection is the bottleneck.
- API calls (`messages.getHistory`, `users.getUsers`) and the `updates` long-poll interleave without head-of-line blocking.
- Each connection is independent at the TCP level but **shares** `auth_key`, `auth_key_id`, `server_salt`, and a single `RpcDispatcher` (correlator by `req_msg_id`). The `session_id` is per-socket (each `EncryptedSession` generates its own on construction) — the server treats them as distinct MTProto sessions, but they are indistinguishable to the client.

**Rough budget:** 4 connections × ~2 MB working set = 8 MB. Fits within the context's 12 MB hot budget (the rest = replay window + msg_id history + auth_key cache).

**Multiplexing:** outbound messages are assigned round-robin (atomic `next_index_++` mod `pool_size_`) to a connection. Inbound messages are processed in arrival order per connection; the correlator by `req_msg_id` ↔ `rpc_result.req_msg_id` stitches the response to the original call — the single `RpcDispatcher` allows a response that arrived on socket Y to resolve the awaiter that wrote on socket X.

**Low-RAM fallback (≥ 512 MB-class hardware):** if `Windows::System::MemoryManager::AppMemoryUsageLimit - AppMemoryUsage < 64 MiB`, `MtProtoSessionPool::DetermineEffectivePoolSize` collapses the pool to 1 socket. Diagnostics via `MtProtoChannel.PoolSize` (projected to the managed side); the `MtProtoSessionPoolSmokeTest.cs` smoke test validates 1 ≤ PoolSize ≤ 4 when opening against DC#2.

**Relevant surface:**
- `MtProtoSessionPool(target, sharedDispatcher)` — ctor that stores the target in `owned_.capacity()`.
- `OpenAsync(host, port, authKey, authKeyId, serverSalt, timeOffset)` — launches N `concurrency::create_task` in parallel, waits for all, rolls back on any failure.
- `NextSession()` — lock-free fetch_add + modulo, returns a `shared_ptr<EncryptedSession>`.
- `AllSessions()` — used by `MtProtoChannel::OpenAsync` to register the push handler on each socket (updates arrive on exactly one).
- `Close()` — idempotent; closes all sockets and leaves the pool without sessions.

### Sticky per endpoint

Some methods (`upload.saveFilePart` in particular) must ALL go through the same connection (ordering matters). The pool supports an affinity hint:

```cpp
// application/use_cases/send_file_use_case.h
auto Send(IBuffer^ chunk, uint32_t partIndex) -> Result<void, MtProtoError> {
    return m_pool->SendAffine(/*affinityKey=*/m_uploadId, /*payload=*/...);
}
```

`SendAffine` guarantees that all packets with the same `affinityKey` go through the same TCP connection.

---

## 4. Authorization key generation (DH handshake)

Full flow per the spec (4 round-trips, ~2 seconds on LTE):

### Step 1 — `req_pq_multi`
```
Client → Server (unencrypted):
  req_pq_multi#be7e8ef1 nonce:int128 = ResPQ
```
Generates a 128-bit nonce with `IRandom`.

### Step 2 — `resPQ`
```
Server → Client:
  resPQ#05162463 nonce:int128 server_nonce:int128 pq:bytes
                 server_public_key_fingerprints:Vector<long> = ResPQ
```
Client:
1. Verifies that `nonce` matches the one sent (defense against MITM).
2. Factorizes `pq` (the product of two small primes p < q). Implementation: Pollard rho. Time budget < 50 ms on a Snapdragon S4.
3. Selects one of the `server_public_key_fingerprints` that matches an RSA pubkey embedded in Vianigram (Telegram's official pubkeys, hardcoded; rotation documented). List: 4 fingerprints in the current spec.

### Step 3 — `req_DH_params`
```
Client → Server (RSA-encrypted via server pubkey):
  req_DH_params#d712e4be nonce:int128 server_nonce:int128
    p:string q:string public_key_fingerprint:long
    encrypted_data:string = Server_DH_Params
```
Where `encrypted_data` = RSA(pubkey, p_q_inner_data + padding):
```
p_q_inner_data#83c95aec pq:string p:string q:string
  nonce:int128 server_nonce:int128 new_nonce:int256 = P_Q_inner_data
```

### Step 4 — `server_DH_params_ok`
```
Server → Client (AES-IGE encrypted with tmp_aes_key/iv derived from new_nonce + server_nonce):
  server_DH_params_ok#d0e8075c nonce:int128 server_nonce:int128
    encrypted_answer:string = Server_DH_Params
```
Where `encrypted_answer`, once decrypted, contains:
```
server_DH_inner_data#b5890dba nonce:int128 server_nonce:int128
  g:int dh_prime:string g_a:string server_time:int = Server_DH_inner_data
```

Client:
1. Decrypts with AES-IGE (`crypto::AesIge::Decrypt(tmp_key, tmp_iv, payload)`).
2. Verifies that `dh_prime` is in the allowlist (Telegram publishes safe primes; an expected SHA-256 hash). **Domain policy**: reject a non-canonical prime.
3. Verifies that `(server_time - client_time)` is within ±300 seconds. If outside → correct the internal clock skew and report a `ClockSkewDetected` event.
4. Generates a random DH `b` (2048 bits), computes `g_b = g^b mod dh_prime`, computes `auth_key = g_a^b mod dh_prime`.

### Step 5 — `set_client_DH_params`
```
Client → Server (AES-IGE):
  set_client_DH_params#f5045f1f nonce:int128 server_nonce:int128
    encrypted_data:string = Set_client_DH_params_answer
```
The client sends `g_b` encrypted with `tmp_aes_key/iv`.

### Step 6 — `dh_gen_ok`
```
Server → Client:
  dh_gen_ok#3bcbf734 nonce:int128 server_nonce:int128
    new_nonce_hash1:int128 = Set_client_DH_params_answer
```
The client verifies `new_nonce_hash1 == SHA-1(new_nonce + 0x01 + auth_key_aux_hash)`. If it matches → handshake successful.

`auth_key_id` = lower 64 bits of SHA-1(auth_key).

**Initial server salt:** `server_salt = first_8_bytes(new_nonce) XOR first_8_bytes(server_nonce)`.

### Domain types

```cpp
namespace vianium::mtproto::domain {

struct AuthKey {
    uint8_t bytes[256];     // 2048 bits
    uint64_t Id() const;    // lower 64 bits SHA-1(bytes)
    uint64_t AuxHash() const;   // subtraction of bytes for auth_key_aux_hash
};

struct ServerSalt {
    uint64_t value;
    int64_t valid_since_unix;   // server_time when introduced
    int64_t valid_until_unix;   // expiration provided by server (~30 min default)
};

struct HandshakeContext {
    Nonce128 client_nonce;
    Nonce128 server_nonce;
    Nonce256 new_nonce;
    BigNum dh_prime;
    int g_param;            // typically 2 or 3
    BigNum g_a;             // server's public DH
    BigNum b;               // client's private DH (cleared after derivation)
    BigNum g_b;             // client's public DH (sent to server)
    int32_t server_time;
};

}
```

**Implementation files:**
```
src/application/use_cases/
├── handshake_use_case.h          ← orchestrates the 6 steps
├── handshake_use_case.cpp
src/domain/services/
├── pq_factorizer.{h,cpp}         ← Pollard rho
├── dh_prime_validator.{h,cpp}    ← allowlist
├── nonce_generator.h             ← inline; uses IRandom
src/domain/policies/
├── handshake_policy.{h,cpp}      ← time skew, prime allowlist, expected fingerprint
src/infrastructure/codec/
├── handshake_serializer.{h,cpp}  ← serializes the 4 outbound TL types (req_pq_multi, etc.)
├── handshake_parser.{h,cpp}      ← parses the 4 inbound types
```

---

## 5. Encrypted message format (post-handshake)

Each outbound encrypted-and-framed packet:

```
TCP frame (Intermediate framing):
  [length: uint32 LE]
  [---------------- payload ----------------]
  [auth_key_id: uint64 LE]
  [msg_key: uint128]
  [encrypted_data: AES-256-IGE(ciphertext)]
```

`encrypted_data` plaintext shape:
```
[salt: uint64 LE]
[session_id: uint64 LE]
[msg_id: uint64 LE]
[seq_no: uint32 LE]
[message_data_length: uint32 LE]
[message_data: bytes]
[padding: 12-1024 bytes random, total payload multiple of 16]
```

### msg_key derivation (MTProto v2)

```
msg_key_large = SHA-256(substr(auth_key, x, 32) + plaintext)
msg_key       = msg_key_large bytes 8..23 (16 bytes)

# x = 0 if outbound (client → server), 8 if inbound
```

### AES key/IV derivation per message

```
sha256_a = SHA-256(msg_key + substr(auth_key, x, 36))
sha256_b = SHA-256(substr(auth_key, 40 + x, 36) + msg_key)

aes_key = sha256_a[0..7] + sha256_b[8..23] + sha256_a[24..31]   (32 bytes)
aes_iv  = sha256_b[0..7] + sha256_a[8..23] + sha256_b[24..31]   (32 bytes)
```

**Zero allocations in the hot path:** the 64 bytes of `sha256_a + sha256_b` live on the stack; `aes_key/iv` are built on the stack and passed by pointer to AES-IGE.

### Padding requirement

12-1024 bytes of random padding after `message_data`, adjusted so that the total plaintext (from `salt` to the end of the padding) is a multiple of 16. Guarantees AES alignment and mitigates a length-extension oracle.

**Implementation files:**
```
src/application/use_cases/
├── encrypt_message_use_case.{h,cpp}
├── decrypt_message_use_case.{h,cpp}
src/domain/services/
├── msg_key_deriver.{h,cpp}       ← SHA-256 derivation per spec
├── aes_key_iv_deriver.{h,cpp}
src/domain/value_objects/
├── msg_key.h                     ← uint8_t[16]
├── encrypted_message_envelope.h  ← POD
```

`AES-IGE` is provided by the `vianium-crypto` sibling. See [03-crypto.md](03-crypto.md).

---

## 6. Session, salt, msg_id, seq_no

### session_id

Generated at the first post-handshake connect with `IRandom::NextUInt64()`. Persists while the app is alive (not persisted to disk). A reconnect within the same app session reuses the `session_id`.

### server_salt

- Initial: derived from `new_nonce ⊕ server_nonce` during the handshake.
- Rotation: when the server responds with `bad_server_salt#edab447b`, the client updates `current_salt` with the provided `new_server_salt` and **retransmits** the original message with the new salt. The `mtproto.salt.rotations` counter is incremented.
- Cache: a freshly-obtained `domain::value_objects::server_salt` is stored together with the `valid_until_unix` that the server provides. The oldest is purged.

### msg_id

Canonical generation:
```
msg_id_high = (unix_time * 2^32)
msg_id_low  = (microseconds_part << 2) | tag_bits  // tag bits depends on the type
```

`tag_bits`:
- `00` for a from-client message (RPC call) that expects a response.
- `01` for a from-client message that does NOT expect a response (acks).
- `10/11` reserved for future extensions.

**Invariant:** `msg_id` strictly increasing within a `MTProtoSession`. `MsgIdGenerator` (in `domain/services/msg_id_generator.h`) keeps `last_msg_id` and, if the natural generation would produce a value ≤ last, adds 4 (to preserve the tag bits).

### seq_no

- **Content-related messages** (RPC calls, incoming updates): even seq_no. Increments by 2 after being sent.
- **Non-content-related** (acks, pings): odd seq_no. Increments by 2 as well, but the initial one is 1.
- Computation: `seq_no = (count_of_relevant_msgs_so_far) * 2 + (is_content_related ? 1 : 0)` (the exact rule depends on the paper and is validated with roundtrip tests).

### Replay window

`domain/value_objects/replay_window.h`:
```cpp
class ReplayWindow {
    static constexpr size_t kSize = 64;
    uint64_t m_seen[kSize];
    size_t m_count;
public:
    bool TryAccept(uint64_t msg_id);    // false if duplicate or too old
};
```

If an inbound msg_id is:
- Greater than the most recent: accepted, replaces the oldest.
- Smaller than the oldest in the window: rejected as too old.
- In the middle of the range but already seen: rejected as a replay (event `ReplayDetected`).

---

## 7. Error handling

Error codes mapped to `MtProtoError::Code` (range 1000-1999):

| Code | Spec name | Meaning | Action |
|------|-----------|-------------|--------|
| 1001 | `bad_msg_notification` (-16, -17, ...) | incorrect/repeated/lookahead msg_id | Resync the clock, regenerate msg_id, retry |
| 1002 | `bad_server_salt#edab447b` | the salt expired or the client used the wrong one | Update `current_salt` with `new_server_salt`, resend |
| 1003 | RPC error 420 `FLOOD_WAIT_X` | Rate-limited X seconds | Return `Result::Fail({.code = FloodWait, .aux = X})`. The managed layer handles the UI countdown. |
| 1004 | RPC error 401 `AUTH_KEY_UNREGISTERED` | auth_key revoked | Clear local credentials, redirect to login |
| 1005 | TCP `Send/Recv` error | Connection drop | Trigger reconnect logic (section 8) |
| 1006 | DH prime hash mismatch | Possible MITM with a prime different from the canonical one | Abort handshake, log emergency |
| 1007 | Nonce mismatch during the handshake | Possible MITM | Abort handshake, log emergency |
| 1008 | Time drift > 300 s | Abnormal clock skew | Force an NTP sync (managed port); retry the handshake |
| 1009 | Session revoked on the server | The server responded with `new_session_created` but the `unique_id` does not match | Log + recreate the session |
| 1010 | RPC error 303 `PHONE_MIGRATE_X`, `NETWORK_MIGRATE_X`, etc. | DC reassignment | Close the connection to the current DC, reopen against DC X |
| 1011 | Replay window rejected the msg_id | Out-of-order or duplicate message | Silent drop + counter `mtproto.replay.dropped_total` |
| 1012 | Container payload corrupt | Invalid hash or framing | Close the connection, reconnect |
| 1099 | Unknown error | Catch-all | Log + default retry policy |

**FLOOD_WAIT special:** it is propagated **immediately** to managed without a retry. The managed layer (`Vianigram.Account` or `Vianigram.Messages`) shows the counter to the user.

**bad_msg_notification special codes** (subset, see the full MTProto spec):
- -16: msg_id too low (the client is behind the server clock).
- -17: msg_id too high (the client is ahead of the server clock).
- -18: incorrect two lower order msg_id bits.
- -32: msg_seqno too low.
- -33: msg_seqno too high.
- -34: an even msg_seqno expected, but odd received.
- -35: odd msg_seqno expected, but even received.

Each one has its recovery path documented in `mtproto-policy.md §5`.

---

## 8. Reconnect logic

### Backoff

Exponential backoff with a cap:
```
attempt_n_delay_ms = min(1000 * 2^n, 60000)
n = 0, 1, 2, ..., max
```

Sequence: 1s, 2s, 4s, 8s, 16s, 32s, 60s, 60s, 60s, ...

Random jitter ±20% to avoid a thundering herd when a DC comes back.

### Session preservation

A reconnect keeps:
- `session_id` (as long as the app has not closed).
- `auth_key`, `auth_key_id`.
- `current_salt` (the server will confirm it or emit `bad_server_salt`).
- `last_msg_id_sent` (continues growing monotonically).
- `seq_no_outbound`.
- In-flight RPC calls **are retried** (the client retains the `req_msg_id` values until it receives an `rpc_result` or a `bad_msg_notification`).

### Network change detection

The managed layer observes `Windows.Networking.Connectivity.NetworkInformation::NetworkStatusChanged` and notifies the context via the `INetworkObserver` port. On a network change:
1. Close all connections in the pool.
2. Reset the backoff to 0 (do not wait).
3. Reconnect the pool immediately.

If the change involves Wi-Fi → cellular or vice versa, the per-DC `auth_key` remains valid; only the physical sockets change.

**Implementation files:**
```
src/application/use_cases/
├── reconnect_use_case.{h,cpp}
src/domain/policies/
├── reconnect_policy.{h,cpp}      ← backoff curve, max attempts, jitter
src/ports/outbound/
├── i_network_observer.h          ← provides NetworkChanged callbacks
```

---

## 9. File structure under `src/`

```
src/
├── pch.h, pch.cpp
├── domain/
│   ├── value_objects/
│   │   ├── dc_id.h
│   │   ├── auth_key.{h,cpp}
│   │   ├── server_salt.{h,cpp}
│   │   ├── msg_id.h
│   │   ├── seq_no.h
│   │   ├── nonce_128.h, nonce_256.h
│   │   ├── msg_key.h
│   │   ├── replay_window.{h,cpp}
│   │   └── encrypted_envelope.h
│   ├── entities/
│   │   ├── connection.{h,cpp}        ← a single TCP connection
│   │   └── rpc_call.{h,cpp}          ← in-flight RPC with req_msg_id
│   ├── aggregates/
│   │   ├── mtproto_session.{h,cpp}   ← root: session_id + auth_key + salt + msg_id state
│   │   └── connection_pool.{h,cpp}   ← pool of N connections per DC
│   ├── policies/
│   │   ├── handshake_policy.{h,cpp}      ← prime allowlist, time skew limit, expected fingerprint
│   │   ├── reconnect_policy.{h,cpp}      ← backoff curve
│   │   ├── replay_policy.{h,cpp}         ← window size, drop semantics
│   │   ├── salt_rotation_policy.{h,cpp}  ← when to accept a new salt
│   │   └── multiplex_policy.{h,cpp}      ← affinity rules (file uploads sticky)
│   ├── events/
│   │   ├── handshake_completed.h
│   │   ├── rpc_dispatched.h
│   │   ├── rpc_acked.h
│   │   ├── bad_server_salt.h
│   │   ├── flood_wait_observed.h
│   │   ├── reconnected.h
│   │   └── replay_detected.h
│   └── services/
│       ├── pq_factorizer.{h,cpp}         ← Pollard rho factorization
│       ├── dh_prime_validator.{h,cpp}    ← canonical primes hash check
│       ├── msg_id_generator.{h,cpp}      ← monotonic generator
│       ├── seq_no_generator.{h,cpp}
│       ├── msg_key_deriver.{h,cpp}       ← SHA-256 derivation
│       └── aes_key_iv_deriver.{h,cpp}
├── application/
│   ├── use_cases/
│   │   ├── handshake_use_case.{h,cpp}
│   │   ├── send_rpc_use_case.{h,cpp}
│   │   ├── receive_loop_use_case.{h,cpp}
│   │   ├── encrypt_message_use_case.{h,cpp}
│   │   ├── decrypt_message_use_case.{h,cpp}
│   │   ├── reconnect_use_case.{h,cpp}
│   │   ├── send_file_chunk_use_case.{h,cpp}    ← affinity-aware
│   │   └── handle_bad_msg_notification.{h,cpp}
│   ├── command_handlers/
│   │   └── send_rpc_handler.{h,cpp}
│   └── query_handlers/
│       └── get_session_state_handler.{h,cpp}
├── ports/
│   ├── inbound/
│   │   └── i_mtproto_channel.h           ← shape consumed by winrt_shim
│   └── outbound/
│       ├── i_tcp_transport.h             ← MTProtoTcpAdapter implements
│       ├── i_random.h                    ← provided by Crypto
│       ├── i_clock.h                     ← Vianium.Core.Kernel
│       ├── i_logger.h                    ← Vianium.Core.Kernel
│       ├── i_telemetry.h                 ← Vianium.Core.Kernel
│       ├── i_event_bus.h                 ← Vianium.Core.Kernel
│       ├── i_network_observer.h
│       ├── i_crypto.h                    ← interface to the vianium-crypto sibling (C ABI)
│       └── i_tl_codec.h                  ← interface to the vianium-mtproto src\tl\ sibling (C ABI)
├── infrastructure/
│   ├── transport/
│   │   ├── mtproto_tcp_adapter.{h,cpp}       ← wraps Vianium::Core::Net::v1::SocketTransport^
│   │   ├── frame_codec.h                     ← IFrameCodec interface
│   │   ├── intermediate_frame_codec.{h,cpp}  ← default
│   │   ├── abridged_frame_codec.{h,cpp}      ← fallback
│   │   └── full_frame_codec.{h,cpp}          ← feature-flag
│   ├── codec/
│   │   ├── handshake_serializer.{h,cpp}
│   │   ├── handshake_parser.{h,cpp}
│   │   ├── envelope_serializer.{h,cpp}
│   │   └── envelope_parser.{h,cpp}
│   ├── crypto_adapter.{h,cpp}                ← wraps Vianium::Crypto::v1::CryptoSession^ (sibling vianium-crypto)
│   ├── tl_codec_adapter.{h,cpp}              ← wraps Vianium::Tl::v1::TlSchema^ (sibling vianium-mtproto src\tl\)
│   └── platform/
│       └── network_observer_winrt.{h,cpp}    ← Windows::Networking::Connectivity
├── api/
│   └── v1/
│       ├── pch.h                              ← /ZW for CX
│       ├── c_api.cpp                          ← vng_mtproto_api.h impl
│       └── winrt_shim.cpp                     ← MTProtoChannel sealed ref class
└── internal/
    ├── mtproto_log.h                          ← MTP_DEBUG_LOG, MTP_INFO_LOG, etc.
    └── self_test.{h,cpp}                      ← DEBUG-only vector tests
```

---

## 10. Public API surface (WinMD)

```cpp
// src/api/v1/winrt_shim.cpp (excerpt)
namespace Vianium::Mtproto::v1 {

public ref struct MTProtoConnectResult sealed {
    property int32_t ErrorCode;            // 0 = OK
    property int64_t AuthKeyId;
    property int64_t ServerSalt;
    property int32_t HandshakeMs;
};

public ref struct MTProtoSendResult sealed {
    property int32_t ErrorCode;
    property int32_t AuxValue;             // FloodWait seconds, BadServerSalt new salt low bits, etc.
    property Windows::Storage::Streams::IBuffer^ Payload;   // RPC result body, post-decrypt + post-TL strip
    property int64_t MsgId;
};

public ref struct MTProtoIncomingMessage sealed {
    property int64_t MsgId;
    property int32_t SeqNo;
    property uint32_t ConstructorId;
    property Windows::Storage::Streams::IBuffer^ Payload;
};

public ref class MTProtoChannel sealed {
public:
    static MTProtoChannel^ Create(
        Vianium::Crypto::v1::CryptoSession^ crypto,
        Vianium::Tl::v1::TlSchema^ schema,
        Vianium::Core::Net::v1::SocketTransport^ socket,
        Windows::Foundation::IAsyncActionWithProgress<Platform::String^>^ /* logger */);

    Windows::Foundation::IAsyncOperation<MTProtoConnectResult^>^
        ConnectAsync(int32_t dcId, Windows::Storage::Streams::IBuffer^ existingAuthKey);

    Windows::Foundation::IAsyncOperation<MTProtoSendResult^>^
        SendAsync(uint32_t ctorId, Windows::Storage::Streams::IBuffer^ tlPayload);

    Windows::Foundation::IAsyncOperation<MTProtoSendResult^>^
        SendFileChunkAsync(int64_t affinityKey, uint32_t ctorId,
                           Windows::Storage::Streams::IBuffer^ tlPayload);

    Windows::Foundation::IAsyncAction^ DisconnectAsync();

    event Windows::Foundation::TypedEventHandler<MTProtoChannel^, MTProtoIncomingMessage^>^ MessageReceived;
    event Windows::Foundation::TypedEventHandler<MTProtoChannel^, Platform::String^>^ Reconnected;
    event Windows::Foundation::TypedEventHandler<MTProtoChannel^, int32_t>^ FloodWaitObserved;
};

}
```

`existingAuthKey` allows passing a persisted auth_key (encrypted at-rest, decrypted on the managed side, passed as an `IBuffer`); if it is null or empty, it triggers a full DH handshake.

---

## 11. Integration with the `vianium-*` sibling repos

| Vianigram MTProto needs | Provided by | Mechanism |
|----------------------------|--------|-----------|
| `Result<T, Error>`, allocators, logger, clock, RNG-port | Sibling `vianium-kernel` | static lib link via `<AdditionalIncludeDirectories>` |
| Socket TCP transport with timeouts, network change observer | Sibling `vianium-net::SocketTransport` | WinMD consumption + `MTProtoTcpAdapter` adapter |
| AES-IGE, DH 2048, SHA-256, RSA encrypt (handshake step 3) | Sibling `vianium-crypto` | C ABI header `vianium/crypto/api/v1/crypto_api.h` |
| TL serialize/deserialize, type registry layer 214 | Sibling `vianium-mtproto\src\tl\` | C ABI header `vianium/tl/api/v1/tl_api.h` |

The `vianium-tls` sibling is **NOT** used for MTProto (MTProto encrypts at its own level over plain TCP).

---

## 12. Self-tests (DEBUG)

At the first `MTProtoChannel::Create()`:

1. **MsgIdMonotonicityTest**: generates 1000 consecutive msg_ids with various tag bits; verifies strictly increasing.
2. **SeqNoTest**: verifies the even/odd discipline in a sequence of alternating content/non-content messages.
3. **ReplayWindowTest**: creates a window, inserts 64 ids, verifies the oldest is purged when the 65th arrives; verifies replay detection in the middle.
4. **HandshakeRoundtripTest**: runs a full DH handshake against a **mock server** (no real network); verifies that the derived `auth_key` matches a reference value.
5. **EncryptDecryptRoundtrip**: encrypts and decrypts a message with a known auth_key; byte-equal.
6. **PqFactorizerTest**: factorizes pq=15485863 (= 3169 × 4889) and other small ones from the Telegram paper; verifies < 50 ms.

If any of them fails → `MTProtoChannel::Create()` returns error code `MTPROTO_SELFTEST_FAILED`. The managed layer writes the error to the log and refuses the flow.

---

## 13. Telemetry

| Metric | Type | When |
|---------|------|--------|
| `mtproto.handshake.duration_ms` | Histogram | Each completed handshake |
| `mtproto.handshake.failure_total{reason}` | Counter | Each failed handshake |
| `mtproto.rpc.duration_ms{ctor}` | Histogram | Each RPC from send to rpc_result |
| `mtproto.rpc.error_total{code}` | Counter | Each RPC with an error |
| `mtproto.connection.reconnects_total{dc}` | Counter | Each reconnect attempted |
| `mtproto.connection.alive_seconds{dc}` | Gauge | The lifetime of each current connection |
| `mtproto.salt.rotations_total` | Counter | Each bad_server_salt + recovery |
| `mtproto.replay.dropped_total` | Counter | Each msg_id dropped by the replay window |
| `mtproto.flood_wait.observed_total{method}` | Counter | Each FLOOD_WAIT received |
| `mtproto.frame.tcp_corruption_total` | Counter | Each frame with an invalid length or CRC mismatch |

Routed via the `ITelemetry*` injected into the channel's ctor.

---

## 14. Risks and mitigations

| Risk | Mitigation |
|--------|------------|
| Telegram changes the MTProto spec (a hypothetical v3) | Layered API: any change stays behind a `v2/` version; v1 keeps working. |
| auth_key leak via logs | The log macros filter sensitive fields; auth_key never appears in `MTP_*_LOG` even in DEBUG. |
| Aggressive clock skew on the device → handshake fail loop | `time_drift_max=300s` policy + auto-correction with the `server_time` from step 4. If failure repeats, the managed port triggers an NTP sync via `Windows.System.UserProfile`. |
| Multi-connection saturating the battery (4 idle keepalive sockets) | Telemetry monitors the idle/active ratio; if the app is suspended > 30 s, the pool drops to 1 connection. |
| FLOOD_WAIT loops due to a bug | The `mtproto.flood_wait.observed_total` counter with alerting; if > 3 in 5 min for the same method, the managed layer presents a UI error. |
| MITM attempting a downgrade (a different DH prime) | The `dh_prime_validator` domain policy rejects non-canonical primes; emits an emergency event. |
| Legitimate replay window overflow (a large out-of-order) | Window size 64 = a trade-off; if telemetry shows legitimate drops > 0.1%, raise it to 128. |

---

## 15. Phasing

**Phase 1 (ROADMAP):**
- Domain types + policies + value objects.
- DH handshake end-to-end against the **test DC** `149.154.167.40:443`.
- Encrypt/decrypt roundtrip with a fixed auth_key (vector test).
- Single connection, Intermediate framing.
- TL codec adapter (consumes the `vianium-mtproto\src\tl\` sibling).
- Crypto adapter (consumes the `vianium-crypto` sibling).
- WinMD shim with a minimal `ConnectAsync` + `SendAsync`.
- Smoke test: `auth.sendCode` → `auth.sentCode` round-trip.

**Phase 2:**
- Connection pool (4 connections per DC).
- Multiplexing by msg_id.
- Affinity hint for file uploads.
- Receive loop + container handling (`msg_container`).
- Bad msg notification recovery + salt rotation.

**Phase 4 (post-MVP):**
- Full reconnect resilience (network change observer + battery-aware pool sizing).
- Full framing behind a feature flag.
- Complete telemetry.

**Phase 5:**
- Secret chats infrastructure (consumes `Vianium::Crypto::SecretChatCrypto` from the `vianium-crypto` sibling). The MTProto transport stays the same; only the encrypted body changes.

**Deferred / out of scope:**
- Push notifications (a separate managed context).
- Web preview (MTProto serves the URL → preview in managed).
- Automatic multi-DC migration (Phase 4+).
