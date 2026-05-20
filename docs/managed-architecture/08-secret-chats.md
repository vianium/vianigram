# Vianigram.SecretChats — Secret Chats Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md), [01-account.md](01-account.md), [03-messages.md](03-messages.md), [05-media.md](05-media.md), and the `security/` companion docs. DDD + hex + managed Kernel + standard C# patterns. Kernel concepts come from `Vianigram.Kernel`.
>
> **Native cross-link:** **all** key material and AES-256-IGE encrypt/decrypt operations live in the sibling `vianium-crypto`. Managed code in this context **never** holds key bytes — only opaque handles. The DH negotiation, the key fingerprint, the rekey pipeline, and the message MAC computations are native-side. Same DDD+hex skeleton, different technology, *especially strict isolation*.
>
> **Roadmap position:** Phase 1.8 — last managed bounded context. Depends on Account, Sync, and Media. We extract it last because (a) it is the smallest in audience but the strictest in security invariants, and (b) it requires every other primitive (DH, AES-IGE, Sync's `qts` channel, Media's encrypted-file upload) to already be available.

---

## 1. Purpose

`Vianigram.SecretChats` owns **end-to-end encrypted 1:1 conversations** with another Telegram user, separate from regular cloud chats. Each secret session has its own DH-negotiated symmetric key (perfect forward secrecy on rekey), runs over Telegram's `messages.sendEncrypted*` family, and is tied to a single device pair — secret messages are **not** synced across the user's devices. The context owns the session aggregate, the rekey lifecycle, the key-fingerprint validation flow, message ordering on the encrypted box, and the storage of encrypted history (which is local-only, never leaves the device).

The hard line: **the security invariant is "key material isolation".** All key bytes live in the sibling `vianium-crypto`. Managed code, including this context, sees only opaque integer handles. Every adapter that crosses the boundary must take/return handles, never `byte[]`. Tests verify this by checking that the managed binary contains no `byte[]` field on any type whose name contains "Key" or "Secret".

---

## 2. Aggregate root

`SecretSession` — single root per encrypted chat, identified by `SecretChatId(int)` (server-assigned during DH). Invariants:

- State machine: `Requested → AwaitingAccept → DhExchange → Active → Discarded`. Transitions match Telegram's `encryptedChat*` TL constructors.
- A session's `rootKeyHandle` (an opaque index into the crypto vault) exists if and only if state is `Active` or `RekeyPending`.
- `keyFingerprint` (8 bytes derived from the root key) is computed once and never changes for the *lifetime of a key generation*; it changes after a rekey.
- `layer` ∈ `{8, 17, 23, 46, 73, 101, 144, …}` per Telegram's secret-chat schema layers; clients negotiate the highest mutually supported layer.
- A session has `outSeq` and `inSeq` integer counters used for replay protection. `outSeq` is monotonically incremented on every send; `inSeq` enforces "no skipped or duplicated message ID".
- After 100 messages or 1 week of session age (whichever comes first), the aggregate transitions to `RekeyPending` and emits `RekeyRequired`. Rekey completes via a sub-flow before any further sends.
- Self-destruct timers: a per-session `ttl: Maybe<TimeSpan>` and per-message `selfDestructAfter: Maybe<TimeSpan>`; messages older than `ttl` are evicted from local storage automatically.
- Discarding (locally or by peer) is irreversible; the root key is wiped from the crypto vault, and all encrypted history for that session is deleted from disk.

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `SecretSession` | aggregate | One end-to-end session. |
| `SecretMessage` | entity | One encrypted message; identity = `(SecretChatId, randomId)`. |
| `RekeyAttempt` | entity | Bookkeeping for a rekey-in-progress: `gA`, `gB` handles, server `exchangeId`. |
| `SecretChatId` | VO struct | `int`, server-assigned. |
| `SecretSessionState` | VO enum | `Requested`, `AwaitingAccept`, `DhExchange`, `Active`, `RekeyPending`, `Discarded`. |
| `LayerVersion` | VO struct | `int`; secret-chat schema layer. |
| `KeyFingerprint` | VO struct | 8 bytes; rendered as visual emoji block + scannable QR. |
| `RootKeyHandle` | VO struct | Opaque `int32` index into the sibling `vianium-crypto`. |
| `EphemeralKeyHandle` | VO struct | Same idea, used during DH. |
| `RandomId` | VO struct | `long` — local message ID before server delivers. |
| `OutSeq` / `InSeq` | VO struct | Replay-protection counters. |
| `SelfDestructTimer` | VO record | `(activatedAt, durationSec)`. |
| `SecretMessageContent` | VO union | `Text` | `Photo` | `Video` | `Voice` | `Document` | `Sticker` | `Service`. |
| `EncryptedFileRef` | VO record | `(id, accessHash, dcId, size, keyFingerprint)` — points into Media's encrypted-upload store. |
| `SecretSessionSnapshot` | VO sealed | Outbound projection. **Never** carries any key handles externally; only fingerprints + state + counters. |

---

## 4. Domain events emitted

| Event | When | Payload |
|---|---|---|
| `SecretChatRequested` | Local user initiated | `SecretSessionSnapshot`, `UserId peer` |
| `SecretChatIncoming` | Sync delivered `encryptedChatRequested` | `SecretSessionSnapshot`, `UserId fromUser` |
| `SecretChatAccepted` | Local user accepted, DH complete | `SecretChatId`, `KeyFingerprint` |
| `SecretChatActive` | Negotiation finished, key fingerprint computed | `SecretChatId`, `KeyFingerprint`, `LayerVersion` |
| `SecretChatDiscarded` | Either side discarded | `SecretChatId`, `DiscardReason` |
| `RekeyRequired` | Threshold (messages or time) reached | `SecretChatId` |
| `RekeyCompleted` | Rekey finished; new fingerprint | `SecretChatId`, `KeyFingerprint previous`, `KeyFingerprint current` |
| `SecretMessageSent` | Local send acked by server | `SecretChatId`, `RandomId`, `MessageId` |
| `SecretMessageReceived` | Decrypted inbound | `SecretChatId`, `SecretMessageSnapshot` |
| `SecretMessageReadByPeer` | Peer reported read | `SecretChatId`, `OutSeq through` |
| `SecretMessageSelfDestructed` | Local TTL expired | `SecretChatId`, `RandomId` |
| `KeyFingerprintMismatchSuspected` | Replay/MITM heuristic fired | `SecretChatId`, `MismatchKind` |

---

## 5. Inbound ports

```csharp
namespace Vianigram.SecretChats.Ports.Inbound
{
    public interface ISecretChatsApi
    {
        Task<Result<SecretSessionSnapshot, Error>> RequestAsync(UserId peer);
        Task<Result<Unit, Error>> AcceptAsync(SecretChatId id);
        Task<Result<Unit, Error>> DiscardAsync(SecretChatId id, DiscardReason reason);
        Task<Result<SecretMessageSnapshot, Error>> SendTextAsync(SecretChatId id, string text, Maybe<TimeSpan> selfDestruct);
        Task<Result<SecretMessageSnapshot, Error>> SendMediaAsync(SecretChatId id, EncryptedFileRef file, SecretMessageContent envelope, Maybe<TimeSpan> selfDestruct);
        Task<Result<Unit, Error>> SetSessionTtlAsync(SecretChatId id, Maybe<TimeSpan> ttl);
        Task<Result<Unit, Error>> ReadHistoryAsync(SecretChatId id, OutSeq through);
        Task<Result<Unit, Error>> RequestRekeyAsync(SecretChatId id);
        Maybe<SecretSessionSnapshot> GetSession(SecretChatId id);
        IReadOnlyList<SecretSessionSnapshot> ListActiveSessions();
        Task<Result<IReadOnlyList<SecretMessageSnapshot>, Error>> GetHistoryAsync(SecretChatId id, int limit, Maybe<RandomId> after);
        KeyFingerprintRendering RenderFingerprint(KeyFingerprint fp);   // emoji + QR
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }
}
```

`KeyFingerprintRendering` is `(IReadOnlyList<string> emojis, byte[] qrPng)` — the QR PNG is precomputed once per fingerprint and cached.

---

## 6. Outbound ports

| Port | Purpose | Adapter |
|---|---|---|
| `IEncryptedSessionStore` | Persist `SecretSession` aggregates **encrypted at rest** via `DataProtectionProvider`. Schema is opaque to caller. | `Infrastructure/Persistence/EncryptedSecretSessionStore.cs` |
| `IEncryptedMessageStore` | Persist decrypted message bodies *also encrypted at rest* (defense in depth). | `Infrastructure/Persistence/EncryptedSecretMessageStore.cs` |
| `ISecretCryptoVault` | The full set of native crypto operations: DH `g^a`/`g^b`/shared key, AES-256-IGE encrypt/decrypt, MAC compute, key fingerprint compute, key-rotation. **All inputs and outputs are handles or short non-secret hashes.** | `Infrastructure/Crypto/SecretCryptoAdapter.cs` |
| `IAuthorizedInvoker` | from Account; for `messages.sendEncrypted*`, `messages.requestEncryption`, etc. | injected |
| `IRawSecretUpdatesSource` | Subset of Sync's raw stream filtered to `updateNewEncryptedMessage`, `updateEncryption`, `updateEncryptedChatTyping`, `updateEncryptedMessagesRead`. | injected from Sync |
| `IEncryptedMediaPort` | Media's encrypted-file capability: `messages.uploadEncryptedFile`. The adapter wraps `IMediaApi` with key-aware upload. | injected from Media |
| `IClock`, `ILogger`, `ITelemetry` | Kernel | injected |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | Description |
|---|---|---|
| `RequestEncryptionCommand(peer)` | `RequestEncryptionUseCase` | Generate DH `g^a`, call `messages.requestEncryption`. State: Idle → Requested. |
| `AcceptEncryptionCommand(id)` | `AcceptEncryptionUseCase` | On incoming, generate `g^b`, compute shared key, call `messages.acceptEncryption`. |
| `ConfirmEncryptionCommand(id, gA)` | `ConfirmEncryptionUseCase` | Peer responded; finalize shared key, emit `SecretChatActive` with fingerprint. |
| `DiscardEncryptionCommand(id, reason)` | `DiscardEncryptionUseCase` | `messages.discardEncryption`; wipe key, wipe local storage. |
| `SendEncryptedTextCommand(id, text, ttl)` | `SendEncryptedTextUseCase` | Build TL envelope at the negotiated `layer`, encrypt, `messages.sendEncrypted`. |
| `SendEncryptedMediaCommand(id, file, envelope, ttl)` | `SendEncryptedMediaUseCase` | Coordinates with Media: file is uploaded encrypted, then envelope referencing it is encrypted+sent. |
| `SendEncryptedServiceCommand(id, service)` | `SendEncryptedServiceUseCase` | `messages.sendEncryptedService` for read receipts, typing, screenshot notifications, layer changes. |
| `SetSessionTtlCommand(id, ttl)` | `SetSessionTtlUseCase` | Service message of kind `decryptedMessageActionSetMessageTTL`. |
| `ReadEncryptedHistoryCommand(id, through)` | `ReadEncryptedHistoryUseCase` | `messages.readEncryptedHistory` + service message. |
| `RequestRekeyCommand(id)` | `RequestRekeyUseCase` | DH-Inner re-negotiation. Service messages of kind `decryptedMessageActionRequestKey/AcceptKey/CommitKey`. |
| `ApplyIncomingMessageCommand(rawCipher)` | `ApplyIncomingMessageUseCase` | Hot path: decrypt, verify MAC, verify `inSeq`, deserialize TL, store, emit `SecretMessageReceived`. |
| `WipeSessionCommand(id)` | `WipeSessionUseCase` | After discard or self-destruct: drop SQLite rows, delete blobs, free crypto handle. |

**Queries**

| Query | Returns |
|---|---|
| `ListActiveSessionsQuery` | `IReadOnlyList<SecretSessionSnapshot>` |
| `GetSessionQuery(id)` | `Maybe<SecretSessionSnapshot>` |
| `GetEncryptedHistoryQuery(id, limit, after)` | `IReadOnlyList<SecretMessageSnapshot>` |
| `RenderFingerprintQuery(fp)` | `KeyFingerprintRendering` |

**Reactive subscribers**

- `SyncSecretReactor` — listens to `Vianigram.Sync.EncryptedMessageReceived` and routes to `ApplyIncomingMessageUseCase`.
- `RekeyScheduler` — periodic check (every 1 min wall-clock) on each `Active` session, fires `RequestRekeyCommand` when threshold met.
- `SelfDestructSweeper` — single timer-driven sweep, evicting expired messages and emitting `SecretMessageSelfDestructed`.
- `AccountReactor` — login bind / logout wipe.

---

## 8. Cross-context interactions

**Emits**

- `SecretMessageReceived/Sent` → consumed by Presentation only. We deliberately **do not** emit into `Vianigram.Messages` — secret chats have their own UI surface and a different storage model.
- `SecretChatActive`/`SecretChatDiscarded` → consumed by Presentation; also by `Vianigram.Chats` to add/remove a "secret" virtual entry in the dialog list.

**Consumes**

- From `Vianigram.Sync`: `EncryptedMessageReceived`, `EncryptedChatStateChanged`, `EncryptedChatTyping`, `EncryptedHistoryRead`. The sole inbound channel.
- From `Vianigram.Account`: `LoginCompleted`, `ActiveAccountChanged`, `AccountLoggedOut` (full wipe of secret stores for that account), `AuthKeyRotated` (does *not* invalidate secret session keys; they are independent of the auth_key, so we keep going).
- From `Vianigram.Contacts`: `IUserResolver` to render peer name and photo.
- From `Vianigram.Media`: `IEncryptedMediaPort` for encrypted file upload/download. The Media context provides a separate codepath that takes a key handle and keeps bytes encrypted at rest.

**Capabilities:** `secretchats.enable` (default on; off disables the entire feature including UI surfacing), `secretchats.screenshot_notify` (off by default; sends a service message when a screenshot is detected — implementation depends on platform support).

---

## 9. Storage strategy

**Encrypted at rest (`DataProtectionProvider`, `LOCAL=user`):**

- All SQLite databases for SecretChats are stored encrypted. Two databases per `AccountId`:
  - `{LocalFolder}/accounts/{accountId}/secret_sessions.dat` — encrypted blob containing session metadata.
  - `{LocalFolder}/accounts/{accountId}/secret_messages.dat` — encrypted blob of message history per session.
- Storage adapter writes ciphertext only. Plaintext SQLite file format never touches disk.
- Decrypted form is held in memory inside the adapter and accessed only by the application layer.

**Memory-only (per process):**

- Active `SecretSession` aggregates with their `RootKeyHandle`s.
- Replay-protection ring buffers (last 100 `inSeq` values per session, used to detect duplicates beyond mere counter check).

**Native crypto vault (per-process, never persisted as plaintext):**

- Root keys, ephemeral DH keys, MAC keys, IGE state.
- Persistence: the vault writes its own ciphertext blob (`crypto_vault.dat`), key-encrypting itself with a master key derived from `DataProtectionProvider`. Managed code never sees bytes; only handles. On startup, the vault rehydrates from its own ciphertext.

**Logout semantics:**

1. Emit `SecretChatDiscarded` for every active session (reason: `LocalLogout`).
2. Tell the vault to drop all keys for that `AccountId`.
3. Delete `secret_sessions.dat` and `secret_messages.dat`.
4. Optionally: server-side `messages.discardEncryption` for each session (best-effort; it's already gone locally and that's the security guarantee).

**Self-destruct semantics:**

- Per-session TTL: messages older than `ttl` are evicted by `SelfDestructSweeper` and emit `SecretMessageSelfDestructed`. UI is expected to remove from view.
- Per-message timer: triggered when peer reads (or, for outgoing, when *we* read). `SelfDestructTimer.activatedAt` is committed transactionally with the read event.
- Eviction is unrecoverable: SQLite row deleted, encrypted blob page rewritten.

---

## 10. Performance considerations

- **DH handshake:** PQ-Inner (2048-bit modular exponentiation × 2) takes ~600 ms on a 512 MB ARMv7 device. Done in the sibling `vianium-crypto` off the UI thread; managed code awaits a single async call.
- **Encrypt/decrypt cost:** AES-256-IGE on a 1 KB message takes ~0.5 ms in native. Negligible on the message hot path.
- **Rekey threshold:** 100 messages OR 7 days, whichever first. Rationale: bounds the impact of any future cryptanalytic advance against the current key generation. Rekey itself takes ~700 ms (DH again) and is invisible to the user — sends are queued, the rekey runs, then sends drain.
- **Send latency:** target ≤ 50 ms from user tap to optimistic UI update; ≤ 500 ms to server ack on Wi-Fi. The encryption step inside the use case is small; most cost is network + the optimistic UI emit.
- **Storage write cost:** each message is two writes — one to the encrypted SQLite blob, one for replay-protection ring buffer update. We coalesce into a single transaction.
- **Memory:** secret sessions hold ~2 KB each in managed memory (counters, snapshots, a few handles); large message histories live on disk, paged in 50 at a time.
- **Self-destruct sweeper:** runs every 60 s; O(active sessions) cost. Per-message timers are not individual `Timer` instances — they're a single ordered min-heap of `(expiresAt, sessionId, randomId)`.
- **Crypto-vault calls:** each call crosses the managed/native boundary. We batch where possible (e.g., decrypt + verify-MAC + parse in one native call) to avoid round-trip overhead. Round-trip is ~10 µs but adds up in inboxes with backlog.

---

## 11. Telegram MTProto methods used

| Method | Purpose |
|---|---|
| `messages.requestEncryption` | Initiate a secret chat. Sends our `g^a`. |
| `messages.acceptEncryption` | Peer accepts; sends `g^b`, finalizes key. |
| `messages.discardEncryption` | Discard a secret chat (server side wipes references). Idempotent. |
| `messages.sendEncrypted` | Send an encrypted text/media message envelope. |
| `messages.sendEncryptedFile` | Send an encrypted message that references an encrypted file. |
| `messages.sendEncryptedService` | Send an encrypted *service* message (typing, read receipts, layer change, rekey actions, TTL change, screenshot notification). |
| `messages.uploadEncryptedFile` | Upload an encrypted file (called via Media's `IEncryptedMediaPort`). |
| `messages.readEncryptedHistory` | Mark messages read up to a sequence number. |
| `messages.receivedQueue` | Reconcile our `inSeq` cursor with the server's view. |
| `messages.getDhConfig` | Fetch DH parameters (`p`, `g`, `version`) periodically. |

> Inbound updates (consumed via Sync, not RPC'd here): `updateNewEncryptedMessage`, `updateEncryption`, `updateEncryptedChatTyping`, `updateEncryptedMessagesRead`.

---

## 12. Open questions / future work

1. **Multi-device secret chats.** Telegram does not support cross-device sync of secret chats by design. Our model honors that. Future Telegram protocol changes may relax this; if so, we'd need to introduce a `SecretSessionDevicePair` entity and reshape the aggregate. Not anticipated soon.
2. **Sticker support in secret chats.** Stickers in secret chats arrive as encrypted documents; rendering reuses sticker decoding from `Vianigram.Core.Media` but the asset is not cached in the public sticker LRU. Open: do we cache decoded frames per session? Probably not — defeats forward secrecy promises.
3. **Layer 144 + features.** We target layer 144 minimum for new sessions, with degradation to older layers if peer is older. The aggregate's `layer` field captures negotiation; specific layer-gated features (reactions in secret chats, custom emoji) are checked per-feature.
4. **Forward secrecy verification.** We rotate keys; we should also expose to the user a "verify keys" UX similar to Signal's safety-number compare. The fingerprint emoji rendering is already implemented; the QR scanning flow is open.
5. **Screenshot notifications.** WP8.1 does not provide a guaranteed screenshot-detection signal. Implementing "screenshot notify" robustly may require lock-screen tricks not appropriate for v1. Capability is gated off.
6. **Replay protection durability.** The 100-entry `inSeq` ring buffer per session is in-memory only by default — survives across restarts via the encrypted store, but if the user force-kills mid-write we may lose 100 entries' worth of replay protection. The MAC-based protocol still prevents forgeries; the ring is defense in depth.
7. **Key-material isolation enforcement.** A build-time analyzer enumerates `byte[]` fields in this assembly and fails if any are named *Key*, *Salt*, *Secret*, or *Iv*. Aggregates expose only handles. The CI rule is described in `security/key-material-isolation.md`.
8. **Reference notes from PivoraTelegram.** `SecretChat/SecretChatManager.cs`, `SecretChatRepository.cs`, `SecretChatEncryptor.cs`, `SelfDestructTimer.cs` are the closest analogues. They keep keys as `byte[]` fields on managed objects — exactly the anti-pattern we are leaving behind. We migrate the *flow* (state machine, service-message kinds, TTL semantics) and rebuild the *substrate* (handles instead of bytes, encrypted-store adapter, native crypto vault) from scratch.
9. **Telemetry.** `SecretChatsTelemetryEmitter` emits `secret.sessions.active`, `secret.messages.sent`, `secret.messages.received`, `secret.rekeys.completed`, histograms for `secret.dh.duration_ms` and `secret.encrypt.duration_us`, counter for `secret.fingerprint_mismatches_suspected` (must always be 0 in healthy operation).
