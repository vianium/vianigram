# Vianigram.Contacts — Contacts Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md), [01-account.md](01-account.md). DDD + hex + managed Kernel + standard C# patterns. Kernel concepts come from `Vianigram.Kernel`.
>
> **Native cross-link:** user metadata projections are normalized from TL `User` records produced by `Vianigram.Core.Mtproto`. Contacts owns the rich shape (status, photos, bio); other contexts hold opaque `PeerId` references and look rich data up here.
>
> **Roadmap position:** Phase 1.4 — depends on Account, peer of Chats and Messages. Contacts is small and deliberately so: the pre-existing Pivora code conflated user lookup, contact import, and blocking into one repository. Splitting these out makes blocking enforcement explicit.

---

## 1. Purpose

`Vianigram.Contacts` owns the user's **address book intersection** with Telegram: phone contacts imported from the device, Telegram-known users discovered via search or chat membership, the saved-contacts list, and the **blocked users** list. It is the source of truth for `User` rich metadata (display name, username, phone, bio, profile photo reference, online status) — every other context that needs to render a peer's name or avatar reads from here.

The hard line: **Contacts owns *Users*; Chats owns *Dialogs***. A user with no dialog is still a contact (e.g. someone you imported but never messaged). A dialog without a known user (rare; transient during sync) is fine — Chats holds a stub and asks Contacts to hydrate.

---

## 2. Aggregate root

`ContactBook` — single root per `AccountId`, conceptually the user's full address book. Invariants:

- The book contains a finite set of `Contact` entries (not capped client-side, but Telegram caps server-side at 5000 imported contacts).
- A `Contact` has a `userId` if and only if the contact was successfully resolved on the server. Unresolved contacts (imported but with no Telegram match) are still tracked for "Invite to Telegram" UX.
- The blocked set is a separate sub-collection: a user can be blocked even if they aren't a contact.
- `mutualContact` is a server-asserted boolean we mirror but never compute locally.
- Block invariant: blocking a peer cascades — Chats archives the dialog, Messages refuses sends to that peer, and Calls refuses incoming calls. Contacts only emits the event; consumers enforce on their side.

There is also a separate **`UserDirectory`** entity — *not* an aggregate root — that caches `User` records for users who are not in your contacts but are visible because they appear in chats you're in. The directory is bounded by an LRU (configurable, default 5000 entries) to prevent unbounded memory growth in big communities.

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `ContactBook` | aggregate | Root, owns saved contacts + blocked list. |
| `Contact` | entity | One imported or saved contact; identity = `client_id` if unresolved, else `userId`. |
| `UserDirectory` | service-ish entity | LRU cache of `User` rich records keyed by `userId`. |
| `User` | entity | Rich Telegram user record. Identity = `userId`. |
| `BlockedRelation` | entity | One blocked-user record; identity = `userId`. |
| `UserId` | VO struct | `long`, namespaced (`user:1234567890`). |
| `Username` | VO struct | `@-prefixed handle`, lowercased on storage, validated 5–32 chars + alphanumeric/underscore rules. |
| `PhoneNumber` | VO struct | E.164 string. |
| `DisplayName` | VO struct | `(firstName, lastName)`; `ToString()` returns " "-joined trimmed. |
| `OnlineStatus` | VO union | `Online(expires)` | `Recently` | `WithinWeek` | `WithinMonth` | `LongAgo` | `Hidden` | `Empty`. |
| `UserPhotoRef` | VO record | `(photoId, dcId, accessHash, smallLoc, bigLoc)` — opaque pointers, no bytes. |
| `Bio` | VO struct | Up to 70 chars. |
| `ContactSource` | VO enum | `DeviceImport`, `ManualPhoneAdd`, `ResolvedFromUsername`, `ResolvedFromChat`. |
| `BlockReason` | VO enum | `User`, `BotPm`, `Stories`. (Telegram has separate "block from stories" semantics.) |
| `MutualState` | VO enum | `None`, `Mutual`, `OneWay`. |
| `ContactSnapshot` / `UserSnapshot` / `BlockedSnapshot` | VO sealed | Outbound projections. |

---

## 4. Domain events emitted

| Event | When | Payload |
|---|---|---|
| `ContactsImported` | `contacts.importContacts` returned | `int imported`, `int matched`, `int retryLater` |
| `ContactAdded` | A contact appears in the book | `ContactSnapshot` |
| `ContactRemoved` | Contact deleted | `UserId` |
| `ContactUpdated` | Display name / phone / username changed | `ContactSnapshot` |
| `UserResolved` | Username or phone search returned a `User` | `UserSnapshot` |
| `UserUpdated` | Status, photo, bio change | `UserSnapshot` |
| `UserBlocked` | Block applied | `UserId`, `BlockReason` |
| `UserUnblocked` | Block lifted | `UserId`, `BlockReason` |
| `MutualStatusChanged` | Server changed mutual flag | `UserId`, `MutualState previous`, `MutualState current` |

---

## 5. Inbound ports

```csharp
namespace Vianigram.Contacts.Ports.Inbound
{
    public interface IContactsApi
    {
        Task<Result<ImportSummary, Error>> ImportFromDeviceAsync(IReadOnlyList<DevicePhoneEntry> entries);
        Task<Result<IReadOnlyList<ContactSnapshot>, Error>> ListAsync();
        Task<Result<ContactSnapshot, Error>> AddByPhoneAsync(PhoneNumber phone, DisplayName name);
        Task<Result<Unit, Error>> RemoveAsync(UserId user);
        Task<Result<UserSnapshot, Error>> ResolveByUsernameAsync(Username username);
        Task<Result<IReadOnlyList<UserSnapshot>, Error>> SearchAsync(string query, int limit);
        Task<Result<Unit, Error>> BlockAsync(UserId user, BlockReason reason);
        Task<Result<Unit, Error>> UnblockAsync(UserId user, BlockReason reason);
        Task<Result<IReadOnlyList<BlockedSnapshot>, Error>> ListBlockedAsync();
        Maybe<UserSnapshot> GetUser(UserId user);
        Task<Result<UserSnapshot, Error>> FetchUserAsync(UserId user);
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }

    public interface IUserResolver  // capability for other contexts
    {
        Maybe<UserSnapshot> GetUserCached(UserId user);
        Task<Result<UserSnapshot, Error>> GetUserOrFetchAsync(UserId user);
    }
}
```

`IUserResolver` is the slim capability that Chats, Messages, and SecretChats receive. They never get the full `IContactsApi`.

---

## 6. Outbound ports

| Port | Purpose | Adapter |
|---|---|---|
| `IContactStore` | SQLite for contacts list and blocked list. | `Infrastructure/Persistence/SqliteContactStore.cs` |
| `IUserCacheStore` | SQLite for user directory LRU. | `Infrastructure/Persistence/SqliteUserCacheStore.cs` |
| `IDevicePhonebookPort` | Read device contacts via `Windows.ApplicationModel.Contacts`, requires `contacts` capability in `Package.appxmanifest`. | `Infrastructure/Device/WindowsPhonebookAdapter.cs` |
| `IAuthorizedInvoker` | from Account. | injected |
| `IClock`, `ILogger`, `ITelemetry` | Kernel | injected |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | MTProto |
|---|---|---|
| `ImportFromDeviceCommand` | `ImportFromDeviceUseCase` | `contacts.importContacts` (chunked 100/call). |
| `AddByPhoneCommand` | `AddContactUseCase` | `contacts.addContact`. |
| `RemoveContactCommand` | `RemoveContactUseCase` | `contacts.deleteContacts`. |
| `BlockUserCommand` | `BlockUserUseCase` | `contacts.block`. |
| `UnblockUserCommand` | `UnblockUserUseCase` | `contacts.unblock`. |
| `ResolveUsernameCommand` | `ResolveUsernameUseCase` | `contacts.resolveUsername`. |
| `SearchContactsCommand` | `SearchContactsUseCase` | `contacts.search`. |
| `RefreshContactsCommand` | `RefreshContactsUseCase` | `contacts.getContacts(hash)` — full sync with hash-based diff. |
| `RefreshBlockedListCommand` | `RefreshBlockedUseCase` | `contacts.getBlocked` (paged). |
| `FetchUsersByIdsCommand` | `FetchUsersUseCase` | `users.getFullUser` for full bio, `users.getUsers` for batched basic info. |

**Queries**

| Query | Returns |
|---|---|
| `ListContactsQuery` | `IReadOnlyList<ContactSnapshot>` (sorted by display name) |
| `ListBlockedQuery` | `IReadOnlyList<BlockedSnapshot>` |
| `GetUserQuery(userId)` | `Maybe<UserSnapshot>` |
| `SearchLocalQuery(prefix)` | Local fuzzy search over contacts and recently-cached users |
| `IsBlockedQuery(userId)` | `bool` (synchronous, in-memory) |

**Reactive subscribers**

- `SyncEventReactor` — on `Sync.UserStatusChanged`, `Sync.UserNameChanged`, `Sync.ContactsResetRequired`, applies updates.
- `AccountReactor` — login/logout/active-switch.

---

## 8. Cross-context interactions

**Emits**

- `ContactAdded`, `ContactRemoved`, `UserUpdated` → consumed by Chats (refresh dialog title/avatar via `IUserResolver`).
- `UserBlocked` → consumed by Chats (archive dialog), Messages (block sends — fast-fails via `IsBlocked` check before queueing), Calls (reject incoming/outgoing).
- `UserUnblocked` → reverses the above for new actions; existing archived dialog stays archived (server semantics).

**Consumes**

- From `Vianigram.Sync`: `UserStatusChanged`, `UserPhotoChanged`, `UserNameChanged`, `ContactsResetRequired`. Sync is the inbound channel; we do not poll status ourselves.
- From `Vianigram.Account`: `LoginCompleted` (initial `contacts.getContacts` and `contacts.getBlocked`), `ActiveAccountChanged`, `AccountLoggedOut`.

**Capabilities:** `contacts.device_import` (gated on platform `contacts` capability + user grant), `contacts.username_resolve`, `contacts.block`. The first one fails-closed if the user denies the capability.

---

## 9. Storage strategy

**SQLite (`{LocalFolder}/accounts/{accountId}/contacts.db`):**

- `contacts(client_id, user_id, phone, first_name, last_name, source, mutual, last_modified, PRIMARY KEY(client_id))` — `user_id` nullable until resolved.
- `users(user_id PK, username, phone, first_name, last_name, status_kind, status_expires, photo_id, photo_dc, photo_small_loc, photo_big_loc, bio, lang_code, flags, last_seen_local)`
- `blocked(user_id, reason, blocked_at, PRIMARY KEY(user_id, reason))`
- `username_index(username PK, user_id, last_seen)` — fast `@username` lookup.
- `phone_index(phone PK, user_id, last_seen)` — fast phone lookup.

Indexes: `(first_name, last_name)` for display-name sort; partial index on `users.status_kind = Online` for "online now" filters.

**Memory-only:**

- `UserDirectory` LRU mirroring `users` table, capped at 5000 hot users. Eviction is deterministic — least-recently-touched, where "touch" = read or update.
- A bloom filter for "is this user blocked" used by Messages' fast-fail check (false positives fall through to SQLite, false negatives impossible).

**Encryption needs:** Phone numbers and contact metadata are mildly sensitive (PII). We rely on the platform's app-data isolation rather than additional encryption — the threat model is not "attacker with disk image", it's "another app on the device", which WP8.1 isolates by default. *Bio* and *display names* may include personal info; same treatment.

**Logout wipe:** drop all three tables for the logged-out `AccountId`. The `UserDirectory` LRU is rebound to the new active account at swap time.

---

## 10. Performance considerations

- **Cold contact list render:** target ≤ 80 ms on 512 MB-class hardware. Served from SQLite (`SELECT * FROM contacts ORDER BY first_name, last_name LIMIT 200`) plus an async network refresh.
- **Phonebook import volume:** users may have thousands of phone contacts. We batch into 100-entry calls to `contacts.importContacts`, with a 250 ms gap between calls to be polite (and not trigger FLOOD_WAIT). Total import for 2000 contacts: ~12 s wall-clock acceptable as a one-time cost.
- **Diff-based contacts sync:** `contacts.getContacts(hash)` — we send a hash of our current contact ID set; the server returns `contactsNotModified` cheaply if unchanged. Hash recomputation is incremental.
- **Status updates fan-in:** in big group chats, `updateUserStatus` may fire frequently. We coalesce per-user within a 200 ms window before emitting `UserUpdated`. The `UserDirectory` write is in-memory; SQLite write is debounced by 1 s.
- **Username resolution cache:** when the user types `@foo`, we hit `username_index` first; only if missing or stale (TTL 24h) do we issue `contacts.resolveUsername`.
- **Avatar fetch:** Contacts only stores the `UserPhotoRef`. Actual bytes are fetched on demand via `Vianigram.Media`. The first time a user is rendered we kick off a small download; subsequent renders read the cached blob.
- **Block-check hot path:** `IsBlockedQuery` must be sub-millisecond because Messages calls it on every send. The bloom filter + in-memory hashset gives us O(1).

---

## 11. Telegram MTProto methods used

| Method | Purpose |
|---|---|
| `contacts.importContacts` | Bulk import device contacts; server returns matched users + retry hints. |
| `contacts.addContact` | Add a single contact by phone number. |
| `contacts.deleteContacts` | Remove contact(s). |
| `contacts.deleteByPhones` | Remove by phone (when `userId` is unknown). |
| `contacts.getContacts` | Fetch saved contacts list with hash diff. |
| `contacts.search` | Server-side search by name across users you can see. |
| `contacts.resolveUsername` | `@username` → `User`. |
| `contacts.block` / `contacts.unblock` | Block/unblock with reason variant. |
| `contacts.getBlocked` | Page through blocked list. |
| `contacts.getTopPeers` | "Frequent contacts" hint surface (used by ranking, not modeled as data here yet). |
| `contacts.toggleTopPeers` | User opt-out of the above. |
| `contacts.resetSaved` | Wipe server-side saved contacts (irreversible; behind a confirm dialog). |
| `users.getUsers` | Batched basic info for ID lists. |
| `users.getFullUser` | Bio, photos, common chats — cached separately under TTL. |
| `account.updateStatus` | Set my own online/offline state. (Outbound own-status push — we still emit through here even though it's about *me*, because UI bind point is here.) |

---

## 12. Open questions / future work

1. **Exporting / removing contacts on cloud reset.** When the user taps "Reset saved contacts", the server wipes its copy but the device-contact mirror persists. We need a policy: should we also delete locally? Default: no, because the device side is the user's address book. The aggregate enforces this asymmetry but we should make it explicit in the security doc.
2. **Stories blocking.** Telegram added separate "block stories" semantics. We model it via `BlockReason.Stories` but the UX flow (separate toggle in user profile) is unimplemented in v1.
3. **Privacy settings.** Who can see my phone number / last seen / etc. live under `account.getPrivacy` / `account.setPrivacy`. They are loosely related to Contacts but really belong in a future `Vianigram.Privacy` context. Today they're stubbed in `Application/UseCases/PrivacyStub.cs`.
4. **Premium emoji statuses on users.** A user can carry a custom emoji status. We store the `customEmojiId` on `User` but we don't yet pull the emoji document; that's a Stickers/Media concern.
5. **Reference notes from PivoraTelegram.** `UserRepository.cs` flattened user metadata into a single mutable `User` model with a global lock. We split into `User` rich entity + `UserDirectory` LRU + `phone_index`/`username_index` projections. The `Contact` model from Pivora's `Models/Contact.cs` corresponds to our `Contact` entity but gains `source` and `mutual` fields and explicit `clientId`-vs-`userId` distinction for unresolved imports.
6. **Bot users.** A `User.flags` includes `bot`. Today we surface `IsBot` on `UserSnapshot` but don't have a separate aggregate for bot capabilities (commands, inline mode). When we add bot UX in Phase 5, a `BotProfile` entity will hang off `User` here.
7. **Telemetry.** `ContactsTelemetryEmitter` emits `contacts.imported.count`, `contacts.resolve.miss_ratio`, `contacts.block.count`, and a histogram for `contacts.importContacts.batch_latency_ms`.
