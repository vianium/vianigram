# Vianigram.Chats — Chats Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md), and [01-account.md](01-account.md). This doc assumes DDD + hex + managed Kernel + the standard C# patterns. Kernel concepts are imported from `Vianigram.Kernel` and not redefined.
>
> **Native cross-link:** the dialog list, peer types, and folder taxonomy mirror the data model used by `Vianigram.Core.Mtproto`'s TL types but are projected into a managed shape that has no idea what a TL constructor is. Same DDD+hex skeleton, different technology.
>
> **Roadmap position:** Phase 1.2 — first context to depend on Account. Chats is the user's "home screen" surface, so its read API is on the critical path of perceived launch latency. We extract it second so the UI shell can land on a real list quickly even before Messages or Sync are wired.

---

## 1. Purpose

`Vianigram.Chats` owns the **dialog catalog**: the ordered list of conversations a user has, with their metadata (last message stub, unread count, mute state, pin/folder placement) but **not** the messages themselves. It is the projection layer between Telegram's `messages.getDialogs` flat result and the user-facing chat list. It owns dialog-level operations — pin, archive, mute, folder reassignment, mark all read — and emits domain events whenever the list shape changes so that the UI, Sync, and Messages contexts can react.

The hard line we draw: **a `Dialog` carries a *pointer* to the latest message but never the message body history.** History is `Vianigram.Messages`'s aggregate. This separation lets us hydrate the chat list with one round-trip and lazy-load history on dialog open.

---

## 2. Aggregate root

`Dialog` — one root per conversation, identified by `PeerId`. Each invariants:

- Exactly one of `peer.kind ∈ {User, Chat, Channel}` is set; the other discriminator fields are `null`.
- `unreadCount ≥ 0`; clamped to 0 when `markedUnread = false` and the read cursor crosses the latest message.
- `topMessage.id` is monotone non-decreasing **per dialog** during a stable session — only resets on `getDifference` reseed.
- A pinned dialog has a `pinIndex ≥ 0`; unpinning clears it. The set of pinned dialogs in a folder is bounded (5 for Default, configurable via `messages.getPinnedDialogs` server cap).
- `muteUntil` is either `Maybe.None` (not muted), a specific `DateTimeOffset`, or `DateTimeOffset.MaxValue` (muted forever).
- `folderId ∈ {0 = Default, 1 = Archive, custom ≥ 2}`. Default and Archive are server-fixed; custom folders are user-defined and live in `DialogFolder` siblings.
- `draftMessage` (if any) is owned by Chats — it isn't a real message yet, just user-typed text awaiting send. Drafts are persisted *here*, not in Messages.

There is also a non-aggregate **collection invariant** maintained by the application layer: dialog ordering within a folder is `(pinIndex asc when pinned) → (topMessage.date desc)`. This invariant is tested but expressed as a service, not as a root.

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `Dialog` | aggregate | Root, see above. |
| `DialogFolder` | entity | A user-defined folder grouping dialogs. Identity = `folderId`. Holds title and inclusion/exclusion peer rules. |
| `DialogDraft` | entity-ish VO | Local draft text + entities + reply-to-message-id. Not yet a Message. |
| `PeerId` | VO struct | Tagged union: `(kind: PeerKind, id: long, accessHash: long)` — the access hash is required to address peers across DCs. |
| `PeerKind` | VO enum | `User`, `Chat` (basic group), `Channel` (broadcast or megagroup). |
| `ChannelKind` | VO enum | `Broadcast`, `Megagroup`, `Gigagroup`, `Forum`. |
| `DialogTopMessageRef` | VO record | `(messageId: int, date: DateTimeOffset, fromPeer: PeerId, kindPreview: MessageKindPreview)`. Stub only. |
| `MessageKindPreview` | VO enum | `Text`, `Photo`, `Video`, `Voice`, `Document`, `Sticker`, `Gif`, `Poll`, `Location`, `Contact`, `Service`. |
| `UnreadCounters` | VO record | `(unread: int, mentions: int, reactions: int)`. |
| `MuteState` | VO record | `(muted: bool, until: Maybe<DateTimeOffset>, soundOverride: Maybe<string>)`. |
| `PinPosition` | VO struct | `int pinIndex` within a folder, validated 0–server cap. |
| `FolderId` | VO struct | `int`, with constants `Default = 0`, `Archive = 1`. |
| `DialogTtl` | VO struct | Auto-delete window in seconds for the dialog (Telegram's "auto-delete messages" feature). |
| `ChatPermissions` | VO bitfield | Send messages, send media, embed links, pin, change info, etc. |
| `MyPermissions` | VO bitfield | What *I* can do here, derived from `ChatPermissions ∩ banRights`. |
| `DialogSnapshot` | VO sealed class | Immutable projection. The only thing that crosses the boundary outward. |
| `FolderSnapshot` | VO sealed class | Immutable projection of `DialogFolder`. |

---

## 4. Domain events emitted

| Event | When | Payload |
|---|---|---|
| `DialogListLoaded` | After initial `messages.getDialogs` completes | `IReadOnlyList<DialogSnapshot>`, `bool fromCache` |
| `DialogAdded` | A new conversation appears (peer started a chat, joined a channel) | `DialogSnapshot` |
| `DialogRemoved` | Left a chat, deleted a conversation, blocked a peer | `PeerId`, `DialogRemovalReason` |
| `DialogTopMessageChanged` | New top message stub (push, sync, or send) | `PeerId`, `DialogTopMessageRef previous`, `DialogTopMessageRef current` |
| `DialogUnreadChanged` | Counters change (read receipt processed, new unread) | `PeerId`, `UnreadCounters` |
| `DialogPinned` / `DialogUnpinned` | Pin toggle | `PeerId`, `FolderId`, `Maybe<PinPosition>` |
| `DialogMuted` / `DialogUnmuted` | Mute toggle | `PeerId`, `MuteState` |
| `DialogMovedToFolder` | Folder reassignment | `PeerId`, `FolderId from`, `FolderId to` |
| `DialogArchived` / `DialogUnarchived` | Convenience over folder=Archive | `PeerId` |
| `DialogDraftChanged` | User edits a draft | `PeerId`, `Maybe<DialogDraft>` |
| `FolderCreated` / `FolderUpdated` / `FolderDeleted` | Folder CRUD | `FolderSnapshot` |
| `DialogReadReceiptApplied` | A `readHistoryInbox/Outbox` settled | `PeerId`, `int upToMessageId`, `ReadDirection` |

All events extend `DomainEventBase` and publish on `IEventBus`.

---

## 5. Inbound ports

```csharp
namespace Vianigram.Chats.Ports.Inbound
{
    public interface IChatsApi
    {
        Task<Result<IReadOnlyList<DialogSnapshot>, Error>> ListAsync(FolderId folder, int limit, Maybe<DialogCursor> after);
        Task<Result<DialogSnapshot, Error>> GetAsync(PeerId peer);
        Task<Result<Unit, Error>> PinAsync(PeerId peer, FolderId folder);
        Task<Result<Unit, Error>> UnpinAsync(PeerId peer);
        Task<Result<Unit, Error>> ArchiveAsync(PeerId peer);
        Task<Result<Unit, Error>> UnarchiveAsync(PeerId peer);
        Task<Result<Unit, Error>> MoveToFolderAsync(PeerId peer, FolderId folder);
        Task<Result<Unit, Error>> MuteAsync(PeerId peer, MuteState state);
        Task<Result<Unit, Error>> MarkAllReadAsync(PeerId peer);
        Task<Result<Unit, Error>> MarkUnreadAsync(PeerId peer, bool unread);
        Task<Result<Unit, Error>> SetDraftAsync(PeerId peer, Maybe<DialogDraft> draft);
        Task<Result<IReadOnlyList<FolderSnapshot>, Error>> ListFoldersAsync();
        Task<Result<FolderSnapshot, Error>> CreateFolderAsync(string title, FolderRules rules);
        Task<Result<Unit, Error>> DeleteFolderAsync(FolderId folder);
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }
}
```

`DialogCursor` is opaque (server `(offsetDate, offsetId, offsetPeer)` triple wrapped) so paging is transparent to callers.

---

## 6. Outbound ports

| Port | Purpose | Adapter |
|---|---|---|
| `IDialogStore` | SQLite read/write of `Dialog` rows, scoped by `AccountId`. | `Infrastructure/Persistence/SqliteDialogStore.cs` |
| `IFolderStore` | SQLite read/write of `DialogFolder`. | same project |
| `IDraftStore` | Persist drafts (often dirty between launches). | `Infrastructure/Persistence/SqliteDraftStore.cs` |
| `IAuthorizedInvoker` | Provided by `Vianigram.Account`. The only legal way to issue MTProto calls. | injected |
| `IPeerCache` | Lookup `User`/`Chat`/`Channel` rich objects by `PeerId`. Implemented inside Chats but exposed to peers (Contacts hydrates user metadata into it). | `Infrastructure/Cache/InMemoryPeerCache.cs` |
| `IClock`, `ILogger`, `ITelemetry` | Kernel | injected |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | Description |
|---|---|---|
| `SyncDialogListCommand(folder, mode)` | `SyncDialogListUseCase` | Issue `messages.getDialogs` (or `messages.getPinnedDialogs` for top of list), reconcile with cache, emit deltas. |
| `PinDialogCommand(peer, folder)` | `PinDialogUseCase` | Calls `messages.toggleDialogPin`. |
| `UnpinDialogCommand(peer)` | same use case | inverse |
| `ArchiveDialogCommand(peer)` | `ArchiveDialogUseCase` | Calls `folders.editPeerFolders` with folder=Archive. |
| `MoveToFolderCommand(peer, folder)` | `MoveToFolderUseCase` | Calls `folders.editPeerFolders`. |
| `MuteDialogCommand(peer, state)` | `MuteDialogUseCase` | Calls `account.updateNotifySettings`. |
| `MarkAllReadCommand(peer)` | `MarkAllReadUseCase` | `messages.readHistory` or `channels.readHistory` depending on peer kind. |
| `MarkDialogUnreadCommand(peer, unread)` | `MarkDialogUnreadUseCase` | `messages.markDialogUnread`. |
| `SetDraftCommand(peer, draft)` | `SetDraftUseCase` | `messages.saveDraft` (debounced 1.5 s). |
| `CreateFolderCommand(...)` / `UpdateFolderCommand(...)` / `DeleteFolderCommand(...)` | `FolderManagementUseCase` | `messages.updateDialogFilter`. |

**Queries**

| Query | Returns |
|---|---|
| `ListDialogsQuery(folder, limit, cursor)` | Paged `IReadOnlyList<DialogSnapshot>` |
| `GetDialogQuery(peer)` | `Maybe<DialogSnapshot>` |
| `ListFoldersQuery` | `IReadOnlyList<FolderSnapshot>` |
| `GetUnreadTotalQuery(folder)` | `UnreadCounters` summed |
| `SearchDialogsQuery(q)` | Local-only fuzzy search over titles |

**Reactive subscribers** (in `Application/Subscriptions`)

- `SyncEventReactor` listens to `MessageReceived` from `Vianigram.Sync` and updates `topMessage` + `unreadCount`.
- `MessagesAckReactor` listens to `OutgoingMessageAcked` from `Vianigram.Messages` (so the optimistic top-message stub gets reconciled).
- `AccountReactor` listens to `ActiveAccountChanged` and `AccountLoggedOut` to rebind stores.

---

## 8. Cross-context interactions

**Emits**

- `DialogListLoaded`, `DialogAdded`, `DialogRemoved` → consumed by Presentation (UI shell).
- `DialogTopMessageChanged` → consumed by UI; *not* by Messages (Messages is upstream — see below).
- `DialogReadReceiptApplied` → consumed by Sync (to pin pts cursor) and Messages (to clear unread state on rendered messages).

**Consumes**

- `MessageReceived(peerId, msg)` from `Vianigram.Sync` → mutate top message, increment unread if not from self.
- `MessageEdited(peerId, msg)` from Sync → if `msg.id == topMessage.id`, refresh top stub.
- `MessageDeleted(peerId, ids)` from Sync → if any equals `topMessage.id`, request a fresh `messages.getHistory limit=1`.
- `OutgoingMessageAcked` from Messages → update top stub from temp client_id to server id atomically with the receipt.
- `LoginCompleted` from Account → trigger initial `SyncDialogListCommand(folder=Default, mode=Cold)`.
- `ActiveAccountChanged` from Account → invalidate caches, re-bind stores, re-issue Cold sync.
- `AccountLoggedOut` from Account → wipe SQLite tables scoped by `AccountId`.

**Capability**: `chats.folders` (default on), `chats.archive` (on), `chats.drafts` (on). Capabilities are advertised through `ICapabilityRegistry`.

---

## 9. Storage strategy

**SQLite (`{LocalFolder}/accounts/{accountId}/chats.db`):**

- `dialogs(peer_kind, peer_id, access_hash, top_msg_id, top_msg_date, top_msg_from_peer, top_msg_kind, unread_count, mention_count, reaction_count, mute_until, pin_index, folder_id, dialog_ttl, my_permissions, draft_text, draft_reply_to, last_modified, PRIMARY KEY(peer_kind, peer_id))`
- `folders(folder_id PK, title, rules_blob, position)`
- `peer_cache(peer_kind, peer_id, access_hash, title, photo_id, status_blob, PRIMARY KEY(peer_kind, peer_id))` — projection of users/chats/channels relevant to dialogs; rich source-of-truth lives in Contacts and is cross-fed.

Indexes: `(folder_id, pin_index, top_msg_date DESC)` for the list query; `(peer_id)` already PK; partial index on `unread_count > 0` for badge counter aggregations.

**Memory-only:**

- Sorted in-memory list per folder (`SortedList<long, Dialog>`-equivalent keyed by `(pin_index, -top_msg_date)`) for sub-millisecond list queries on the hot path. Rebuilt from SQLite at cold start.
- Draft debounce timers.

**Encryption needs:** None at rest beyond what the OS file system provides — dialog metadata is non-secret. *Drafts*, however, are user-authored text and **are** encrypted via `DataProtectionProvider`. The `IDraftStore` adapter writes ciphertext, transparently to the application layer.

**Schema versioning:** `dialogs.schema_version` table; migrations are forward-only and idempotent. PivoraTelegram's `DialogRepository.StorageVersion = 9` taught us that monolithic version bumps that re-fetch everything are fine for this size of cache.

---

## 10. Performance considerations

- **Cold list render:** target ≤ 250 ms from `LoginCompleted` to first chats-page-rendered on a 512 MB-class hardware. We achieve this by serving from SQLite immediately and issuing the network `messages.getDialogs` in parallel; the network result reconciles via deltas.
- **Reconciliation cost:** when a `getDifference` returns 1000 updates, a naive "rebuild list" is O(n) and visible. We instead apply a typed *delta journal* — `(peer, op)` tuples — and re-sort only the affected folder bucket.
- **Folder switches:** instant, because each folder has its own sorted in-memory bucket. Folder rule changes (custom folders include/exclude lists) trigger a one-shot rebuild.
- **Top-message preview generation:** done lazily in the projector; doesn't touch full message body. The `MessageKindPreview` enum is enough for UI.
- **Mute-state refresh:** every dialog has its own `muteUntil`. Rather than a periodic sweep, we register a single timer for the *next* expiring mute and rearm on completion. O(1) cost regardless of dialog count.
- **Sort key cost:** `(pinIndex, -topMsgDate)` is precomputed and stored in the in-memory entry; sort is on already-materialized keys.
- **Batched reads:** `markAllReadAsync` doesn't fan out per-message — it's a single RPC plus a single SQL update of the read cursor.

---

## 11. Telegram MTProto methods used

| Method | Purpose |
|---|---|
| `messages.getDialogs` | Initial and paginated list fetch (offset_date/offset_id/offset_peer + limit + hash). |
| `messages.getPinnedDialogs` | Cheap top-of-list refresh. |
| `messages.getDialogFilters` | Custom folder definitions. |
| `messages.updateDialogFilter` | Create/update/delete a custom folder. |
| `messages.updateDialogFiltersOrder` | Reorder folder tabs. |
| `messages.toggleDialogPin` | Pin/unpin within current folder. |
| `messages.reorderPinnedDialogs` | Drag-reorder pinned set. |
| `folders.editPeerFolders` | Move dialog between Default/Archive (and custom). |
| `messages.markDialogUnread` | Toggle unread badge without unread messages. |
| `messages.readHistory` | 1:1 / basic-group read cursor. |
| `channels.readHistory` | Channel/megagroup read cursor. |
| `messages.saveDraft` | Persist user-typed drafts server-side. |
| `messages.clearAllDrafts` | Bulk wipe drafts. |
| `account.updateNotifySettings` | Mute/unmute, sound, until-when. |
| `channels.getChannels` | Hydrate channel metadata in batches when loading dialogs. |
| `messages.getPeerSettings` | Determine "report spam" and other peer-bar UI hints. |

---

## 12. Open questions / future work

1. **Forum topics.** Telegram introduced topics inside megagroups (channel kind = `Forum`). They're a sub-aggregate of a `Dialog` but the data model implications are non-trivial: each topic has its own pts/qts and its own unread cursor. We'll model them as a `ForumTopic` entity inside the `Dialog` aggregate when we tackle forums in Phase 4. The aggregate root stays `Dialog` to keep cross-context semantics simple.
2. **Stories integration.** Stories live atop the dialog list as a horizontal strip. They are *not* a Chats concern (a future `Vianigram.Stories` context will own them) but they need a stable hook point in the chat list view; we expose `IChatsApi.GetStoryEligibleDialogs()` returning peers that have unread stories — a thin read query.
3. **Offline pin reconciliation.** If the user pins offline and the server later returns a different pin order (someone else pinned via another device), our local pin survives only as a hint. We need a "merge pinned" rule. Current default: server wins, local pending pin requeued.
4. **Search-while-typing across dialogs and global users.** Today Chats does *local* dialog title search. Global search (`messages.searchGlobal`) is a separate use case and may live in `Vianigram.Messages` or a future `Vianigram.Search` context.
5. **Reference notes from PivoraTelegram.** `PivoraTelegram.Core/Cache/DialogRepository.cs` keeps an in-memory `List<Dialog>` plus a periodic flush, with a single global lock and an explicit `StorageVersion` field. We keep the in-memory-first idea but split read paths from write paths and replace the `Dialog` god-DTO with a real aggregate. Drafts, which Pivora kept in `MessageRepository`, are moved here where they belong (a draft isn't a message).
6. **Dialog auto-delete TTL.** The `DialogTtl` value object exists; the application use case to set it (`messages.setHistoryTTL`) is stubbed in `Application/UseCases/SetHistoryTtlUseCase.cs` but unwired in v1.
7. **Telemetry.** `ChatsTelemetryEmitter` emits `chats.list.size`, `chats.unread.total`, and a histogram for `chats.list.cold_render_ms`.
