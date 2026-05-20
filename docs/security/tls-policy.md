# TLS Policy — Vianigram

**Status:** active · **Profile:** Mozilla Modern (TLS 1.3 + ChaCha20-Poly1305 + X25519 + RSA-PSS) · **Last reviewed:** 2026-04-27

> **Shared document.** Vianigram inherits the TLS policy from the `vianium-tls` library (github.com/vianium/vianium-tls). This file is a synchronized snapshot of the canonical document published by that sibling repo. Vianigram links `Vianium.Core.Tls` via a project reference (`..\vianium-tls\Vianium.Core.Tls.vcxproj`); both projects implement a byte-for-byte identical Mozilla Modern policy. Any change here **MUST** be reflected symmetrically in the `vianium-tls` sibling, or the project reference must be migrated to a submodule with explicit version pinning before divergences are accepted. The canonical source of constants lives in `..\vianium-tls\src\tls\tls_constants.h` and is validated at runtime against `https://www.howsmyssl.com/a/check`.

This document is the authoritative description of the TLS posture Vianigram advertises during HTTPS connections. It is enforced by the native stack `Vianium.Core.Tls` and consumed by the native HTTP client `Vianium.Core.Http`. The managed C# layer does not configure TLS directly; it only selects which transport (`Vianium.Http.HttpClient` native vs. `Windows.Web.Http.HttpClient` fallback) carries a given request.

If you change anything in this document, also update (in the sibling `vianium-tls` repo):
- `src/tls/tls_constants.h` (constants)
- `src/tls/client_hello_builder.cpp` (ClientHello cipher list, supported groups, signature algorithms)
- `src/tls/tls_handshake.cpp` `GetCipherProperties` (server-selected cipher dispatch)
- `Clients/Vianium.Tls.SmokeTests/HowsMySslSmokeTest.cs` (regression assertions in the sibling repo)
- And then mirror in Vianigram: `Clients/Vianigram.SmokeTests/HowsMySslSmokeTest.cs`.

---

## 1. Protocol versions

| Version  | Status         | Reason |
|----------|----------------|--------|
| TLS 1.3  | Preferred      | Forward secrecy by construction; AEAD-only; encrypted handshake; 1-RTT default. |
| TLS 1.2  | Allowed        | Negotiated only with the cipher list below. Session ticket and OCSP stapling supported. |
| TLS 1.1  | **Forbidden**  | Removed from `supported_versions`. |
| TLS 1.0  | **Forbidden**  | BEAST-class, deprecated by all major vendors. |
| SSL 3.0  | **Forbidden**  | POODLE. |

Wire-level: every ClientHello carries `supported_versions` (extension 0x002B) listing TLS 1.3 (0x0304) and TLS 1.2 (0x0303). The record-layer version field is fixed at 0x0303 per RFC 8446 §5.1 middlebox compatibility.

The TLS 1.3 `key_share` extension (extension 0x0033) is **always** populated with a freshly generated X25519 ECDHE public key (32 bytes, RFC 7748). This guarantees TLS 1.3 is reachable without HelloRetryRequest for any X25519-capable server, and falls back via HRR to P-256 / P-384 otherwise.

---

## 2. Cipher suites — ClientHello offer

The advertised list, in order of preference:

### TLS 1.3
| Hex     | IANA name                          | Notes |
|---------|------------------------------------|-------|
| 0x1303  | TLS_CHACHA20_POLY1305_SHA256       | First — ARM-friendly when AES-NI absent (RFC 8446 §B.4 / RFC 7539). |
| 0x1301  | TLS_AES_128_GCM_SHA256             | Mandatory (RFC 8446 §9.1). |
| 0x1302  | TLS_AES_256_GCM_SHA384             | Stronger key, SHA-384 transcript. |

### TLS 1.2
| Hex     | IANA name                                   | Notes |
|---------|---------------------------------------------|-------|
| 0xCCA9  | TLS_ECDHE_ECDSA_WITH_CHACHA20_POLY1305_SHA256 | First — RFC 7905. |
| 0xCCA8  | TLS_ECDHE_RSA_WITH_CHACHA20_POLY1305_SHA256   | RSA path, ChaCha20. |
| 0xC02C  | TLS_ECDHE_ECDSA_WITH_AES_256_GCM_SHA384     | Modern ECDSA + AEAD. |
| 0xC030  | TLS_ECDHE_RSA_WITH_AES_256_GCM_SHA384       | RSA path, AEAD, SHA-384. |
| 0xC02B  | TLS_ECDHE_ECDSA_WITH_AES_128_GCM_SHA256     | ECDSA AEAD. |
| 0xC02F  | TLS_ECDHE_RSA_WITH_AES_128_GCM_SHA256       | Most widely deployed AEAD. |

### Banned suites (deliberately not advertised)
| Pattern                 | Reason banned |
|-------------------------|---------------|
| `TLS_RSA_WITH_*`        | Static-RSA key exchange — no forward secrecy. |
| `*_3DES_EDE_CBC_*`      | Sweet32 (CVE-2016-2183). |
| `*_RC4_*`               | RFC 7465. |
| `*_DES_*`, `*_NULL_*`, `*_anon_*`, `*_EXPORT_*` | Trivially broken. |
| `*_CBC_SHA` (any CBC + SHA-1) | Lucky13 / padding-oracle exposure. |
| `*_CBC_SHA256` (any CBC) | Same family — padding-oracle risk; AEAD is mandatory. |

The constants for banned suites remain declared in `tls_constants.h` so the parser/dispatcher can refuse them defensively if an out-of-spec server selects one — but the ClientHello never offers them.

### ChaCha20-Poly1305 — landed in Phase 1d

Implemented clean-room from RFC 7539 in `..\vianium-tls\src\crypto\`:
- `chacha20.{h,cpp}` — RFC 7539 §2.3 stream cipher; self-tested vs §2.3.2 vector at startup (DEBUG).
- `poly1305.{h,cpp}` — RFC 7539 §2.5 one-time authenticator; self-tested vs §2.5.2 vector.
- `chacha20_poly1305.{h,cpp}` — AEAD construction (RFC 7539 §2.8); self-tested vs §2.8.2 with round-trip + tamper check.
- `chacha20_poly1305_tls_cipher.{h,cpp}` — TLS 1.2 record adapter (RFC 7905 nonce derivation, 13-byte AAD).
- `..\vianium-tls\src\tls\tls13_chacha20_poly1305_record_protection.{h,cpp}` — TLS 1.3 record protection (RFC 8446 §5.2 binding).

Listed first in both TLS 1.3 and TLS 1.2 cipher orders so ARM mobiles without AES-NI fall on the faster path; servers honouring client preference will pick ChaCha20 when both sides support it.

---

## 3. Supported groups (key exchange)

| Hex     | Name        | Status |
|---------|-------------|--------|
| 0x001D  | x25519      | Advertised first; **default key_share** (32-byte public key) on every ClientHello. |
| 0x0017  | secp256r1 (P-256) | Advertised; HelloRetryRequest fallback. |
| 0x0018  | secp384r1 (P-384) | Advertised; HelloRetryRequest fallback. |

X25519 (Curve25519) is the primary group. RFC 7748 implementation lives in `..\vianium-tls\src\crypto\x25519.{h,cpp}`, verified at every DEBUG startup against the §5.2 test vectors and the §6.1 Diffie-Hellman round-trip. Servers that don't support X25519 trigger a HelloRetryRequest pointing at P-256 or P-384, which `tls_handshake.cpp` regenerates on demand.

P-256 / P-384 ECDHE remain available for backwards compatibility. Static finite-field DH (FFDHE) is not offered; modular-exponentiation DH performance and weak parameter handling are unacceptable.

---

## 4. Signature algorithms

Advertised in `signature_algorithms` (extension 0x000D), preference order:

| Hex     | Name              |
|---------|-------------------|
| 0x0804  | rsa_pss_rsae_sha256  |
| 0x0805  | rsa_pss_rsae_sha384  |
| 0x0403  | ecdsa_secp256r1_sha256 |
| 0x0503  | ecdsa_secp384r1_sha384 |
| 0x0401  | rsa_pkcs1_sha256  |
| 0x0501  | rsa_pkcs1_sha384  |

Anything below SHA-256 (SHA-1, MD5) is not offered.

---

## 5. Other ClientHello extensions

| Extension              | Hex    | Behavior |
|------------------------|--------|----------|
| `server_name`          | 0x0000 | Always sent (SNI). |
| `supported_groups`     | 0x000A | See §3. |
| `ec_point_formats`     | 0x000B | Uncompressed only. |
| `signature_algorithms` | 0x000D | See §4. |
| `application_layer_protocol_negotiation` | 0x0010 | Sent when caller provides ALPN list (typically `h2`, `http/1.1`). |
| `session_ticket`       | 0x0023 | Empty payload — "I support tickets" (RFC 5077). |
| `status_request`       | 0x0005 | OCSP stapling requested. **Validation pending**: response is parsed but not yet enforced. |
| `renegotiation_info`   | 0xFF01 | Empty for initial handshake (RFC 5746). |
| `supported_versions`   | 0x002B | TLS 1.3 + TLS 1.2 (only). |
| `psk_key_exchange_modes` | 0x002D | `psk_dhe_ke` only. |
| `key_share`            | 0x0033 | X25519 always; P-256 / P-384 after HRR. |
| `early_data`           | 0x002A | Sent only when offering PSK with non-zero `max_early_data_size`. |
| `pre_shared_key`       | 0x0029 | Sent for TLS 1.3 session resumption; binder recomputed after transcript hash. |

**TLS compression** is hardcoded to `null` only (RFC 7457; CRIME mitigation).

---

## 6. Certificate validation

Implemented in `..\vianium-tls\src\tls\certificate_validator.cpp`.

- Chain construction up to a trusted root (system root store on WP8.1).
- Hostname match (CN + SAN; wildcards per RFC 6125).
- Validity period.
- Signature algorithm restricted to SHA-256 / SHA-384 / RSA-PSS.
- OCSP stapling (RFC 6960) parsed + signature-verified + freshness-clamped (≤ 7 days, 1 h skew tolerance).
- RFC 7633 `id-pe-tlsfeature` (must-staple) enforced when the leaf cert advertises it.
- RFC 6962 §3.3 embedded SCT extension parsed and verified against bundled CT log keys (Argon2025h2, Argon2026h1, Xenon2025h2, Cloudflare Nimbus2026, Sectigo Sabre).
- SPKI pinning (RFC 7469 §2.4 hash) via `IPinValidator` global hook; verdicts `kAccepted | kReportOnly | kRejected | kNoPolicy`.

---

## 7. Transport selection (managed side)

Vianigram routes through:

1. **Primary:** `Vianium.Http.HttpClient` (the native stack governed by this document) via `INetApi.SendAsync`.
2. **Fallback:** `Windows.Web.Http.HttpClient` with `HttpBaseProtocolFilter`. Used only when the native stack throws. The fallback uses Schannel and inherits the WP8.1 OS cipher list, which still offers Sweet32-grade suites; **all responses served by the fallback are TLS-policy-degraded and must be marked accordingly** (`TlsSecurityProfile.Reduced`, no-cache hardening, structured `[HTTP] transport=…` logs).

Both transports live under the `Vianium.Net` bounded context (DDD+Hexagonal), shared between Vianigram and other consumers via the sibling `vianium-net`. The `IHttpTransport` outbound port abstracts which transport was used; only `WinRtFallbackTransport` ever instantiates `HttpBaseProtocolFilter`. Vianigram consumes the same composition root via `Vianigram.Composition.NetCompositionRoot`.

---

## 8. Regression test

`Clients/Vianigram.SmokeTests/HowsMySslSmokeTest.cs` (and its twin in the sibling `vianium-tls` repo) performs a live HTTPS GET against `https://www.howsmyssl.com/a/check` via `Vianium.Http.HttpClient` and asserts:

- `rating != "Bad"`
- `insecure_cipher_suites` is empty
- `tls_compression_supported == false`
- `beast_vuln == false`
- `ephemeral_keys_supported == true`
- `tls_version` contains `"1.2"` or `"1.3"`

The test is not invoked from `SmokeTestRunner.RunAllAsync()` because it requires network connectivity; trigger it from the dedicated TLS validation hook.

---

## 9. Threat model — what this policy defends

| Threat                                  | Defense |
|-----------------------------------------|---------|
| Passive eavesdrop on captured traffic   | TLS 1.3 + ECDHE → forward secrecy. |
| Sweet32 (3DES birthday)                 | 3DES suites not offered. |
| RC4 keystream biases                    | RC4 not offered. |
| BEAST (TLS 1.0 + CBC)                   | TLS ≥ 1.2; CBC not offered. |
| CRIME / TIME (TLS compression)          | Compression `null` only. |
| Lucky13 / padding oracles               | AEAD only (AES-GCM / ChaCha20-Poly1305); CBC not offered. |
| FREAK / Logjam (export crypto)          | Export suites not offered. |
| Static-RSA key compromise (no FS)       | Static-RSA not offered. |
| Downgrade to SSL 3.0 (POODLE)           | Only TLS 1.2 / 1.3 in `supported_versions`. |
| Renegotiation MITM (CVE-2009-3555)      | Secure renegotiation extension sent. |
| Cross-protocol attacks                  | ALPN locks h2 / http1.1. |

### What this policy does **not** defend
- Compromised system root CAs (mitigation: SPKI pinning, Telegram DC pins to be added).
- Quantum adversary with stored ciphertext (mitigation: hybrid PQ key exchange, future work).
- Application-layer attacks (XSS, CSRF, MTProto-level replay) — out of scope for transport. See `mtproto-policy.md` for the MTProto layer.

---

## 10. Change log

| Date       | Change |
|------------|--------|
| 2026-04-27 | Initial Mozilla-Modern-aligned policy. Banned static-RSA, CBC-SHA1, CBC-SHA256 suites in ClientHello. Added 0xC02C (ECDHE-ECDSA-AES-256-GCM-SHA384). |
| 2026-04-27 | ChaCha20-Poly1305 landed: clean-room RFC 7539 primitives + TLS 1.2 (RFC 7905) and TLS 1.3 (RFC 8446 §5.2) record protection wired. Suites 0xCCA8, 0xCCA9, and 0x1303 now advertised first for ARM-friendly negotiation. RFC 7539 self-tests run at startup in DEBUG. |
| 2026-04-27 | X25519 (RFC 7748) landed: clean-room implementation in `crypto/x25519.{h,cpp}` with §5.2 + §6.1 self-tests. Now the **default** TLS 1.3 key_share group; advertised first in supported_groups; HelloRetryRequest fallback handles X25519/P-256/P-384. |
| 2026-04-27 | Phase 5 (pinning), Phase 4 (HSTS), Phase 6 (OCSP + CT), Phase 8 (DoH) closed in sibling `vianium-tls`; Vianigram inherits all of them via the same project reference. |
| 2026-04-27 | **CRITICAL: ChaCha20-Poly1305 AEAD constant-time-equals bug fixed.** `ConstantTimeEquals16` previously used `>> 7` instead of `>> 8`, accepting ~50% of mismatched tags. Fix matches BoringSSL `CRYPTO_memcmp`: cast `diff` to `uint32_t` before subtraction, then `>> 8`. |
| 2026-04-27 | **CRITICAL: RSA pubkey buffer truncation fixed.** `CertificateInfo::publicKeyBytes` raised from `uint8_t[256]` to `uint8_t[1280]` (RSA-8192 + headroom) so RSA-2048 RSAPublicKey DER (~270 bytes) no longer truncates. Regression guard `TestRsa2048PublicKeyParse` added. |
| 2026-04-27 | **🎯 Mozilla-Modern objective achieved — howsmyssl.com confirms "Probably Okay".** Live device validation: TLS 1.3 + 0x1303 (ChaCha20-Poly1305) + X25519 + Let's Encrypt R12 RSA-2048 leaf, all 10 startup `[CryptoSelfTest]` known-answer tests passed. |
| 2026-04-27 | **Vianigram inherits this policy via project reference** to `..\vianium-tls\Vianium.Core.Tls.vcxproj`. No Vianigram-specific TLS configuration. The Telegram DC endpoints (DC1..DC5: `pluto/venus/aurora/vesta/flora.web.telegram.org` and the IP fallbacks `149.154.175.50/.167.50/.175.100/.167.91/.171.5`) all complete TLS 1.3 + X25519 + ChaCha20-Poly1305 negotiation against this stack; the MTProto handshake (`req_pq` → `set_client_DH_params`) is layered on top of the resulting TLS tunnel. See `mtproto-policy.md` for the application-layer crypto invariants and `at-rest-encryption.md` for how auth keys persist between sessions. |
