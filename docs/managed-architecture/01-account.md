# Vianigram.Account — Account Bounded Context

> **Required prior reading:** [principles.md](principles.md) and [00-overview.md](00-overview.md). This doc assumes DDD + hex + managed Kernel + the C# patterns (`Result<T, Error>`, `IEventBus`, `ICapabilityRegistry`, ViewModel-as-adapter). Kernel concepts (`IClock`, `ILogger`, `ITelemetry`, `IObjectStore<T>`, `SubscriptionToken`, `Maybe<T>`) are imported from `Vianigram.Kernel` and not redefined here.
>
> **Native cross-link:** this context owns the managed surface that wraps the sibling `vianium-crypto` (auth_key derivation, PQ-Inner, AES-IGE) and the sibling `vianium-mtproto` (Diffie–Hellman handshake, server salt, session ID). Same DDD+hex skeleton, different technology.
>
> **Roadmap position:** Phase 1.1 — first managed bounded context extracted. Account is the gate: nothing else in the app is meaningful without an authorized session, and the encrypted artifacts (auth_key, server_salt) it produces are referenced by every other context. We extract it first so that all subsequent contexts can depend on `IAccountApi` instead of fishing inside a god-class.

---

## 1. Purpose

`Vianigram.Account` owns everything related to the user's authenticated identity against Telegram's MTProto servers. It is the only context allowed to **mint, persist, rotate, or destroy** the long-lived `auth_key` material and the per-account session metadata (DC ID, user ID, server salt, session ID, layer). It implements the four canonical login paths — phone+SMS, password (SRP-2048), QR cross-device, and bot token — and exposes idempotent commands to add, switch between, and terminate sessions on this device or any other device authorized for the account.

Everything else in Vianigram (Chats, Messages, Sync, Calls, Media, SecretChats, Contacts) treats Account as a hard dependency: they receive an opaque `AccountHandle` and an `IAuthorizedInvoker` capability, never raw key bytes.

---

## 2. Aggregate root

`AccountIdentity` — single root that enforces:

- An identity is **either** `Anonymous`, `Pending(authStep)`, or `Authorized(userId, dcId)`. No other states.
- The `authKeyHandle` (opaque pointer into the sibling `vianium-crypto`) exists if and only if state is `Authorized` or `Pending(awaitingPassword)`.
- `dcId ∈ {1, 2, 3, 4, 5}` and is immutable for the lifetime of an authorized identity (DC migration creates a new key).
- A device may host multiple `AccountIdentity` aggregates, each with a distinct `(userId, dcId)` pair, but only one is `Active` at a time. Switching active identity is an explicit command.
- `serverSalt` and `sessionId` may rotate during the lifetime of a single authorized identity; they are entity-internal.
- Logout is irreversible for the local copy: it clears the `authKeyHandle` from the crypto context and emits `AccountLoggedOut` *before* clearing persisted bytes — the order matters for downstream cleanup.

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `AccountIdentity` | aggregate | Root entity; encapsulates auth state machine and key lifecycle. |
| `AuthSession` | entity | One in-flight login attempt (sendCode → signIn or checkPassword). Bound to the aggregate but tombstoned on success or after 15 minutes idle. |
| `LinkedDevice` | entity | A representation of one row from `account.getAuthorizations` — *not* the local identity, but a remote authorization record we can revoke. |
| `AccountId` | VO struct | `long` user_id wrapped, namespaced (`acct:1234567890`). |
| `DcId` | VO struct | `int` 1–5, validated. |
| `PhoneNumber` | VO struct | E.164 string, normalized (no spaces/dashes). |
| `PhoneCodeHash` | VO struct | Server-provided 8-byte token returned by `auth.sendCode`, opaque to us. |
| `AuthStep` | VO enum | `Idle`, `CodeSent`, `AwaitingCode`, `AwaitingPassword`, `AwaitingQrConfirm`, `Authorized`, `Failed`. |
| `LoginMethod` | VO enum | `Sms`, `Call`, `App`, `FlashCall`, `MissedCall`, `Email`, `Qr`, `BotToken`. |
| `AuthKeyHandle` | VO struct | Opaque `int32` index into the sibling `vianium-crypto`'s key vault. **Never** contains key bytes. |
| `ServerSalt` | VO struct | `long`, rotates every ~30 minutes per server hint. |
| `SessionId` | VO struct | `long` random, rotates on reconnect or DC migration. |
| `LayerVersion` | VO struct | TL layer used (e.g. 158). Bumps on app upgrade. |
| `QrLoginToken` | VO record | Bytes from `auth.exportLoginToken` plus expiry. |
| `Srp2048Challenge` | VO record | `{salt1, salt2, g, p, srp_id, srp_B}` from `account.getPassword`. |
| `TwoFactorHint` | VO struct | Server-provided plaintext password hint, capped at 256 chars. |
| `AccountSnapshot` | VO sealed class | Immutable projection. The only thing crossing the context boundary externally. |

---

## 4. Domain events emitted

| Event | When | Payload |
|---|---|---|
| `LoginRequested` | After `auth.sendCode` succeeds | `PhoneNumber`, `LoginMethod`, `PhoneCodeHash`, `DateTimeOffset sentAt` |
| `LoginCodeAccepted` | After `auth.signIn` returns `auth.authorization` | `AccountId`, `DcId`, `LayerVersion` |
| `LoginRequiresPassword` | When `signIn` returns `auth.authorizationSignUpRequired` or password challenge | `Srp2048Challenge`, `TwoFactorHint` |
| `LoginCompleted` | When the identity reaches `Authorized` | `AccountId`, `DcId`, `LoginMethod` |
| `LoginFailed` | Any terminal failure (FLOOD, PHONE_NUMBER_INVALID, PHONE_CODE_INVALID, PASSWORD_HASH_INVALID, SESSION_PASSWORD_NEEDED handled separately) | `Error`, `LoginMethod` |
| `QrTokenIssued` | After `auth.exportLoginToken` returns a token | `QrLoginToken`, `DateTimeOffset expiresAt` |
| `QrTokenAccepted` | After receiving an `updateLoginToken` push | `AccountId` |
| `ActiveAccountChanged` | When the user switches between multi-account identities | `AccountId? previous`, `AccountId current` |
| `AccountLoggedOut` | After `auth.logOut` returns or local-only logout completes | `AccountId`, `LogoutReason` |
| `LinkedDeviceRevoked` | After `account.resetAuthorization` succeeds for a remote device | `long hash` (the device hash, not auth_key) |
| `AuthKeyRotated` | After a DC migration triggers re-auth | `AccountId`, `DcId previous`, `DcId current` |

All events extend `DomainEventBase` (Kernel) and are immutable. They are published on the shared `IEventBus`, never directly to UI.

---

## 5. Inbound ports

```csharp
namespace Vianigram.Account.Ports.Inbound
{
    public interface IAccountApi
    {
        Task<Result<AccountSnapshot, Error>> StartPhoneLoginAsync(PhoneNumber phone, LoginMethod hint);
        Task<Result<AccountSnapshot, Error>> SubmitCodeAsync(AccountHandle pending, string code);
        Task<Result<AccountSnapshot, Error>> SubmitPasswordAsync(AccountHandle pending, string password);
        Task<Result<QrLoginPayload, Error>> StartQrLoginAsync();
        Task<Result<AccountSnapshot, Error>> ConfirmQrFromOtherDeviceAsync(byte[] tokenBytes);
        Task<Result<Unit, Error>> LogOutAsync(AccountHandle account, bool wipeLocalData);
        Task<Result<Unit, Error>> SetActiveAsync(AccountHandle account);
        Task<Result<IReadOnlyList<LinkedDeviceSnapshot>, Error>> ListLinkedDevicesAsync();
        Task<Result<Unit, Error>> RevokeLinkedDeviceAsync(long deviceHash);
        IReadOnlyList<AccountSnapshot> ListLocalAccounts();
        Maybe<AccountSnapshot> GetActive();
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }

    public interface IAuthorizedInvoker
    {
        Task<Result<TResponse, Error>> InvokeAsync<TResponse>(ITlRequest<TResponse> request, CancellationToken ct);
        AccountHandle Account { get; }
    }
}
```

`IAuthorizedInvoker` is the *capability* every other context receives. It binds requests to the active account's auth_key transparently and is the only legal way to issue MTProto calls that aren't part of the login flow itself.

---

## 6. Outbound ports

| Port | Purpose | Adapter lives in |
|---|---|---|
| `IEncryptedSessionStore` | Persist `AccountIdentity` snapshots, encrypted per-account via `DataProtectionProvider`. | `Infrastructure/Persistence` |
| `ICryptoVault` | Bridge to the sibling `vianium-crypto`. Methods take/return `AuthKeyHandle`, never key bytes. Implements PQ-Inner, DH, AES-IGE, SRP-2048. | `Infrastructure/Crypto` |
| `IMtprotoTransport` | Bridge to the sibling `vianium-mtproto`. Sends raw TL on a DC connection, returns raw TL replies. | `Infrastructure/Transport` |
| `ITlSerializer` | Encode/decode TL records. Pure function over byte buffers. | `Infrastructure/Tl` |
| `IDcResolver` | Look up DC endpoints from `help.getConfig` cache. | `Infrastructure/Dc` |
| `IDeviceInfoProvider` | Device name, app version, system version for `initConnection`. | `Infrastructure/Device` |
| `IPushChannel` | Subscribe to `updateLoginToken` for QR flow without going through Sync (Sync isn't running yet during login). | `Infrastructure/Push` |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | Result |
|---|---|---|
| `StartPhoneLoginCommand(phone, methodHint)` | `StartPhoneLoginUseCase` | `Result<AccountSnapshot pending, Error>` |
| `SubmitCodeCommand(handle, code)` | `SubmitCodeUseCase` | `Result<AccountSnapshot, Error>` |
| `SubmitPasswordCommand(handle, password)` | `SubmitPasswordUseCase` | `Result<AccountSnapshot, Error>` |
| `StartQrLoginCommand()` | `StartQrLoginUseCase` | `Result<QrLoginPayload, Error>` |
| `AcceptQrTokenCommand(bytes)` | `AcceptQrTokenUseCase` | `Result<AccountSnapshot, Error>` |
| `LogOutCommand(handle, wipeLocal)` | `LogOutUseCase` | `Result<Unit, Error>` |
| `SwitchActiveAccountCommand(handle)` | `SwitchActiveAccountUseCase` | `Result<Unit, Error>` |
| `RevokeLinkedDeviceCommand(hash)` | `RevokeLinkedDeviceUseCase` | `Result<Unit, Error>` |
| `MigrateDcCommand(handle, targetDc)` | `MigrateDcUseCase` (internal, triggered by `PHONE_MIGRATE_X`) | `Result<Unit, Error>` |

**Queries**

| Query | Returns |
|---|---|
| `ListLocalAccountsQuery` | `IReadOnlyList<AccountSnapshot>` |
| `GetActiveAccountQuery` | `Maybe<AccountSnapshot>` |
| `ListLinkedDevicesQuery` | `IReadOnlyList<LinkedDeviceSnapshot>` |
| `GetAuthStepQuery(handle)` | `AuthStep` |

The use-case layer never throws for protocol-level errors; it maps Telegram RPC errors (`PHONE_CODE_INVALID`, `SESSION_PASSWORD_NEEDED`, `FLOOD_WAIT_X`, `PHONE_MIGRATE_X`) into typed `Error` codes catalogued in `Domain/Errors/AccountErrors.cs`. `FLOOD_WAIT` is special-cased and surfaces an `Error.WithRetryAfter(seconds)` so the UI can show a countdown.

---

## 8. Cross-context interactions

**Emits (consumed by others)**

- `LoginCompleted` → consumed by `Vianigram.Sync` (start update loop), `Vianigram.Chats` (load dialog list), `Vianigram.Contacts` (kick off `contacts.getContacts`), `Vianigram.SecretChats` (load encrypted-chat history).
- `AccountLoggedOut` → consumed by every other context: each performs its own teardown of in-memory state and SQLite tables scoped to that `AccountId`. Order is enforced by the bus: Account is deliberately last to clear its own bytes, so consumers can still scope writes correctly.
- `ActiveAccountChanged` → consumed by `Vianigram.Sync` (swap cursor), `Vianigram.Chats` (swap dialog list), `Vianigram.Messages` (swap repo), `Vianigram.Media` (swap blob folder).
- `AuthKeyRotated` → consumed by `Vianigram.Sync` (must getDifference from scratch), `Vianigram.SecretChats` (key fingerprints unaffected, but keep root key map in sync).

**Consumes (from others)**

- `AppLifecycleSuspending` from Kernel → eagerly persist any in-flight `AuthSession` so the user can resume after a phone call interruption.
- `NetworkConnectivityChanged` from Kernel → defer login retries while offline; surface `Error.NetworkUnavailable`.

---

## 9. Storage strategy

**Persisted (encrypted via `DataProtectionProvider`, scope = `LOCAL=user`):**

- `auth_key` (256 bytes) — handed to the native crypto vault before encryption; the managed side only stores the `AuthKeyHandle` index plus the ciphertext blob that lets the vault rehydrate it on next launch.
- `server_salt` (8 bytes), `session_id` (8 bytes), `dc_id`, `user_id`, `layer`.
- `phone_number` (E.164) and `login_method` last used, for UI continuity.
- Last `pts/qts/seq/date` are *not* stored here — that belongs to `Vianigram.Sync`. Account only holds *who*, not *what they've seen*.

**Persisted (plaintext, `LocalSettings`):**

- `active_account_id` — which of the local accounts is currently selected.
- `accounts_index` — list of `AccountId`s plus a `dc_hint` per account.

**Memory-only:**

- Active `AuthSession` (current `phone_code_hash`, `srp_id`, `srp_B`) — wiped on `AppSuspending`. If the user dies mid-login, they re-enter the phone.
- The decrypted `auth_key` bytes — only ever exist inside the sibling `vianium-crypto`'s opaque vault. Managed code holds a handle.

**Encryption layout**

```
LocalFolder/accounts/{user_id}.session.dat   ← ciphertext blob (DataProtectionProvider)
LocalFolder/accounts/{user_id}.meta.json     ← non-secret: dc_id, layer, last_login_at
LocalSettings["vianigram.active_account"]    ← user_id of active account
```

Per-account isolation is enforced at the file-path level: contexts that need per-account stores (Messages, Media) namespace by `AccountId` to make logout-wipe trivially correct.

---

## 10. Performance considerations

- **Cold start path:** load `accounts_index` from `LocalSettings` (sync, cheap), pick active, kick off async decryption of the session blob. Target: < 80 ms wall-clock to having an `AuthorizedInvoker` ready on a 512 MB-class hardware.
- **DH handshake (only on first ever login on a DC):** ~3 RTTs. Pin the keepalive socket so the second leg doesn't reopen TCP.
- **SRP-2048 derivation:** PBKDF2 + 2048-bit modular exponentiation. Done in the sibling `vianium-crypto` off the UI thread; managed side awaits a single async call.
- **QR token expiry:** server tokens last 30 s. We refresh proactively at 25 s; if `auth.exportLoginToken` returns `loginTokenMigrateTo`, we follow the migration in-band rather than throwing back to UI.
- **Multi-account memory:** each inactive account keeps only its `AccountSnapshot` (≈ 200 bytes) in memory. Auth keys for inactive accounts are kept in the vault but their MTProto sockets are torn down.
- **Logout latency:** `auth.logOut` on poor networks may hang. We give it 4 s, then proceed with local wipe and let the server-side revocation race in the background. The user-visible state machine is local-truth.

---

## 11. Telegram MTProto methods used

| Method | Purpose | Idempotent? |
|---|---|---|
| `auth.sendCode` | Initiate phone login. | Server-side rate-limited; client treats retry as new attempt. |
| `auth.resendCode` | Request a new code via a different transport (SMS → call). | Yes within window. |
| `auth.signIn` | Submit code. | One-shot per `phone_code_hash`. |
| `auth.checkPassword` | SRP-2048 check after `SESSION_PASSWORD_NEEDED`. | One-shot per challenge. |
| `auth.exportLoginToken` | Issue a QR token to display. | Idempotent until expiry. |
| `auth.importLoginToken` | This-device side of QR (when scanning, not displaying). | One-shot per token. |
| `auth.acceptLoginToken` | Other-device confirmation (called from device hosting an existing session). | One-shot per token. |
| `auth.logOut` | Server-side revoke of this device's auth_key. | Yes (returns true even on second call). |
| `auth.bindTempAuthKey` | Bind a perfect-forward-secrecy temp key. Done lazily after first login. | Per temp key. |
| `auth.cancelCode` | Cancel a pending sendCode (back navigation in UI). | Yes. |
| `account.getPassword` | Fetch SRP challenge before `auth.checkPassword`. | Yes. |
| `account.updatePasswordSettings` | Change 2FA password (out of scope for v1, port stub exists). | Yes. |
| `account.getAuthorizations` | List remote sessions for the linked-devices screen. | Yes. |
| `account.resetAuthorization` | Revoke a specific remote session. | Yes per `hash`. |
| `account.getNotifySettings` (read-only initial fetch) | Used during post-login warmup. | Yes. |
| `help.getConfig` | DC list refresh on each cold-login. | Yes. |
| `help.getNearestDc` | Best-effort DC hint when user doesn't yet have one. | Yes. |

---

## 12. Open questions / future work

1. **Passkey / WebAuthn login.** Telegram's API doesn't expose this yet, but the aggregate is structured so adding a `LoginMethod.Passkey` variant won't disturb other states.
2. **Per-account proxy settings.** Today proxy is global (Kernel-level). When we add MTProxy per-account, `AccountIdentity` will gain a `proxy: Maybe<MtProxy>` field and the outbound `IMtprotoTransport` port will key on the active account.
3. **Hardware-backed key storage.** WP8.1 has no TEE we can trust uniformly across OEMs. Long-term, we want to fall through to `KeyCredentialManager` where present and seal the auth_key under a non-exportable key. Tracked in `security/notes-hsm.md`.
4. **2FA enrollment & change flows.** Currently we only consume the SRP challenge to log in. The full `account.updatePasswordSettings` flow (set, change, recover via email) is deferred to Phase 2. The aggregate already has the `Srp2048Challenge` value object, so the application-layer additions are localized.
5. **Multi-account UI cap.** We cap at 4 simultaneous accounts. The cap is an `AccountLimitsPolicy` constant, not a hard invariant — the aggregate itself has no opinion on how many siblings exist.
6. **Reference notes from PivoraTelegram.** `PivoraTelegram.Core/Auth/AuthService.cs` and `SessionStore.cs` are the closest analogues; they conflate session persistence with login orchestration and use `Async().Wait()` patterns that we explicitly do not replicate here. The state machine that lives implicitly across `AuthService` flag fields is hoisted into `AuthStep` and made explicit.
7. **Telemetry.** A `TelemetryEmitter` subscriber to all events feeds counters (`account.login.completed`, `account.login.failed{reason}`) and a histogram for code-entry latency. Defined in `Application/Subscriptions/AccountTelemetryEmitter.cs`.
