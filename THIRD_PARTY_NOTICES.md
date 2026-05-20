# Third-Party Notices — Vianigram

This product includes software developed by third parties, plus public
specifications and reference implementations consulted during clean-room
development. Below is the catalog with respective licenses.

For the policy governing what may be vendored and how, see
`..\vianium-docs\references-and-licensing.md` (inherited via the sibling repo
`vianium-docs`) and the clean-room methodology described in
`docs\managed-architecture\principles.md` §"OSS code policy".

> **Status:** This file is **authoritative**. Every vendored library MUST
> appear here before it can be merged. Build CI verifies (or will verify)
> that no `<context>/Infrastructure/Vendored/<lib>/` directory exists
> without a corresponding entry below.

> **Currently:** No third-party libraries are vendored yet (project is in
> Phase 0 — foundation). Entries below are **planned** and will be
> activated as bounded contexts implement their phases.

---

## Specifications and protocol references (clean-room)

These are public specs read in clean-room mode (Modo A: read spec, then
implement from spec, not from any reference implementation's source code).
No code is vendored from these — they are listed for traceability.

### Telegram MTProto 2.0

- **Source:** https://core.telegram.org/mtproto
- **License:** Public specification (Telegram FZ-LLC publishes it
  expressly for third-party clients).
- **Used by:** Sibling repos `vianium-mtproto` (including its
  `src\mtproto\` and `src\tl\` subcomponents) and `vianium-crypto`.
  Vianigram consumes those via their published WinMDs.
- **Notes:** TL schema (`scheme.tl`) is part of the spec. Schema layer
  214 is the build target. Reading allowed; clean-room implementation
  required.

### Telegram TL Schema (layer 214)

- **Source:** https://core.telegram.org/schema (TL combinator language).
- **License:** Public spec.
- **Used by:** `tools\tl-codegen\` (build-time codegen) emits
  `tl_layer_214.h/cpp` consumed by the sibling `vianium-mtproto`
  (`src\tl\`).

### RFC 7748 — Elliptic Curves for Security (Curve25519, X25519)

- **Source:** https://datatracker.ietf.org/doc/html/rfc7748
- **License:** RFC text — IETF Trust Legal Provisions; usage of the
  protocol is unencumbered.
- **Used by:** Sibling `vianium-crypto` (X25519 ECDH for secret chats);
  Vianigram references via its WinMD.

### RFC 7539 — ChaCha20 and Poly1305 for IETF Protocols

- **Source:** https://datatracker.ietf.org/doc/html/rfc7539
- **License:** RFC text.
- **Used by:** Sibling `vianium-crypto` (potential AEAD for at-rest in
  Phase 5+; primary AEAD for at-rest is AES-GCM via `BCryptAuthenticated*`).

### RFC 8446 — TLS 1.3

- **Source:** https://datatracker.ietf.org/doc/html/rfc8446
- **License:** RFC text.
- **Used by:** Indirectly via project reference to the sibling
  `..\vianium-tls\` (TLS 1.3 Mozilla Modern). MTProto itself does not
  use TLS — it encrypts at its own level.

---

## Planned vendoring (not yet active)

The following libraries are approved for clean-room study + selective
vendoring. They will appear with full notices once actually copied into
the repository.

### Opus codec

- **Version:** TBD (will pin specific commit at vendoring time).
- **Source:** https://opus-codec.org/ (reference impl); upstream
  `https://gitlab.xiph.org/xiph/opus`.
- **License:** BSD-3-Clause.
- **Planned vendor location:** sibling `vianium-voip\third_party\opus\`
  (selective: encoder + decoder, drop tests).
- **Used by:** Sibling `vianium-voip` (voice call codec, Phase 5);
  `Vianigram.Core.Media` (voice message decode, Phase 3).
- **Status:** Approved, not yet vendored.

### libwebp

- **Version:** TBD.
- **Source:** https://chromium.googlesource.com/webm/libwebp
- **License:** BSD-3-Clause (Google).
- **Planned local copy:** `Core\Vianigram.Core.Media\Infrastructure\
  Vendored\libwebp\` (selective: decoder only, drop encoder + demux
  + tests).
- **Used by:** `Vianigram.Core.Media` (WebP decode for stickers and
  photos, Phase 3).
- **Status:** Approved, not yet vendored.

### libvpx

- **Version:** TBD.
- **Source:** https://chromium.googlesource.com/webm/libvpx
- **License:** BSD-3-Clause (Google).
- **Planned vendor location:** sibling `vianium-voip\third_party\libvpx\`
  (selective: VP8 decoder for video calls; defer VP9 + encoder).
- **Used by:** Sibling `vianium-voip` (video call decode, Phase 5+,
  feature-flagged — H.264 likely preferred on WP8.1 hardware).
- **Status:** Approved, **conditional** on Phase 5 video decision.
- **Notes:** ARM NEON optimizations relevant for 1 GB-class hardware.

### TweetNaCl

- **Version:** TBD (already partially ported as
  `x25519.cpp` in the sibling `vianium-tls/src/crypto/`).
- **Source:** https://tweetnacl.cr.yp.to/ (Daniel J. Bernstein, Tanja
  Lange et al.)
- **License:** Public Domain.
- **Used by:** Sibling `vianium-crypto` indirectly via reuse from
  `..\vianium-tls\src\crypto\x25519.{h,cpp}` (X25519 scalar
  multiplication). Vianigram consumes through the
  `vianium-crypto` WinMD.
- **Status:** Active in the sibling `vianium-tls`; the sibling
  `vianium-crypto` references it via `<AdditionalIncludeDirectories>`
  or selective copy.

### BoringSSL — constant-time patterns (study only)

- **Source:** https://boringssl.googlesource.com/boringssl/
- **License:** Apache 2.0 (relevant individual files may carry an
  additional ISC/OpenSSL notice).
- **Mode:** Study only (Modo A). Read for constant-time implementation
  patterns (SRP-2048 bignum, AES-GCM tag comparison). No textual vendor.
- **Used by:** Sibling `vianium-crypto` (clean-room SRP-2048 + AES-IGE
  guided by BoringSSL crypto layout).
- **Status:** Reference only.

### BearSSL — alternative reference for constant-time crypto

- **Source:** https://www.bearssl.org/
- **License:** MIT.
- **Mode:** Study; potential selective vendor for `bignum` if the
  in-tree `bignum.cpp` (sibling `vianium-tls`) proves insufficient for
  SRP-2048.
- **Status:** Approved, not yet vendored.

### SQLite

- **Version:** TBD (pin amalgamation version).
- **Source:** https://www.sqlite.org/
- **License:** Public Domain.
- **Planned local copy:** `Core\Vianigram.Storage\Infrastructure\
  Vendored\sqlite\` or used via the `Microsoft.Data.SQLite` package
  (TBD in Phase 3).
- **Used by:** `Vianigram.Storage` (Phase 3 — at-rest encrypted local
  cache).
- **Status:** Approved, not yet vendored. Encryption layer in `Storage`
  via `DataProtectionProvider`, not via SQLCipher.

### Lottie reference (animated stickers)

- **Source:** https://github.com/airbnb/lottie-android (reference;
  iOS/web variants as well).
- **License:** Apache 2.0.
- **Mode:** Study only — native implementations portable to WP8.1
  are scarce. Likely path: load Lottie JSON and rasterize via a
  custom Canvas2D in `Vianigram.Stickers`.
- **Status:** Under evaluation.

---

## Active vendored libraries

(empty — no libraries vendored yet)

---

## Audit log

| Date | Action | Library | Reviewer |
|---|---|---|---|
| 2026-04-27 | Initial NOTICES skeleton created | (n/a) | architecture team |

---

## Process for adding a new entry

1. Confirm library is approved per Vianigram OSS policy (inherited from
   `..\vianium-docs\references-and-licensing.md` §6 green list,
   or explicitly approved in a plan revision).
2. Verify license is in approved set (MIT, BSD-2/3-Clause, Apache 2.0,
   ISC, Public Domain). **Reject:** GPL/AGPL, LGPL (only static-link
   if isolation hermetic — review case-by-case).
3. Copy into `Core\<context>\Infrastructure\Vendored\<lib>\` preserving
   all license headers.
4. Create `UPSTREAM.md` next to vendored files documenting commit hash,
   date, maintainer (Vianigram-side), next review date (default +12
   months).
5. **Add entry to this file** with full license text below in
   "License texts of vendored libraries".
6. Update this file's audit log.
7. PR review by the bounded-context maintainer + one additional reviewer.

---

## License texts of vendored libraries (for reference)

(Will be filled in when each library is actually vendored — for now see
upstream LICENSE files at the URLs listed above.)
