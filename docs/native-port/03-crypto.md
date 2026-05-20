# Vianium.Crypto — Crypto Primitives for MTProto, Secret Chats and At-Rest

**Status:** planned (Phase 1) · **Last reviewed:** 2026-04-27 · **Sibling repo:** `vianium-crypto`

> Required prior reading:
> - [architecture-principles.md](architecture-principles.md) — toolchain, DDD+hex, WinMD ABI rules.
> - [00-overview.md](00-overview.md) — context map.
> - [01-mtproto.md](01-mtproto.md) — which consumes `IMTProtoCrypto`.
> - `..\security\mtproto-policy.md` — invariants that `domain/policies/` enforces.
> - The canonical crypto policy lives in the `vianium-tls` sibling (`..\vianium-docs\security\tls-policy.md`); Vianigram inherits the primitives via the `vianium-crypto` sibling.

The `vianium-crypto` sibling gathers all the cryptographic primitives that MTProto, secret chats, and at-rest storage need. Its philosophy: **reuse everything the `vianium-tls` sibling has already validated** (sha256, sha512, hkdf, hmac, x25519, aes_core, aes_gcm, bignum) and add **only what is specific to Telegram** (AES-256-IGE, DH 2048-bit, SRP-2048, PBKDF2-SHA512, key fingerprints). This document describes that interface from Vianigram's perspective, which consumes the sibling's WinMD.

---

## 1. Bounded context

### Ubiquitous language

| Term | Definition |
|---------|------------|
| **auth_key** | A 2048-bit key derived from the MTProto DH handshake. Long-lived. |
| **msg_key** | A 128-bit per-message key derived from auth_key + plaintext (MTProto v2). |
| **AES-IGE** | Infinite Garble Extension mode of AES, custom to Telegram. |
| **DH 2048** | Diffie-Hellman over a 2048-bit prime, used in the MTProto handshake. |
| **SRP-2048** | Secure Remote Password protocol RFC 5054 with a 2048-bit prime. For 2FA. |
| **PBKDF2-SHA512** | Password-based KDF RFC 2898 with HMAC-SHA512. Iterations as set by the server. |
| **secret chat root_key** | A key derived from Curve25519 DH between two peers. Long-lived until rekey. |
| **key fingerprint** | A visual hash of root_key + msg_key, shown to the user as an emoji set for out-of-band verification. |
| **auxiliary_hash** | Bytes of auth_key used for auth_key_id (lower 64 bits of the SHA-1). |

### Aggregate root

`CryptoSession` (in `domain/aggregates/crypto_session.h`):
- Identified by `(scope, scope_id)` — scope ∈ {MTProto, SecretChat, AtRest}.
- Owns: the long-lived keys of the scope (auth_key, root_key, etc.).
- Invariants:
  - Keys never cross boundaries outside the context unencrypted (DPAPI on the managed side).
  - Self-tests on construction; refuses service if they fail.
  - `auth_key` in heap memory marked for zeroize on destroy.

### Domain events

- `KeyDerived{ scope, key_id, derivation_ms }`.
- `FingerprintComputed{ scope, key_id, fingerprint_emoji_indices[5] }`.
- `IntegrityCheckFailed{ scope, key_id, reason }`.
- `SelfTestPassed{ }`, `SelfTestFailed{ test_name }`.

---

## 2. Reuse from `..\vianium-tls\src\crypto\`

These primitives are compiled **source-level** inside the `vianium-crypto` sibling. They are not project references because the `vianium-tls` sibling is a WinRT Component (it does not expose the `.cpp` files directly).

### Exact list of linked files

| File | Relative path from the `vianium-crypto` sibling | Use in Vianigram |
|---------|-------------------------------|-------------------|
| `sha256.h/cpp` | `..\vianium-tls\src\crypto\sha256.{h,cpp}` | msg_key derivation, key fingerprints, generic hashing |
| `sha512.h/cpp` | `..\vianium-tls\src\crypto\sha512.{h,cpp}` | SRP-2048, PBKDF2-SHA512 base |
| `hkdf.h/cpp` | `..\vianium-tls\src\crypto\hkdf.{h,cpp}` | secret-chat key derivation |
| `hmac.h/cpp` | `..\vianium-tls\src\crypto\hmac.{h,cpp}` | base of PBKDF2, generic MAC |
| `x25519.h/cpp` | `..\vianium-tls\src\crypto\x25519.{h,cpp}` | secret chats DH (Curve25519) |
| `aes_core.h/cpp` | `..\vianium-tls\src\crypto\aes_core.{h,cpp}` | base of AES-IGE |
| `aes_gcm.h/cpp` | `..\vianium-tls\src\crypto\aes_gcm.{h,cpp}` | optional at-rest (encrypted envelope of blobs) |
| `bignum.h/cpp` | `..\vianium-tls\src\crypto\bignum.{h,cpp}` | DH 2048, SRP-2048 |

### vcxproj configuration (inside the `vianium-crypto` sibling)

```xml
<!-- Vianium.Crypto.vcxproj -->
<ItemGroup Label="Reused from vianium-tls">
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

<ItemDefinitionGroup>
  <ClCompile>
    <AdditionalIncludeDirectories>
      $(MSBuildProjectDirectory)\;
      $(MSBuildProjectDirectory)\..\vianium-kernel\include\;
      $(MSBuildProjectDirectory)\..\vianium-tls\src\;
    </AdditionalIncludeDirectories>
  </ClCompile>
</ItemDefinitionGroup>
```

### Advantage of source-level linking

- When upstream `vianium-tls` fixes a bug (e.g., a constant-time issue in `bignum.cpp`), the `vianium-crypto` sibling inherits it in the next build with no manual work; Vianigram, in turn, inherits it via the sibling's WinMD.
- Upstream self-tests run inside `vianium-crypto` too (the `CryptoSelfTest::RunAll` is portable).
- Zero duplication of logic → zero silent divergence.

---

## 3. New primitives (Telegram-specific)

These live inside the `vianium-crypto` sibling, in `src/domain/services/` or `src/infrastructure/codec/`:

### 3.1. AES-256-IGE (Infinite Garble Extension)

**It is not standard.** Custom to Telegram. Construction:

```
IGE encryption block by block:
  C_i = E_K(P_i XOR C_{i-1}) XOR P_{i-1}

where:
  P_0  = first plaintext block
  C_0  = E_K(P_0 XOR IV1) XOR IV2     // two halves of the 32-byte IV
  IV1  = IV[0..15]
  IV2  = IV[16..31]

Decryption block by block:
  P_i = D_K(C_i XOR P_{i-1}) XOR C_{i-1}
```

**Properties:**
- 16-byte blocks (AES block size).
- The plaintext must be a multiple of 16 bytes.
- A 32-byte IV (two AES blocks).
- Without authentication (Telegram combines it with `msg_key` for integrity).

**Implementation:**
```
src/domain/services/aes_ige.{h,cpp}
```

```cpp
namespace vianium::crypto::domain {

class AesIge {
public:
    // key: 32 bytes (AES-256). iv: 32 bytes. plaintext/ciphertext: a multiple of 16.
    // In-place: src and dst may point to the same buffer.
    static Result<void, CryptoError> Encrypt(
        const uint8_t key[32], const uint8_t iv[32],
        const uint8_t* plaintext, size_t len,
        uint8_t* ciphertext);

    static Result<void, CryptoError> Decrypt(
        const uint8_t key[32], const uint8_t iv[32],
        const uint8_t* ciphertext, size_t len,
        uint8_t* plaintext);

private:
    // Internally uses Vianium::crypto::AesCore::EncryptBlock / DecryptBlock
    // from aes_core.h linked from the vianium-tls sibling.
};

}
```

**Self-test vectors:**
- NIST SP 800-38A AES-256 vectors (AES core validation).
- IGE-specific: Telegram publishes vectors at https://core.telegram.org/api/end-to-end (the "Sample Implementations" section). Roundtrip + tamper detection.

### 3.2. DH 2048-bit (MTProto handshake)

Builds on `bignum.h`:
- ModPow constant-time scalar processing (the same Montgomery ladder branch as x25519, but over a 2048-bit bignum).
- Prime allowlist: Telegram publishes safe primes in the spec; Vianigram hashes them SHA-256 and verifies the match at first use via `dh_prime_validator`.
- Generator g always small (2 or 3) as set by the server.

**Implementation:**
```
src/domain/services/dh_2048.{h,cpp}
src/domain/services/dh_prime_validator.{h,cpp}
```

```cpp
class Dh2048 {
public:
    // Generates the private key b (2048-bit), returns g^b mod p (public).
    // out_priv and out_pub must be 256 bytes.
    static Result<void, CryptoError> GenerateKeyPair(
        IRandom* rng, const BigNum& prime, int g,
        uint8_t out_priv[256], uint8_t out_pub[256]);

    // Computes shared = peer^my_priv mod p
    static Result<void, CryptoError> ComputeSharedSecret(
        const uint8_t my_priv[256], const uint8_t peer_pub[256],
        const BigNum& prime, uint8_t out_shared[256]);

private:
    // Constant-time ModPow ladder
};
```

**Time budget:** ModPow 2048-bit on a Snapdragon S4 ~600-900 ms. Acceptable because the handshake is one-shot.

**Self-test:** RFC 7919 ffdhe2048 group + custom test vectors derived from the Telegram MTProto paper.

### 3.3. SRP-2048 (2FA password authentication)

RFC 5054 with a 2048-bit prime. Telegram uses this protocol for login with 2FA:

```
Server provides: salt, prime p, generator g, B (server public)
Client computes:
  x = SHA-256(salt + SHA-256(salt + password))    // or a PBKDF2 variant depending on the protocol version
  a = random 256 bytes
  A = g^a mod p
  u = SHA-256(A || B)
  S = (B - 3 * g^x)^(a + u*x) mod p              // shared secret
  K = SHA-256(S)
  M1 = SHA-256(A || B || K)
  → the client sends A, M1
```

Telegram has **two** supported SRP algorithms:
- `SecurePasswordKdfAlgoSHA512_SHA256` (legacy).
- `SecurePasswordKdfAlgoModPow#3a912d4a` (current, layer 214).

The current algorithm uses PBKDF2-SHA512 over salt2 before the SRP modulus. The exact detail is in the Telegram spec (`auth.checkPassword`).

**Implementation:**
```
src/domain/services/srp_2048.{h,cpp}
```

```cpp
class Srp2048 {
public:
    struct Params {
        uint8_t salt1[32];
        uint8_t salt2[32];
        uint8_t prime[256];
        int g;
        uint8_t srp_B[256];        // server public
        int64_t srp_id;
    };
    struct Result {
        uint8_t srp_A[256];
        uint8_t srp_M1[32];
    };

    static vianium::kernel::Result<Result, CryptoError> Compute(
        const Params& params,
        const wchar_t* password, size_t password_len,
        IRandom* rng);
};
```

**Self-test:** RFC 5054 test vectors + Telegram-published vectors if available.

### 3.4. PBKDF2-SHA512

RFC 2898. Builds on HMAC-SHA512 (which already comes from the reused `hmac.h` + `sha512.h`).

**Implementation:**
```
src/domain/services/pbkdf2_sha512.{h,cpp}
```

```cpp
class Pbkdf2Sha512 {
public:
    static Result<void, CryptoError> Derive(
        const uint8_t* password, size_t password_len,
        const uint8_t* salt, size_t salt_len,
        uint32_t iterations,
        uint8_t* out_key, size_t out_len);
};
```

**Iterations:** Telegram specifies 100000 iterations as a baseline for SRP. Configurable by the server response.

**Self-test:** RFC 7914 vectors + a custom 100000-iteration sanity check.

### 3.5. Secret chat DH (Curve25519)

Reuses `x25519.h` from the `vianium-tls` sibling (linked from `vianium-crypto`). **What is new:** key fingerprint computation.

**Key fingerprint** (shown to the user as an emoji set for verification):
```
fingerprint_bytes = SHA-1(root_key)[0..15]    // the first 16 bytes of the SHA-1
emoji_indices = {
    fingerprint_bytes[0..3]  modulo 333,
    fingerprint_bytes[4..7]  modulo 333,
    fingerprint_bytes[8..11] modulo 333,
    fingerprint_bytes[12..15] modulo 333,
    extra computed from sha256_of_root + msg_key
}
```

Telegram publishes a table of 333 emojis. Each index identifies one. 4-5 emojis = ~2^36 entropy, visually comparable.

**Implementation:**
```
src/domain/services/key_fingerprint.{h,cpp}
src/infrastructure/data/emoji_table.{h,cpp}    ← embedded table of 333 emojis
```

```cpp
class KeyFingerprint {
public:
    struct EmojiSet {
        int32_t indices[5];
        Platform::String^ ToDisplayString() const;     // helper for the WinRT shim
    };

    static Result<EmojiSet, CryptoError> Compute(const uint8_t root_key[256]);
};
```

### 3.6. Random source

`IRandom` port (in `ports/outbound/i_random.h`). Implementation:
```
src/infrastructure/random/winrt_random.{h,cpp}
```

```cpp
class WinRtRandom : public ports::outbound::IRandom {
public:
    Result<void, CryptoError> Generate(uint8_t* out, size_t len) override {
        auto buf = Windows::Security::Cryptography::CryptographicBuffer::GenerateRandom(static_cast<uint32_t>(len));
        Platform::Array<uint8_t>^ arr = ref new Platform::Array<uint8_t>(static_cast<uint32_t>(len));
        Windows::Security::Cryptography::CryptographicBuffer::CopyToByteArray(buf, &arr);
        memcpy(out, arr->Data, len);
        return Result<void, CryptoError>::Ok();
    }
};
```

`CryptographicBuffer::GenerateRandom` uses BCryptGenRandom under the hood — a validated CSPRNG.

**Never a fallback to `rand()`.** If CryptographicBuffer fails, `IRandom::Generate` returns an error and the use case will fail the handshake rather than proceed with weak entropy.

---

## 4. Constant-time guarantees

Lessons from the `vianium-tls` sibling (`chacha20_poly1305.cpp::ConstantTimeEquals16`) and from its `bignum.cpp` ladders:

```cpp
// src/domain/services/constant_time.h (in the vianium-crypto sibling)
namespace vianium::crypto::domain {

inline bool CtEqual(const uint8_t* a, const uint8_t* b, size_t len) {
    uint8_t diff = 0;
    for (size_t i = 0; i < len; i++) {
        diff |= (a[i] ^ b[i]);
    }
    return diff == 0;
}

inline void CtZeroize(uint8_t* p, size_t len) {
    volatile uint8_t* vp = p;
    for (size_t i = 0; i < len; i++) vp[i] = 0;
}

}
```

**Mandatory use:**
- Comparison of `auth_key_id`, `msg_key`, `srp_M1`, fingerprints → `CtEqual`.
- Destructor of `AuthKey`, `RootKey`, password plaintext buffers → `CtZeroize`.
- The DH ModPow ladder is already constant-time from `bignum.cpp` upstream (sibling `vianium-tls`).

**What does NOT need constant-time:**
- Comparison of constructor IDs (public, not sensitive).
- Comparison of msg_id (public on the wire).

---

## 5. API surfaces (internal interfaces)

### 5.1. `IMTProtoCrypto` (port consumed by the `vianium-mtproto` sibling)

```cpp
// ports/inbound/i_mtproto_crypto.h (in the vianium-crypto sibling)
namespace vianium::crypto::ports::inbound {

class IMTProtoCrypto {
public:
    // Handshake
    virtual Result<HandshakePqResult, CryptoError> ProcessReqPqResponse(
        const uint8_t* res_pq_bytes, size_t len) = 0;
    virtual Result<HandshakeDhParamsResult, CryptoError> ProcessServerDhParams(
        const uint8_t* server_dh_bytes, size_t len,
        const uint8_t new_nonce[32], const uint8_t server_nonce[16]) = 0;
    virtual Result<AuthKeyDerivation, CryptoError> ComputeAuthKey(
        const uint8_t* g_a, size_t g_a_len,
        const uint8_t prime[256]) = 0;

    // Per-message encryption
    virtual Result<void, CryptoError> EncryptMessage(
        const AuthKey& auth_key,
        const uint8_t* plaintext, size_t plaintext_len,
        uint8_t out_msg_key[16],
        uint8_t* out_ciphertext) = 0;

    virtual Result<void, CryptoError> DecryptMessage(
        const AuthKey& auth_key,
        const uint8_t msg_key[16],
        const uint8_t* ciphertext, size_t ciphertext_len,
        uint8_t* out_plaintext) = 0;

    virtual Result<uint64_t, CryptoError> ComputeAuthKeyId(const AuthKey& auth_key) = 0;
};

}
```

### 5.2. `ISecretChatCrypto` (port consumed by managed Secret Chat use cases)

```cpp
// ports/inbound/i_secret_chat_crypto.h (in the vianium-crypto sibling)
namespace vianium::crypto::ports::inbound {

class ISecretChatCrypto {
public:
    // DH key exchange
    virtual Result<DhKeyPair, CryptoError> GenerateDhKeyPair(IRandom* rng,
        const uint8_t prime[256], int g) = 0;

    virtual Result<RootKey, CryptoError> DeriveRootKey(
        const uint8_t my_priv[256], const uint8_t peer_pub[256],
        const uint8_t prime[256]) = 0;

    // Fingerprint visualization
    virtual Result<KeyFingerprint::EmojiSet, CryptoError> ComputeFingerprint(
        const RootKey& root) = 0;

    // Per-message encryption (similar to MTProto but with different key derivation)
    virtual Result<void, CryptoError> EncryptSecretMessage(
        const RootKey& root, uint64_t msg_id,
        const uint8_t* plaintext, size_t len,
        uint8_t out_msg_key[16],
        uint8_t* out_ciphertext) = 0;

    virtual Result<void, CryptoError> DecryptSecretMessage(
        const RootKey& root, uint64_t msg_id,
        const uint8_t msg_key[16],
        const uint8_t* ciphertext, size_t len,
        uint8_t* out_plaintext) = 0;

    // Rekey (PFS - perfect forward secrecy)
    virtual Result<RootKey, CryptoError> RekeyDh(
        const RootKey& current,
        const uint8_t new_priv[256], const uint8_t new_peer_pub[256],
        const uint8_t prime[256]) = 0;
};

}
```

### 5.3. `IAtRestCrypto` (interface; impl in managed)

```cpp
// ports/inbound/i_at_rest_crypto.h (in the vianium-crypto sibling)
namespace vianium::crypto::ports::inbound {

class IAtRestCrypto {
public:
    // Wraps Windows::Security::Cryptography::DataProtection::DataProtectionProvider.
    // Implementation lives managed-side (Vianigram.Storage.Encryption);
    // native side just defines this interface for symmetry.
    // In practice, `IAtRestCrypto` is not invoked from native; managed code
    // uses DPAPI directly. This interface exists for completeness and the future
    // possibility of moving the logic to native.
    virtual Result<Span<const uint8_t>, CryptoError> Protect(
        const uint8_t* plaintext, size_t len, IAllocator* outAlloc) = 0;
    virtual Result<Span<const uint8_t>, CryptoError> Unprotect(
        const uint8_t* protected_blob, size_t len, IAllocator* outAlloc) = 0;
};

}
```

Recommended DPAPI scopes (configured managed-side):
- `LOCAL=user` for auth_key, root_key (no roaming).
- `LOCAL=machine` optionally for shared system caches.

Detailed documentation in `docs/security/at-rest-encryption.md`.

---

## 6. File structure under `src/` (in the `vianium-crypto` sibling)

```
src/
├── pch.h, pch.cpp
├── domain/
│   ├── value_objects/
│   │   ├── auth_key.{h,cpp}                ← 256 bytes + Id() + AuxHash() + zeroize destructor
│   │   ├── root_key.{h,cpp}                ← 256 bytes secret-chat
│   │   ├── msg_key.h
│   │   ├── srp_params.h
│   │   └── dh_params.h
│   ├── policies/
│   │   ├── dh_prime_policy.{h,cpp}         ← allowlist of canonical primes (hashed)
│   │   ├── srp_iteration_policy.h          ← min 100000 iterations
│   │   └── time_drift_policy.h             ← server_time vs client_time ±300s
│   ├── events/
│   │   ├── key_derived.h
│   │   ├── fingerprint_computed.h
│   │   ├── integrity_check_failed.h
│   │   ├── self_test_passed.h
│   │   └── self_test_failed.h
│   └── services/
│       ├── aes_ige.{h,cpp}                 ← AES-256-IGE encrypt/decrypt
│       ├── dh_2048.{h,cpp}
│       ├── dh_prime_validator.{h,cpp}
│       ├── srp_2048.{h,cpp}
│       ├── pbkdf2_sha512.{h,cpp}
│       ├── key_fingerprint.{h,cpp}
│       ├── msg_key_deriver.{h,cpp}         ← (also lives in the MTProto domain;
│       │                                       the important thing is that it is not duplicated
│       │                                       — the one that applies is in Crypto and
│       │                                       MTProto consumes it via a port)
│       ├── aes_key_iv_deriver.{h,cpp}
│       ├── auth_key_id_computer.{h,cpp}    ← lower 64 bits of SHA-1
│       └── constant_time.h                  ← inline CtEqual / CtZeroize
├── application/
│   ├── use_cases/
│   │   ├── derive_auth_key_use_case.{h,cpp}
│   │   ├── encrypt_mtproto_message_use_case.{h,cpp}
│   │   ├── decrypt_mtproto_message_use_case.{h,cpp}
│   │   ├── compute_srp_response_use_case.{h,cpp}
│   │   ├── derive_secret_chat_root_use_case.{h,cpp}
│   │   ├── compute_fingerprint_use_case.{h,cpp}
│   │   └── rekey_secret_chat_use_case.{h,cpp}
│   └── services/
│       └── crypto_session_factory.{h,cpp}  ← orchestrates self-tests on construction
├── ports/
│   ├── inbound/
│   │   ├── i_mtproto_crypto.h
│   │   ├── i_secret_chat_crypto.h
│   │   └── i_at_rest_crypto.h              ← interface; impl managed-side
│   └── outbound/
│       ├── i_random.h                       ← rng port; impl WinRtRandom
│       ├── i_clock.h                        ← Vianium.Core.Kernel
│       └── i_logger.h                       ← Vianium.Core.Kernel
├── infrastructure/
│   ├── random/
│   │   └── winrt_random.{h,cpp}
│   ├── codec/
│   │   └── (parsers for handshake messages — shared with MTProto's serializer
│   │        but the crypto logic lives here)
│   ├── data/
│   │   ├── emoji_table.{h,cpp}             ← 333 embedded emoji codepoints
│   │   ├── canonical_dh_primes.{h,cpp}     ← list of expected SHA-256 hashes
│   │   └── telegram_rsa_pubkeys.{h,cpp}    ← 4 RSA pubkeys (fingerprints + n + e)
│   └── platform/
│       └── (empty; the WinRT RNG is the only platform-specific feature
│           and lives in infrastructure/random/)
├── api/
│   └── v1/
│       ├── pch.h
│       ├── c_api.cpp                        ← vng_crypto_api.h impl
│       └── winrt_shim.cpp                   ← CryptoSession sealed ref class
└── internal/
    ├── crypto_log.h
    └── self_test.{h,cpp}                    ← extends CryptoSelfTest from the vianium-tls sibling
```

---

## 7. Public API surface (WinMD)

```cpp
namespace Vianium::Crypto::v1 {

public ref struct EmojiFingerprint sealed {
    property Platform::Array<int32_t>^ Indices { Platform::Array<int32_t>^ get(); }
    property Platform::String^ DisplayString { Platform::String^ get(); }
};

public ref struct SrpResponse sealed {
    property Windows::Storage::Streams::IBuffer^ A;
    property Windows::Storage::Streams::IBuffer^ M1;
    property int32_t ErrorCode;
};

public ref class CryptoSession sealed {
public:
    static CryptoSession^ Create();

    // Self-test status
    property bool SelfTestPassed { bool get(); }

    // SRP (the managed layer invokes it during 2FA login)
    Windows::Foundation::IAsyncOperation<SrpResponse^>^ ComputeSrpResponseAsync(
        Platform::String^ password,
        Windows::Storage::Streams::IBuffer^ salt1,
        Windows::Storage::Streams::IBuffer^ salt2,
        Windows::Storage::Streams::IBuffer^ prime,
        int32_t g,
        Windows::Storage::Streams::IBuffer^ srpB,
        int64_t srpId);

    // Secret chat fingerprint (the managed layer shows it to the user)
    Windows::Foundation::IAsyncOperation<EmojiFingerprint^>^ ComputeFingerprintAsync(
        Windows::Storage::Streams::IBuffer^ rootKey);

    // The rest of the crypto surface (encrypt MTProto message, derive auth_key)
    // is NOT exposed to managed directly — the MTProto channel consumes it via
    // the internal C ABI. Managed only invokes what it needs that is user-facing.
};

}
```

**Note:** most of the crypto API does NOT cross the WinMD ABI to managed. The `vianium-mtproto` sibling consumes `IMTProtoCrypto` via `vianium/crypto/api/v1/crypto_api.h` (C ABI). Only SRP and the fingerprint emoji are exposed to managed because they are the only cases where managed **initiates** a crypto operation.

---

## 8. Self-tests (DEBUG)

Extends `Vianium::Tls::CryptoSelfTest` (sibling `vianium-tls`) with specific vectors:

```cpp
namespace vianium::crypto::internal {

class CryptoSelfTest {
public:
    static Result<void, SelfTestFailure> RunAll(ILogger* log);

private:
    // Inherited (delegated to Vianium::Tls::CryptoSelfTest from the vianium-tls sibling)
    static void RunUpstreamTests(ILogger* log);   // sha256, sha512, hkdf, hmac, x25519, aes_core, aes_gcm, bignum

    // New
    static void TestAesIgeNistVectors(ILogger* log);
    static void TestAesIgeRoundtrip(ILogger* log);
    static void TestAesIgeTamperDetection(ILogger* log);     // bit flip + verifies that decrypt produces garbage
    static void TestDh2048Rfc7919(ILogger* log);
    static void TestDh2048Roundtrip(ILogger* log);
    static void TestDhPrimeAllowlist(ILogger* log);          // non-canonical prime → policy reject
    static void TestSrp2048Rfc5054(ILogger* log);
    static void TestPbkdf2Sha512Rfc7914(ILogger* log);
    static void TestPbkdf2Sha512Iterations(ILogger* log);    // 100000 iter sanity check
    static void TestKeyFingerprintTelegramVectors(ILogger* log);
    static void TestConstantTimeEqualSimple(ILogger* log);
    static void TestZeroizeAfterDestroy(ILogger* log);       // verifies that the AuthKey destructor erases memory
    static void TestRandomEntropyWeakCheck(ILogger* log);    // 1 KB random, chi-square sanity
};

}
```

If **any** test fails → `CryptoSession::Create` returns null + `selftest.failed_total` increments with the tag of the specific test. The managed layer does not proceed with login if Crypto refuses service.

---

## 9. Telemetry

| Metric | Type | When |
|---------|------|--------|
| `crypto.aes_ige.encrypt_throughput_mbps` | Histogram | Each AES-IGE encrypt operation > 16 KB |
| `crypto.aes_ige.decrypt_throughput_mbps` | Histogram | Each decrypt > 16 KB |
| `crypto.dh.modpow_duration_ms` | Histogram | Each DH ModPow 2048-bit |
| `crypto.srp.compute_duration_ms` | Histogram | Each SRP-2048 compute |
| `crypto.pbkdf2.iterations_actual` | Gauge | Iterations used (server-controlled) |
| `crypto.fingerprint.compute_duration_us` | Histogram | Each fingerprint compute |
| `crypto.selftest.duration_ms` | Histogram | The full cycle of self-tests |
| `crypto.selftest.passed_total{test}` | Counter | Per-test pass |
| `crypto.selftest.failed_total{test}` | Counter | Per-test fail |
| `crypto.random.bytes_generated_total` | Counter | Total RNG output |
| `crypto.dh_prime_rejected_total` | Counter | Each time `dh_prime_validator` rejects |

---

## 10. Risks and mitigations

| Risk | Mitigation |
|--------|------------|
| The AES-IGE custom mode has an undiscovered weakness | Clean-room implementation from the Telegram spec; self-test against tamper detection; staying current with academic research. |
| A malicious DH 2048 prime from the server (downgrade attack) | `dh_prime_validator` checks the SHA-256 against the canonical allowlist. Telemetry counter. |
| SRP-2048 timing leak in the bignum ModPow | `bignum.cpp` upstream is already constant-time (verified in the `vianium-tls` sibling's tests). It is inherited. |
| A Telegram RSA pubkey is rotated → the client cannot start the handshake | RSA pubkeys hardcoded in `infrastructure/data/telegram_rsa_pubkeys.cpp`. Update via an app update. Telemetry counter `crypto.unknown_rsa_fingerprint_total` for alerting. |
| auth_key plaintext exposed via a crash dump | A custom unhandled exception filter (managed side) that clears sensitive memory before a minidump (best-effort). `CtZeroize` in the destructor. Defense in depth. |
| A constant-time violation introduced in a refactor | The self-test includes a synthetic timing comparison (1M iterations same vs different inputs); flags if the delta exceeds a threshold. |
| The RNG returns predictable bytes (a CryptographicBuffer bug) | Chi-square sanity check at DEBUG startup; a failure blocks crypto. |
| Side-channel via cache timing (rare on ARM but possible) | The ARM Snapdragon S4 has small L1 caches; the AES core uses table-free S-boxes when possible. AES-NI is NOT available on this hardware. |
| A schema bump changes the DH params → a client with an old prime allowlist fails | Telemetry alerting; a release mechanism to push a new allowlist via an app update. |

---

## 11. Phasing

**Phase 1 (ROADMAP):**
- Source-level link of the 8 upstream crypto files.
- AES-IGE implementation + tests (NIST + IGE-specific tamper).
- DH 2048 + prime allowlist + DH self-test.
- PBKDF2-SHA512 + RFC test.
- SRP-2048 + RFC 5054 test.
- `IMTProtoCrypto` port + an impl that orchestrates auth_key derivation, msg_key derivation, encrypt/decrypt.
- Minimal WinRT shim (`CryptoSession::Create`, `ComputeSrpResponseAsync`).
- Self-tests at DEBUG startup.
- Smoke test: DH handshake end-to-end.

**Phase 5 (Secret chats):**
- `ISecretChatCrypto` port + an impl with the reused `x25519.h`.
- Key fingerprint computation + the 333-emoji table embedded.
- Rekey DH for PFS.
- The WinRT shim adds `ComputeFingerprintAsync`.
- Smoke test: secret chat handshake + message encrypt/decrypt + fingerprint match with the peer-published one.

**Phase 6 (VoIP):**
- `IVoipCrypto` port (in this sibling or in the `vianium-voip` sibling): AES-256-CTR for RTP payload encryption.

**Deferred:**
- AES-NI hardware acceleration: WP8.1 ARM does not expose AES instructions consistently. Defer until a future platform.
- Post-quantum hybrid: investigation work, out of scope.
- Libsodium-style high-level API: Vianigram does not need the wrapper; it uses the primitives directly.
