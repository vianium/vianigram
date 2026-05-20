# At-Rest Encryption Policy — Vianigram

**Status:** active · **Last reviewed:** 2026-04-27

This policy defines **how Vianigram protects sensitive data when it is on disk**, in `LocalSettings`, in the package file system, and in auxiliary databases. It is complementary to:

- `tls-policy.md` — confidentiality in transit.
- `mtproto-policy.md` — confidentiality at the application layer against the Telegram server.

The goal of this document is confidentiality **against an attacker with physical access to the powered-off device or to a backup of the package**.

---

## 1. Crypto provider

Vianigram **MUST** use `Windows.Security.Cryptography.DataProtection.DataProtectionProvider` (DPAPI-WP, part of WP8.1 platform crypto) for all at-rest encrypted material. The obligation is strict and is enforced in `principles.md` via a grep ban-list (`AesManaged`, `RijndaelManaged`, `XOR`, custom obfuscation).

### Reasons

- **TPM binding when available**: on SoCs with TPM 2.0 (devices with TPM 2.0 and later), DPAPI-WP derives the master key from the TPM, not from the user. A backup of the package to another device does NOT decrypt.
- **Platform audit**: the implementation is audited by Microsoft and receives security patches without Vianigram having to redo crypto.
- **Do not reinvent AEAD**: any attempt to "wrap with AES-256-GCM ourselves" introduces the risk of nonce reuse, incorrect key derivation, or a predictable IV. No.

### Scopes

| Scope string | Meaning | Use |
|--------------|-------------|-----|
| `LOCAL=user` | Per-user, device-bound. Survives an app update, NOT a factory reset, NOT a transfer of the package to another device. | **Default** for all of Vianigram's critical data. |
| `LOCAL=machine` | Per-machine. Any app of the same user can decrypt. | **PROHIBITED** — unacceptable cross-app attack surface. |
| `WEBCREDENTIALS=...` | Sync via Microsoft Account. | **PROHIBITED in v1**. Auth keys leaving the device violate the MTProto model. |
| `LOCAL=user AND ...` | Composition with an MS account for future sync. | Out of scope; documented here only to warn against accidental adoption. |

Cross-device sync (future) would require a re-encryption layer with a key derived from a human credential, not DPAPI directly.

---

## 2. Data classification

Every persisted datum falls into one of five classes. The classification determines the encryption regime.

| Class | Examples | Encryption | Notes |
|-------|----------|------------|-------|
| **Critical** | `auth_key` (one per DC), secret chat root keys, SRP-derived `passcode_hash` | DataProtectionProvider, scope=`LOCAL=user`, **always-on** | Never appears in logs. Filtered by `LoggingPolicy.RedactCriticalKeys` before any formatter. |
| **Sensitive** | `server_salt`, `msg_id` state, `session_id`, `last_seen_msg_id`, `auth_key_id` reverse index | DataProtectionProvider, scope=`LOCAL=user` | Performance: lazy encryption, decrypted on reconnect. Cached in RAM while the session is alive. |
| **Personal** | User profile, contacts, dialog list, last message preview | Plaintext (default) **OR** encrypted with a master key derived from the Local Passcode (opt-in §4) | User-controlled. The decision is in the setup wizard. |
| **Bulk** | Message bodies, downloaded media, chat history blobs | Plaintext (default) **OR** encrypted with the master key (opt-in) | Performance trade-off: decrypted on-demand when opening a chat. |
| **Cache** | Sticker thumbs, profile thumbs, shared media previews, emoji table | Plaintext, without exception | Public or trivially re-derivable; encrypting it wastes cycles. |

### Hard rules

1. **Critical is NEVER opt-out**. Even if the user declines the Local Passcode, the auth keys are still encrypted with DPAPI.
2. **Sensitive is NEVER in logs**. `server_salt` may seem innocuous, but it pinpoints sessions and lets an adversary validate matches between logs and a network capture.
3. **Cache NEVER contains message text**. The line between a "public thumb" and a "message preview" is drawn by the caller; the storage rejects `MessageBlob` types in the cache bucket.

---

## 3. Key isolation rule

Vianigram applies a hard architectural invariant: **critical cryptographic material does not cross the WinMD ABI as a managed `byte[]`**.

### Why

- The .NET GC can move (compact) a `byte[]`. Even if the code zeroizes `array[i] = 0` at every index, a copy of the original value may have survived in another region of the heap if there was movement during the operation.
- A `byte[]` projected to WinRT crosses a marshalling boundary that copies bytes. The copy is never zeroized.
- A crash dump (Watson) captured by Windows would include the managed heap.

### How

- `Core/Vianium.Core.Crypto/src/keys/secret_key_handle.{h,cpp}` defines `SecretKeyHandle`, a native class that:
  - Allocates bytes in a `VirtualAlloc(MEM_COMMIT | MEM_RESERVE)` arena with `VirtualLock` when the OS allows it.
  - Zeroizes with `RtlSecureZeroMemory` (not optimizable away) in its destructor.
  - Exposes only an `IBuffer` projected via `CryptographicBuffer.CreateFromByteArray` over the pinned arena (no copy).
- The managed code calls by handle: `IAuthKeyStore.GetAuthKeyHandle(int dc)` returns an `IBuffer` whose underlying storage is the native arena. Cryptographic operations that require the bytes (e.g. derivation of `aes_key` for a message) occur entirely in C++.
- Auth keys are loaded **once per session** from DataProtectionProvider into the native arena. The intermediate managed `byte[]` (the output of `DataProtectionProvider.UnprotectAsync`) is explicitly zeroized and freed before the next subsequent await.

### Exceptions

- The `passcode_hash` that the user types is handled as a managed `byte[]` during the SRP handshake (§4 of `mtproto-policy.md`). It is zeroized immediately after `M1` is computed and never persists.
- Unit tests that validate known vectors may use a managed `byte[]` for deterministic input. Marked with `[TestOnly]` and with no code path in release builds.

---

## 4. Local Passcode mode

When the user enables the Local Passcode, Vianigram extends the DPAPI protection with a **master key derived from the passcode**. This encrypts the **Personal** and **Bulk** classes that would otherwise be in plaintext.

### 4.1 Master key derivation

```
device_salt = persistent random 32-byte value, generated at first launch, stored in LocalSettings (DPAPI-protected)
master_key  = PBKDF2-HMAC-SHA512(passcode_utf8, device_salt, iter=100000, dklen=64)
```

`master_key` (64 bytes) is kept **only in RAM** (a native arena, zeroized on lock). A device restart or auto-lock requires re-typing the passcode.

### 4.2 Data key derivation

Vianigram uses a key hierarchy to avoid a passcode change requiring re-encryption of the entire storage:

```
master_key                      // PBKDF2 output, ephemeral
  ├── kek_personal              // = HKDF-SHA256(master_key, "vianigram.kek.personal")
  │     └── data_key_personal   // = AES-256-GCM-decrypt(stored_personal_blob, kek_personal)
  └── kek_bulk                  // = HKDF-SHA256(master_key, "vianigram.kek.bulk")
        └── data_key_bulk       // = AES-256-GCM-decrypt(stored_bulk_blob, kek_bulk)
```

`data_key_personal` and `data_key_bulk` are generated randomly when the Local Passcode is enabled and persist encrypted with the corresponding KEK. **Changing the passcode** re-encrypts only the two KEK blobs; the data keys do not change.

> **Note**: HKDF-SHA256 is not available natively on WP8.1. Vianigram implements it clean-room in `Core/Vianium.Core.Crypto/src/crypto/hkdf.{h,cpp}` with self-tests vs RFC 5869 §A.1/A.2/A.3 at startup.

### 4.3 Lock screen

- The passcode screen appears at **launch** and after a configurable timeout: **1 min, 5 min, 15 min, 1 hour, 1 day, never**.
- On lock: `master_key`, `data_key_personal`, `data_key_bulk` zeroized from their arena. The UI empties the dialog list (encrypted).
- Auto-lock also triggers when the app goes to background if the timeout is ≤ 5 min, to prevent shoulder-surfing in the task switcher.

### 4.4 Failed unlock backoff

An exponential table in `Core/Vianium.Core.Crypto/src/passcode/lockout.cpp`:

| Failed attempt | Wait before the next input |
|-----------------|--------------------------------|
| 1               | 0 s |
| 2               | 1 s |
| 3               | 2 s |
| 4               | 4 s |
| 5               | 8 s |
| 6               | 16 s |
| 7               | 32 s |
| 8               | 60 s |
| 9               | 60 s |
| 10              | **Optional auto-logout** |

After the 10th attempt, if the user enabled "wipe-on-failure" at setup, Vianigram executes:

1. Logout of every DC (best-effort RPC `auth.logOut`; ignore failures).
2. Deletion of the entire `LocalSettings`.
3. Recursive deletion of `LocalFolder/secrets.bin`, `storage.db`, `media/**`.
4. `master_key`, `data_key_*` zeroized.

The unlock attempt count survives a restart (the counter is persisted in LocalSettings encrypted with direct DPAPI).

---

## 5. Storage layout

### 5.1 `LocalSettings` (small KV)

`Windows.Storage.ApplicationData.LocalSettings` holds small key-value pairs (≤ 8 KB each). Vianigram uses it for metadata encrypted with DPAPI directly.

| Key                        | Class      | Encryption                      | Typical size |
|----------------------------|------------|---------------------------------|---------------|
| `auth.dc{1..5}.key`        | Critical   | DPAPI(`LOCAL=user`)             | 256 B         |
| `auth.dc{1..5}.id`         | Sensitive  | Plaintext (it is a fingerprint) | 8 B           |
| `session.dc{N}.id`         | Sensitive  | DPAPI                           | 8 B           |
| `session.dc{N}.salt.cur`   | Sensitive  | DPAPI                           | 8 B           |
| `session.dc{N}.last_msgid` | Sensitive  | DPAPI                           | 8 B           |
| `passcode.salt`            | Critical   | DPAPI                           | 32 B          |
| `passcode.lockout_count`   | Sensitive  | DPAPI                           | 4 B           |
| `passcode.lockout_until`   | Sensitive  | DPAPI                           | 8 B (FILETIME)|

### 5.2 `LocalFolder/secrets.bin`

A master blob that contains the **secret chat keys** (of which there can be many: one per active E2E chat). Structure:

```
[ magic     "VGSCKEY1"             8 B   ]
[ version   uint32                  4 B   ]
[ count     uint32                  4 B   ]
[ records   variable                       ]
   per-record:
     [ chat_id     int64           8 B  ]
     [ peer_id     int64           8 B  ]
     [ created_at  int64 unix      8 B  ]
     [ key_blob    DPAPI(256 B)    ~280 B encrypted overhead ]
     [ fingerprint sha1[16]       16 B  ]
[ hmac      sha256(file)          32 B   ]
```

**Atomic write**: `secrets.bin.tmp` is written completely, flushed + closed, and renamed to `secrets.bin` (Win32 `MoveFileEx` with `MOVEFILE_REPLACE_EXISTING`). A crash mid-write leaves the orphaned .tmp, which the next startup detects and discards.

### 5.3 `LocalFolder/storage.db` (SQLite)

The main database: dialogs, messages, contacts, peers. Vanilla SQLite (without SQLCipher — not shippable under the WP8.1 package model without pre-compilation).

**Opt-in per-column encryption** when the Local Passcode is enabled:

- The columns `messages.body`, `messages.media_caption`, `dialogs.draft` encrypted with AES-256-GCM under `data_key_bulk`.
- Nonce: 96-bit, monotonic per-table (`tablename || rowid || version_counter`). The counter is persisted in `LocalSettings` to guarantee no reuse across restarts.
- AAD: `tablename || rowid` for domain separation.
- Tag: 128 bits appended to the ciphertext.

Implementation in `Core/Vianigram.Storage/src/encrypted_column.cpp`. AES-GCM via `BCryptEncrypt` with `BCRYPT_AES_GCM_ALG_HANDLE`.

**Without the Local Passcode**: the columns are stored plaintext. The DB itself is **not** encrypted at the file level (the package container offers package isolation, not block encryption).

### 5.4 `LocalFolder/media/`

Downloaded media (photos, videos, voice notes). Filenames are `sha256(file_reference)[..16]` to avoid leaking metadata via the name.

- Without the Local Passcode: plaintext on disk.
- With the Local Passcode: each file is encrypted with AES-256-GCM under `data_key_bulk`, the nonce derived from the filename and a per-file counter. When opening media, Vianigram decrypts to an arena in RAM (not to a temporary file).

---

## 6. Key rotation / device migration

### 6.1 Migration to a new device

**Not supported in v1**. Rationale:

- Telegram treats each `auth_key` as bound to a device. Transferring it breaks the active-session detection model.
- To preserve the E2E Secret Chat keys it would be necessary to re-encrypt against the new device's DPAPI, requiring an out-of-band trust handshake that does not provide value proportional to the risk.

Supported flow: the user does **`auth.logOut` from the old device**, a new sign-in on the new device, regenerating all the auth keys. The old Secret Chats become inaccessible (a lost key is by design in E2E without a backup).

### 6.2 Post-compromise rotation

Triggered by:

- A suspected compromise reported by the user.
- An `auth_key_unregistered` (-401) response from the server with no explainable reason.
- 10 failed Local Passcode attempts with wipe-on-failure active.

A **full reset** action:

1. RPC `auth.logOut` for each DC.
2. Complete wipe of `LocalFolder/**` (including the media cache).
3. Complete wipe of `LocalSettings`.
4. A re-launch of the app forces an onboarding from-scratch (phone number → SMS code → auth key generation).

### 6.3 Silent rotation

Vianigram does **NOT** rotate auth keys preventively. One live auth key per DC for the lifetime of the user on that device is by MTProto design. Rotating implies a full re-handshake + re-establishment of session state, which does not buy forward secrecy (it is already provided by the rotation of `server_salt` and the Secret Chat rekeys).

---

## 7. Smoke tests

`Clients/Vianigram.SmokeTests/Tests/`:

| Test | Coverage | Source |
|------|-----------|--------|
| `AtRestEncryptionTest` | Writes `auth.dc2.key` with DPAPI, simulates a restart (rebuilds the `IAuthKeyStore` with a new `LocalFolder` mount), decrypts and compares. Also verifies that the ciphertext blob does not contain the plaintext in a substring search. | `AtRestEncryptionTest.cs` |
| `PasscodeUnlockTest` | Happy path (unlock with the correct passcode). Lockout path (10 consecutive failures trigger a wipe). Passcode change (re-encrypts only the KEK blob, not the data keys). | `PasscodeUnlockTest.cs` |
| `EncryptedColumnRoundtripTest` | Writes 1000 messages with random bodies, reads them back. Asserts nonce-uniqueness by counting reuse in the counter. Tampering with the tag ⇒ an exception. | `EncryptedColumnRoundtripTest.cs` |
| `KeyHandleZeroizationTest` | Creates a `SecretKeyHandle`, writes a known pattern, forces the destructor, reads the region via an alias native pointer (debug-only) to verify it is zeroized. | `KeyHandleZeroizationTest.cs` |
| `HkdfRfc5869VectorTest` | The §A.1, §A.2, §A.3 vectors of RFC 5869. | `HkdfRfc5869VectorTest.cs` |
| `Pbkdf2Sha512VectorTest` | RFC 7914 §11 vectors + RFC 6070 (adapted to SHA-512). A short 100k iter test + a long 1M iter test (skipped in CI for time). | `Pbkdf2Sha512VectorTest.cs` |
| `SecretsBlobAtomicWriteTest` | Injects a crash mid-write (a file handle drop before the rename); the next startup sees only the old .bin and discards the .tmp. | `SecretsBlobAtomicWriteTest.cs` |

Any failure is **release-blocking** because it concerns the confidentiality of Critical-class material.

---

## 8. Threat model

### Defended

| Threat | Defense |
|---------|---------|
| Theft of a powered-off device | Auth keys + secret chat keys + passcode_hash encrypted with DPAPI; on SoCs with a TPM the master key is not even in flash. |
| Theft of an unlocked device but with a Local Passcode set | The Personal and Bulk classes remain encrypted with a `master_key` absent from RAM. The attacker sees only an empty dialog list. |
| Forensic recovery of unallocated sectors | RAII zeroization of `SecretKeyHandle` erases the bytes before release. DPAPI overwrites the slot when re-encrypting. |
| Backup of the package via USB-C | DPAPI scope `LOCAL=user` + TPM binding makes the blob un-decryptable on another device. |
| Brute-force of the Local Passcode | PBKDF2-SHA512 100k iter (~250ms on a device with TPM 2.0) + exponential lockout + optional wipe-on-failure. |
| Cross-app data leak | DPAPI scope `LOCAL=user` (NOT `LOCAL=machine`); the package container isolates LocalFolder. |
| Logs leaking keys | `LoggingPolicy.RedactCriticalKeys` blocks any log that matches `auth_key|secret_key|master_key|passcode|m1|m2|server_salt` before the formatter. |

### NOT defended

- **A rooted/jailbroken device with active malware**: kernel access defeats DPAPI and pinned arenas. Vianigram does not detect jailbreak (there is no reliable API on WP8.1) and assumes no additional trust if it detects it.
- **A voluntarily shared passcode**: social engineering, a photo of the passcode in another context, etc. The unlock policy assumes the passcode is the user's secret.
- **An adversary with the cooperation of the Telegram operator**: the operator can read cloud messages (not E2E), independent of any at-rest protection. Only Secret Chats are out of their reach.
- **Cold boot attack**: arenas with `VirtualLock` can be read if the attacker extracts the RAM while the app is unlocked. WP8.1 devices have no hardware-level cold-boot mitigation.
- **Side-channel timing/power**: Vianigram's constant-time primitives (`constant_time_eq`, AES bcrypt) protect against software timing, not against power analysis with physical probes.

---

## 9. Cross-references

- `tls-policy.md` §6 — certificate validation and SPKI pinning of the transport.
- `mtproto-policy.md` §1.4 — persistence of `auth_key` per DC.
- `mtproto-policy.md` §3.5 — the `msg_key` mismatch invariant.
- `mtproto-policy.md` §5.4 — zeroization of Secret Chat keys post-rekey (the RAII guard that shares `SecretKeyHandle` with §3 of this document).
- `principles.md` — the ban-list of prohibited crypto APIs (custom AES, XOR, etc.).

---

## 10. Change log

| Date       | Change |
|------------|--------|
| 2026-04-27 | Initial policy. DataProtectionProvider scope=`LOCAL=user` for Critical and Sensitive, an opt-in passcode mode with PBKDF2-SHA512 100k iter + a KEK/DEK hierarchy + AES-256-GCM column-level encryption in SQLite. `SecretKeyHandle` RAII for deterministic zeroization of keys in native arenas. Exponential lockout + an optional wipe-on-failure after 10 failures. Atomic write of `secrets.bin` via .tmp + rename. 7 smoke tests block a release on any regression. |
