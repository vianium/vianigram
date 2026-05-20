# MTProto 2.0 Policy — Vianigram

**Status:** active · **Layer:** 214 · **Last reviewed:** 2026-04-27

This policy describes the cryptographic and protocol invariants that Vianigram requires of **MTProto 2.0**, Telegram's transport and application layer. The canonical public specification lives at `https://core.telegram.org/mtproto`; the TL/schema layer at `https://core.telegram.org/schema` (Layer 214). MTProto runs **on top of** the TLS 1.3 tunnel described in `tls-policy.md`, but does NOT trust TLS for message confidentiality: the E2E encryption of Secret Chats and the client-server encryption of cloud chats are the exclusive responsibility of this layer.

> **Architectural position.** Vianigram treats MTProto as a native *bounded context* extracted to the `vianium-mtproto` sibling (`src\mtproto\`) whose WinMD surface exposes only: `IMtprotoSession`, `IAuthKeyStore`, `ISecretChatHandshake`, `ISrpAuthenticator`. Message plaintext never crosses the WinMD ABI as `byte[]` — only an opaque `IBuffer` backed by native arenas with deterministic zeroization. See `at-rest-encryption.md` §3 for the isolation rule.

If you modify anything in this document, also update (in the `vianium-mtproto` sibling):
- `src/handshake/dh_handshake.cpp` (req_pq → set_client_DH_params)
- `src/session/session_state.cpp` (msg_id, seq_no, server_salt)
- `src/crypto/aes_ige.cpp` (AES-256-IGE record)
- `src/auth/srp2048.cpp` (SRP-6a/2048)
- `src/secret_chat/decrypted_message.cpp` (E2E)
- `Clients/Vianigram.SmokeTests/Tests/MtProtoHandshakeSmokeTest.cs`
- `Clients/Vianigram.SmokeTests/Tests/MsgIdReplayTest.cs`
- `Clients/Vianigram.SmokeTests/Tests/Aes256IgeVectorTest.cs`
- `Clients/Vianigram.SmokeTests/Tests/Srp2048VectorTest.cs`

---

## 1. Authorization Key (auth_key)

The `auth_key` is a shared key of **2048 bits (256 bytes)** between the client and a specific Data Center (DC1..DC5), generated via Diffie-Hellman at first contact and reused for the lifetime of the device on that DC.

### 1.1 DH handshake (3-pass)

Mandatory sequence, defined at `https://core.telegram.org/mtproto/auth_key`:

1. **`req_pq_multi`** (client → server)
   - The client sends `nonce` (16 random bytes).
   - The server responds `resPQ { nonce, server_nonce, pq, server_public_key_fingerprints }`.
   - **Invariant**: the `nonce` of the response **MUST** match bit-for-bit the one sent. A mismatch ⇒ `MtprotoAbort.NonceMismatch`, the connection is closed, the attempt counted for exponential backoff.

2. **`req_DH_params`** (client → server)
   - The client factorizes `pq` (a ~63-bit integer) into primes `p < q` via Pollard ρ-Brent (sibling `vianium-mtproto\src\crypto\pollard_rho.cpp`).
   - The client encrypts `p_q_inner_data { pq, p, q, nonce, server_nonce, new_nonce }` with RSA-OAEP+ (the MTProto variant, see `https://core.telegram.org/mtproto/auth_key#41-rsa-paddata-server-public-key-mentioned-above-is-implemented-as-follows`) under the `server_public_key` corresponding to the announced fingerprint.
   - The server responds `server_DH_params_ok { encrypted_answer }`, encrypted with AES-256-IGE under keys derived from `new_nonce + server_nonce` via SHA-1.

3. **`set_client_DH_params`** (client → server)
   - The client generates a random 2048-bit `b ∈ [2, dh_prime − 2]`, computes `g_b = g^b mod dh_prime`.
   - The client sends `client_DH_inner_data { nonce, server_nonce, retry_id, g_b }` encrypted with the same AES-IGE.
   - The server responds `dh_gen_ok | dh_gen_retry | dh_gen_fail`.
   - `auth_key = g_a^b mod dh_prime` (256 bytes; left-padded with zeros if shorter).

### 1.2 Validation of the `dh_prime` prime

- Exact length = 2048 bits.
- `dh_prime` must be prime, and `(dh_prime − 1) / 2` also prime (a safe prime).
- `g ∈ {2, 3, 4, 5, 6, 7}` and `g` is a generator of the subgroup of order `(p−1)/2`.
- Vianigram caches the accepted `dh_prime` values (sibling `vianium-mtproto\src\handshake\dh_prime_cache.cpp`) and re-validates only when the server delivers a new one. Miller-Rabin test with 64 rounds (Baillie-PSW preferred when the toolchain allows it).
- `g_a, g_b ∈ [2^{2047}, dh_prime − 2^{2047}]` to avoid small subgroups. Out-of-range ⇒ abort.

### 1.3 Time verification

- `server_time` comes in `dh_gen_ok` and in every subsequent container.
- **Invariant**: `|server_time − client_time| < 300 s`. Out-of-range ⇒ adjust the internal `time_offset` (not the system `clock_settime`) and retry; if it persists after 3 retries, abort and emit `[MTProto] CLOCK_SKEW_ABORT`.
- A message whose `msg_id` (which encodes a timestamp) falls outside `[server_time − 300, server_time + 30]` is rejected.

### 1.4 Persistence

- One `auth_key` per DC, written to `LocalSettings` under the key `auth.dc{N}.key` in `byte[256]` ciphertext format via `DataProtectionProvider` (scope `LOCAL=user`).
- The `auth_key_id = lower 64 bits of SHA1(auth_key)` is cached as a key prefix so `LocalSettings` can search without decrypting.
- **Rotation**: never on the client's initiative. It is only regenerated on `logOut`, on detecting `auth_key_unregistered` (-401), or on a complete user wipe. See §6 for integration with at-rest encryption.

---

## 2. Session

A session is a logical unit of continuity with a specific DC. Multiple sessions can coexist under the same `auth_key` (typically: one for interactive messaging, another for upload/download).

### 2.1 `session_id`

- 8 random bytes generated on each **reconnection** (not on each request).
- The server keeps a bucket of the last `msg_id` values per `(auth_key_id, session_id)`; reusing a dead `session_id` causes a `bad_msg_notification` with code 16/17.
- Vianigram persists the active `session_id` for fast reconnection; invalidates it after 24 h or after `bad_session_id` (-32).

### 2.2 `server_salt`

- A random 8-byte value that the server rotates every **~30 minutes**.
- It is included in the header of every encrypted message (the `salt` field, 8 bytes).
- In response to `bad_server_salt` (error code -32 on the container), the server attaches `new_server_salt`. **Invariant**: the client MUST apply the new salt and resend the failed container on the same socket. A missing retry ⇒ silent loss of messages.
- Vianigram preloads `future_salts` (RPC `get_future_salts`) every 15 min to avoid the error window.

### 2.3 `seq_no`

- A monotonic counter **per session, per direction**.
- Increments by 2 for each *content-related message* (messages that require an ack); by 0 for service messages (`msgs_ack`, `pong`, etc.).
- The LSB of the `seq_no` indicates whether the message requires an ack (1) or not (0). The server rejects non-monotonic `seq_no` values with `bad_msg_notification` 32/33.

### 2.4 `msg_id`

A unique message identifier, 8 bytes:

```
msg_id = (unix_time << 32) | (counter_within_second << 2) | direction_lsb
```

- LSB bits:
  - `00` ⇒ a client→server message that does not expect a response.
  - `01` ⇒ server→client responding to a client.
  - `10` ⇒ a client→server message that expects a response.
  - `11` ⇒ a server→client notification.
- **Monotonicity invariant**: each outbound `msg_id` must be strictly greater than the last one sent in the session.
- **Window invariant**: the server rejects `msg_id` values with a `unix_time` outside `[server_time − 300, server_time + 30]`.

### 2.5 Replay window (client)

Vianigram applies a symmetric acceptance window for inbound messages:

- Keeps `last_seen_msg_id` per session.
- An inbound message with `msg_id ≤ last_seen_msg_id` ⇒ **rejection** (suspected replay; emits `[MTProto] REPLAY_REJECTED msg_id={…}`), it is not decapsulated.
- An inbound message with `unix_time(msg_id) < server_time − 300` ⇒ rejected for age.
- An inbound message with `unix_time(msg_id) > server_time + 30` ⇒ rejected for being in the future (a probable clock mismatch).
- Implemented in sibling `vianium-mtproto\src\session\replay_guard.cpp`.

---

## 3. Encryption — AES-256-IGE

### 3.1 Algorithm

MTProto encrypts each message (after the `auth_key_id || msg_key` header) with **AES-256-IGE** (Infinite Garble Extension): a non-standard mode specific to Telegram that chains each 16-byte block with the previous ciphertext **and** plaintext block:

```
C_i = AES_enc_K(P_i ⊕ C_{i−1}) ⊕ P_{i−1}
P_i = AES_dec_K(C_i ⊕ P_{i−1}) ⊕ C_{i−1}
```

with `IV1 = IV[0..16)` and `IV2 = IV[16..32)` as the initial `C_0` and `P_0`.

Implementation in sibling `vianium-mtproto\src\crypto\aes_ige.cpp`. Self-tested at startup against the vectors published by Telegram (`https://core.telegram.org/mtproto/security_guidelines#test-vectors`).

### 3.2 Derivation of `msg_key` (v2)

Layer 214 uses **msg_key v2**:

```
msg_key_large = SHA-256(substr(auth_key, 88+x, 32) || plaintext_with_padding)
msg_key       = substr(msg_key_large, 8, 16)   // 16 bytes
```

where `x = 0` for client→server messages, `x = 8` for server→client.

### 3.3 Derivation of `aes_key` and `aes_iv`

```
sha256_a = SHA-256(msg_key || substr(auth_key, x, 36))
sha256_b = SHA-256(substr(auth_key, 40+x, 36) || msg_key)
aes_key  = substr(sha256_a, 0, 8) || substr(sha256_b, 8, 16) || substr(sha256_a, 24, 8)   // 32 bytes
aes_iv   = substr(sha256_b, 0, 8) || substr(sha256_a, 8, 16) || substr(sha256_b, 24, 8)   // 32 bytes
```

### 3.4 Padding

- Before encrypting, the plaintext is padded with **12..1024 random bytes** up to a multiple of 16.
- Vianigram uses the sibling `vianium-crypto\src\random\secure_random.cpp` (CryptGenRandom-equivalent via `BCryptGenRandom` when available; fallback to `CryptographicBuffer.GenerateRandom` on WP8.1).
- The extra random padding within the range protects against known-plaintext in predictable headers.

### 3.5 Decryption invariant

**Critical** — protects against active MITMs that substitute ciphertext with garbage:

1. Decrypt with AES-IGE-256 using `(aes_key, aes_iv)` derived from the received `msg_key`.
2. Recompute `msg_key' = SHA-256(auth_key_part || plaintext)[8..24]`.
3. Compare `msg_key'` vs the received `msg_key` **in constant time** (sibling `vianium-crypto\src\crypto\constant_time_eq.cpp`, the same post-fix `>> 8` helper as `chacha20_poly1305.cpp` in the `vianium-tls` sibling).
4. **A mismatch ⇒ abort the entire connection**, NOT just discard the message. Emit `[MTProto] MSG_KEY_MISMATCH session={…} dc={…}` and disconnect the socket. Reconnect with a new `session_id`.

Rationale: an attacker who can inject a single validly-formed message under the `auth_key` has probably compromised the key; reusing the session would be negligent.

### 3.6 Banned

- AES-256-CBC (vulnerable to a padding oracle if the msg_key check were to fail).
- AES-256-CTR (it is not what MTProto defines).
- ChaCha20-Poly1305 (not supported by Telegram servers in this protocol; it is available in TLS, see `tls-policy.md`).

---

## 4. 2FA — SRP-6a / 2048

Telegram offers second-factor authentication via **SRP-6a** (RFC 5054, adapted), with a 2048-bit prime `p`.

### 4.1 Parameters

- `p`, `g` provided by the server in `account.getPassword` (TL `account.password`). Vianigram does **NOT** hardcode them: it re-validates the primality and the order of `g` upon receiving them (the same procedure as §1.2).
- Hash: **SHA-256** (Telegram custom-uses SHA-256 instead of the SHA-1 of RFC 5054).
- Password KDF: **PBKDF2-HMAC-SHA512**, 100 000 iterations, server-provided salt (`new_secure_salt + new_secure_salt + ... + secure_salt`, see `https://core.telegram.org/api/srp`).
- PBKDF2 output: 64 bytes ⇒ `x` (RFC 5054).

### 4.2 Computations

```
A = g^a mod p                                  // a = 2048-bit random
u = H(A | B)
S = (B − k·g^x)^(a + u·x) mod p
K = H(S)
M1 = H(H(p) XOR H(g) | H(I) | salt | A | B | K)
M2 = H(A | M1 | K)
```

where `k = H(p | g)` and `I = "user_id"`.

### 4.3 Invariants

- `A ≢ 0 (mod p)` and `B ≢ 0 (mod p)`. Either ⇒ abort.
- `u ≢ 0`. If it were zero, abort (a classic SRP vulnerability).
- **Constant-time comparison** of M1 (client) and M2 (server) with sibling `vianium-crypto\src\crypto\constant_time_eq.cpp`. Never use `==` over `byte[]` to verify M1/M2.
- M2 verified *after* the server accepts M1. If the server declares success but M2 does not verify ⇒ abort + alert.

### 4.4 Persistence

The derived password is kept **only in RAM** (a native arena, zeroized when sign-in completes) during the handshake. The password plaintext is never written to disk. The optional `passcode_hash` for the Local Passcode (§6) is independent and lives in `at-rest-encryption.md` §4.

---

## 5. Secret Chats — End-to-End

Secret Chats are E2E chats independent of the client-server encryption. They converse with only a single peer and are bound to the device.

### 5.1 Diffie-Hellman 2048

- Parameters (`g`, `p`) provided by **`messages.getDhConfig`** — DIFFERENT from those in §1 (auth_key).
- Vianigram re-validates `p` and `g` exactly as in §1.2.
- Exponent size: 2048 bits, generated with `secure_random`.
- Shared key `key = g_b^a mod p` ⇒ 256 bytes.

### 5.2 Key fingerprint

- `key_fingerprint = SHA-1(key)[0..16]` (16 bytes).
- Shown to the user as a **6×8 emoji** grid derived from `SHA-256(key | g_a)` in 8-bit blocks indexing a table of 256 emojis (identical to that of the official client — keep the identical mapping so that the fingerprints match visually).
- **UX invariant**: Vianigram SHOWS the fingerprint on a dedicated screen accessible from the Secret Chat's header. The user must confirm that it matches the peer's fingerprint via an out-of-band channel (typically a voice call). Without visual verification there is NO guarantee of the absence of a MITM in the handshake.

### 5.3 Message encryption

- `decryptedMessageLayer { random_bytes, layer, in_seq_no, out_seq_no, message }` serialized as TL.
- Encrypted with AES-256-IGE under keys derived from `key` and `msg_key` the same as §3 (the v2 variant — compatibility with Telegram Layer 73+).
- `auth_key` here = `key` (it is not the client-server auth_key).

### 5.4 PFS rekey

- Rekey every **100 messages OR 1 week**, whichever comes first.
- Trigger: either end sends `decryptedMessageActionRequestKey` with a new `g_a`.
- After `decryptedMessageActionAcceptKey` and `decryptedMessageActionCommitKey`, both parties compute the new `key`.
- **Zeroization invariant**: the old `key` is overwritten with zero (`SecureZeroMemory` or `RtlSecureZeroMemory`) **immediately** after the commit. The buffer of the old key must NOT survive the next allocation. Implemented via the RAII guard `SecretKeyHandle` in sibling `vianium-crypto\src\keys\secret_key_handle.cpp`.

### 5.5 Layer negotiation

- At the first handshake, both parties send `decryptedMessageLayer` with their maximum supported `layer` (Vianigram = 214).
- Effective layer = `MIN(self.layer, peer.layer)`.
- Vianigram never downgrades its announced layer in order to preserve mitigations (e.g. msg_key v2 vs v1).
- If `peer.layer < 73`, Vianigram **rejects** the chat (msg_key v1 has a known flaw described at `https://core.telegram.org/mtproto/security_guidelines#mtproto-1-0-deprecation`).

---

## 6. Sensitive data at rest

Full detail in `at-rest-encryption.md`. Summary:

| Data | At-rest encryption |
|------|--------------------|
| `auth_key` (one per DC) | DataProtectionProvider, scope `LOCAL=user`. |
| Secret chat root keys (`key` per chat) | DataProtectionProvider + a zeroized native arena. |
| `passcode_hash` (optional Local Passcode) | PBKDF2-SHA512, device-bound salt. |
| `server_salt`, `session_id`, `last_seen_msg_id` | DataProtectionProvider (lazy-decrypted on reconnect). |
| Message bodies (cloud chats) | Plaintext by default; encrypted with a master key derived from the Local Passcode when it is enabled. |
| Contact metadata | Plaintext by default; same as message bodies under the Local Passcode. |
| Sticker thumbs / cache | Plaintext (public). |

**Hard rule**: no critical material (auth_key, secret chat key, SRP-derived password) crosses the WinMD ABI as a managed `byte[]`. Only opaque handles (an `IBuffer` projected from a native arena) or IDs.

---

## 7. Threat model

### Defended

| Threat | Defense |
|---------|---------|
| Passive eavesdrop of captured traffic | TLS 1.3 (transport) + AES-IGE under `auth_key` (cloud messages) + DH-derived key (secret chats E2E). |
| MITM in the client-server DH | RSA-OAEP+ with a hardcoded `server_public_key`, fingerprint match in `req_pq_multi`. |
| MITM in the secret chat DH | Visual verification of the emoji fingerprint via an out-of-band channel. |
| Message replay | `msg_id` strictly monotonic + a ±300/30 s window + `last_seen_msg_id` per session. |
| Padding-oracle / msg_key tamper | SHA-256 recompute + constant-time comparison + abort of the entire connection on a mismatch. |
| Timing-leak in auth verification (M1/M2) | Constant-time comparison mandatory. |
| Theft of a powered-off device | `auth_key` + secret chat keys encrypted with DataProtectionProvider (TPM-bound on capable SoCs). |
| MTProto layer downgrade | Minimum layer 73 rejected; the announced layer is never reduced voluntarily. |
| Small-subgroup in DH | Validation `g_a, g_b ∈ [2^{2047}, p − 2^{2047}]` and the order of `g`. |

### NOT defended

- **A malicious Telegram operator** reads cloud chats (they are not E2E). Only Secret Chats are protected against the server; the rest are end-to-server-to-end. The user must understand this difference (the UI labels Secret Chats with a distinctive lock icon).
- **A compromised client with a known passcode**: malware with kernel access on the device can read native arenas; DataProtectionProvider does not protect against an active adversary on the same system.
- **Traffic analysis**: packet sizes and cadence reveal activity even under TLS+MTProto. There is no anti-fingerprint traffic padding.
- **Social engineering / SIM-swap**: the handshake trusts SMS/a call for delivery of the initial verification code. A SIM-swap defeats the auth — the user must enable 2FA SRP (§4) to add a factor independent of the mobile operator.
- **A quantum adversary with stored ciphertext**: AES-256 survives Grover (a 128-bit margin), but DH-2048 falls to Shor. Mitigation: hybrid PQ key exchange, future work — aligned with `tls-policy.md` §9.

---

## 8. Audit trail / smoke tests

All tests live in `Clients/Vianigram.SmokeTests/Tests/` and are run via `SmokeTestRunner.RunAllAsync()` except those that require the network (marked `[NetworkRequired]`).

| Test | Purpose | Source |
|------|-----------|--------|
| `MtProtoHandshakeSmokeTest` | Full handshake `req_pq_multi → set_client_DH_params` against the test DC (DC2 staging `149.154.167.40:443`). Asserts `dh_gen_ok`, validates `auth_key.length == 256`, validates the derived `auth_key_id`. | `MtProtoHandshakeSmokeTest.cs` |
| `MsgIdReplayTest` | Sends a reused `msg_id` within the window; expects a `bad_msg_notification` with code 16 (msg_id too low) or 17 (msg_id too high). In the reverse direction, simulates a server→client message with `msg_id < last_seen` and verifies that the `replay_guard` discards it without decrypting. | `MsgIdReplayTest.cs` |
| `Aes256IgeVectorTest` | Official Telegram vectors (`https://core.telegram.org/mtproto/security_guidelines#test-vectors`) + custom vectors for encryption and decryption of blocks that are multiples of 16 bytes up to 1024 KB. | `Aes256IgeVectorTest.cs` |
| `Srp2048VectorTest` | RFC 5054 §B vectors adapted to SHA-256 + PBKDF2-SHA512(100k). Covers the computation of `x`, `A`, `S`, `K`, `M1`, `M2` and the error branch `B ≡ 0 (mod p)`. | `Srp2048VectorTest.cs` |
| `MsgKeyV2InvariantTest` | Generates random plaintexts, encrypts with AES-IGE-256 under a mock `auth_key`, corrupts 1 byte of the ciphertext, verifies that the `msg_key` recompute fails and that the connection is marked for abort. | `MsgKeyV2InvariantTest.cs` |
| `SecretChatFingerprintTest` | Generates a DH 2048 key, derives the SHA-1 + emoji-grid fingerprint, compares it with vectors generated by TDLib (a cross-suite to guarantee an identical mapping). | `SecretChatFingerprintTest.cs` |
| `DhPrimeValidationTest` | Passes valid primes, non-safe primes, small composite primes, generators of order 2; verifies that only safe-prime + correct-g-order ones are accepted. | `DhPrimeValidationTest.cs` |

Any failure in the group is **release-blocking**.

---

## 9. Change log

| Date       | Change |
|------------|--------|
| 2026-04-27 | Initial policy. Layer 214. Mozilla Modern TLS via a project reference to the `vianium-tls` sibling (`tls-policy.md`). AES-256-IGE + msg_key v2 + SRP-6a/2048 + secret chat DH-2048. Replay window ±300/30 s. msg_key mismatch ⇒ abort of the entire connection (not just of the message). PFS rekey in secret chats every 100 messages / 1 week with RAII zeroization of the old key. Smoke tests: 7 tests cover handshake, replay, AES-IGE, SRP, msg_key invariant, fingerprint, dh_prime. |
