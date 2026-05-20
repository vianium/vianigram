# Vianigram.Messages — Messages Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md), [01-account.md](01-account.md), [02-chats.md](02-chats.md). DDD + hex + managed Kernel + standard C# patterns. Kernel concepts come from `Vianigram.Kernel`.
>
> **Native cross-link:** message bodies, entities, and reply graphs are projected from TL records produced by `Vianigram.Core.Mtproto`. Media attachments carry `MediaRef` handles that resolve through `Vianigram.Media` — Messages never deals in raw bytes.
>
> **Roadmap position:** Phase 1.3 — the third managed context. It depends on Account (auth) and is the read/write peer of Chats (which owns dialog stubs). It is the largest context by volume of code and traffic, and the one where optimistic UI matters most.

---

## 1. Purpose

`Vianigram.Messages` owns the **per-dialog history** — the ordered, append-only-after-acknowledgement stream of `Message` entities for every conversation the user can see. It is the only place that knows about message bodies, edits, deletions, forwards, replies, reactions, read receipts, pins, and reaction summaries. It is also the home of the **outbox**: queued send commands that await network availability and acknowledgement.

The hard line: **Messages owns one aggregate per dialog (the `MessageStream`); it does not own the dialog list itself.** Chats owns the catalog and renders top-message stubs that come from us via events. The two contexts coordinate strictly by event, never by direct call.

---

## 2. Aggregate root

`MessageStream` — one root **per `(AccountId, PeerId)`**. A stream is a sparse, partially-cached window into the server-side history. Invariants:

- Entries are addressed by `MessageId(int)` server-assigned, monotonically increasing per dialog (with rare server-side rewrites we treat as deletions+inserts).
- An outgoing message before server ack carries a `ClientId(Guid)` and a synthetic `negative MessageId` that's locally unique and never collides with server IDs (Telegram itself reserves negative IDs for this).
- Coverage ranges (`HistoryCoverageRange(min, max)`) are tracked so we know which segments of history are fully cached vs. require a network fetch. PivoraTelegram's `MessageHistoryCoverageResult` taught us this pattern.
- `topMessageId` of a stream equals the max of `MessageId.value` over non-deleted entries; emitted to Chats whenever it changes.
- A reply edge `replyTo` may point to a message we don't have locally — that's fine; we resolve lazily on render.
- Edits never change `MessageId`; they bump `EditDate`.
- Deletions are tombstones with `Deleted` state and may be either two-sided (`revokeForAll = true`) or local (`revokeForAll = false`).
- Reactions are a sub-aggregate-of-aggregate: a `ReactionSummary` can be updated by the server independently of the carrier message and is reconciled by `ReactionsUpdated`.

There is a separate aggregate, **`Outbox`**, scoped per `AccountId` (not per peer), that owns pending sends/edits/deletes across all peers. It exists because retry and ordering across peers is a global concern (FLOOD_WAIT applies per RPC method, not per peer).

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `MessageStream` | aggregate | Per-dialog history root. |
| `Message` | entity | One message; identity = `MessageId` once acked, `ClientId` while pending. |
| `Outbox` | aggregate | Pending outgoing operations, account-scoped. |
| `OutboxEntry` | entity | One pending op (send/edit/delete/forward/react/read). |
| `ReactionSummary` | entity | Reaction counts + my reactions for one message. |
| `MessageId` | VO struct | `int`, with `IsClientLocal` predicate (negative). |
| `ClientId` | VO struct | `Guid`, optimistic identity until ack. |
| `MessageContent` | VO union | `Text` | `Photo` | `Video` | `Voice` | `RoundVideo` | `Document` | `Sticker` | `Animation` (gif) | `Poll` | `Location` | `Venue` | `Contact` | `Game` | `Invoice` | `WebPage` | `Service`. Closed sum. |
| `TextEntities` | VO list | Bold/italic/code/mention/url/spoiler/etc. ranges over text. |
| `MessageState` | VO enum | `PendingSend`, `Sending`, `Sent`, `EditedPendingSend`, `Acked`, `ReadByPeer`, `Deleted`. |
| `ReplyTo` | VO | `MessageId target` plus optional cached `MessagePreview`. |
| `ForwardHeader` | VO | `originalFromPeer`, `originalDate`, `originalMessageId`, `signature?`. |
| `MediaRef` | VO | Opaque handle into `Vianigram.Media`. Carries `(fileLocation, dcId, size, mimeType, thumb)`. Body bytes never live here. |
| `Reaction` | VO | Either an emoji string or a `customEmojiId: long`. |
| `ReadCursor` | VO | `(inboxMaxId, outboxMaxId)` per dialog — the message IDs through which I've read inbox and the peer has read outbox. |
| `MessageSearchHit` | VO | `(MessageId, snippet, highlights)` returned from search. |
| `MessageSnapshot` | VO sealed class | The only outbound shape. |
| `OutboxEntrySnapshot` | VO sealed class | UI-visible projection of pending sends. |

---

## 4. Domain events emitted

| Event | When | Payload |
|---|---|---|
| `MessageQueued` | Optimistic insert at send-time | `PeerId`, `OutboxEntrySnapshot`, `MessageSnapshot pending` |
| `MessageSending` | Outbox dispatcher pulled the entry | `ClientId`, `PeerId` |
| `OutgoingMessageAcked` | Server returned the real `MessageId` | `PeerId`, `ClientId`, `MessageId` |
| `OutgoingMessageFailed` | Terminal RPC error (e.g. `MESSAGE_TOO_LONG`, `USER_IS_BLOCKED`) | `ClientId`, `Error` |
| `MessageReceived` | Inbound message from `Vianigram.Sync` applied | `PeerId`, `MessageSnapshot` |
| `MessageEdited` | `messages.editMessage` applied (mine or peer's) | `PeerId`, `MessageSnapshot updated` |
| `MessageDeleted` | Deletion applied | `PeerId`, `IReadOnlyList<MessageId>`, `bool revokeForAll` |
| `MessageReadByPeer` | `outboxMaxId` advanced | `PeerId`, `MessageId upTo` |
| `ReactionsUpdated` | Reaction summary changed | `PeerId`, `MessageId`, `ReactionSummary` |
| `MessagePinned` / `MessageUnpinned` | Pin op | `PeerId`, `MessageId` |
| `MessageForwarded` | Forward operation completed | `PeerId source`, `MessageId source`, `PeerId destination`, `MessageId resulting` |
| `HistoryRangeLoaded` | A coverage hole was filled | `PeerId`, `MessageId from`, `MessageId to`, `int count` |

All published on `IEventBus`.

---

## 5. Inbound ports

```csharp
namespace Vianigram.Messages.Ports.Inbound
{
    public interface IMessagesApi
    {
        Task<Result<MessageSnapshot, Error>> SendTextAsync(PeerId peer, string text, TextEntities entities, Maybe<MessageId> replyTo, SendOptions options);
        Task<Result<MessageSnapshot, Error>> SendMediaAsync(PeerId peer, MediaRef media, string caption, TextEntities entities, Maybe<MessageId> replyTo, SendOptions options);
        Task<Result<MessageSnapshot, Error>> EditTextAsync(PeerId peer, MessageId id, string text, TextEntities entities);
        Task<Result<MessageSnapshot, Error>> EditMediaAsync(PeerId peer, MessageId id, MediaRef newMedia, string caption, TextEntities entities);
        Task<Result<Unit, Error>> DeleteAsync(PeerId peer, IReadOnlyList<MessageId> ids, bool revokeForAll);
        Task<Result<IReadOnlyList<MessageSnapshot>, Error>> ForwardAsync(PeerId from, IReadOnlyList<MessageId> ids, PeerId to, ForwardOptions options);
        Task<Result<Unit, Error>> ReactAsync(PeerId peer, MessageId id, IReadOnlyList<Reaction> reactions, bool big);
        Task<Result<Unit, Error>> ReadHistoryAsync(PeerId peer, MessageId upTo);
        Task<Result<Unit, Error>> PinAsync(PeerId peer, MessageId id, PinOptions options);
        Task<Result<Unit, Error>> UnpinAsync(PeerId peer, MessageId id);
        Task<Result<HistoryPage, Error>> GetHistoryAsync(PeerId peer, HistoryCursor cursor, int limit);
        Task<Result<IReadOnlyList<MessageSearchHit>, Error>> SearchAsync(PeerId peer, string query, SearchFilter filter, int limit, Maybe<MessageId> after);
        Task<Result<IReadOnlyList<MessageSnapshot>, Error>> GetByIdsAsync(PeerId peer, IReadOnlyList<MessageId> ids);
        IReadOnlyList<OutboxEntrySnapshot> ListPendingFor(PeerId peer);
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }
}
```

`HistoryCursor` is opaque (wraps offset_id + add_offset + offset_date). `HistoryPage` carries `(messages, cursorNext, hasGap, oldestId)`.

---

## 6. Outbound ports

| Port | Purpose | Adapter |
|---|---|---|
| `IMessageStore` | SQLite read/write of messages, scoped by `AccountId`. | `Infrastructure/Persistence/SqliteMessageStore.cs` |
| `ICoverageStore` | Track which `[min, max]` ranges per peer are fully cached. | same project |
| `IOutboxStore` | Persist outbox so pending sends survive app suspension. | `Infrastructure/Persistence/SqliteOutboxStore.cs` |
| `IAuthorizedInvoker` | from `Vianigram.Account`. | injected |
| `IMediaApi` | from `Vianigram.Media`. Needed to upload referenced files before `messages.sendMedia`. | injected |
| `IChatsObserver` | Read-only view: get a `PeerId`'s `MyPermissions` to fail-fast on send. | injected |
| `IClock`, `ILogger`, `ITelemetry` | Kernel | injected |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | Notes |
|---|---|---|
| `SendTextCommand` | `SendTextUseCase` | Creates `OutboxEntry`, emits `MessageQueued` *synchronously* with optimistic snapshot, then queues. |
| `SendMediaCommand` | `SendMediaUseCase` | Resolves `MediaRef` → ensures media is uploaded by `Vianigram.Media`, then `messages.sendMedia`. |
| `EditTextCommand` / `EditMediaCommand` | `EditMessageUseCase` | Optimistic; rollback on failure. |
| `DeleteMessagesCommand` | `DeleteMessagesUseCase` | `messages.deleteMessages` or `channels.deleteMessages`. |
| `ForwardMessagesCommand` | `ForwardMessagesUseCase` | `messages.forwardMessages` (max 100 per call, batched). |
| `ReactCommand` | `ReactUseCase` | `messages.sendReaction`; coalesces rapid taps. |
| `ReadHistoryCommand` | `ReadHistoryUseCase` | `messages.readHistory` / `channels.readHistory`. Idempotent — calls only when `upTo > inboxMaxId`. |
| `PinMessageCommand` / `UnpinMessageCommand` | `PinUseCase` | `messages.updatePinnedMessage`. |
| `LoadHistoryCommand` | `LoadHistoryUseCase` | `messages.getHistory` with coverage-range merging. |
| `SearchMessagesCommand` | `SearchMessagesUseCase` | `messages.search` (per-peer) or `messages.searchGlobal`. |
| `RequeueOutboxCommand` | `RequeueOutboxUseCase` | Triggered on connectivity restored. |

**Queries**

| Query | Returns |
|---|---|
| `GetHistoryQuery(peer, cursor, limit)` | `HistoryPage` (cache-first, fetches if hole) |
| `GetMessagesByIdsQuery(peer, ids)` | `IReadOnlyList<MessageSnapshot>` |
| `GetPendingForPeerQuery(peer)` | Outbox snapshots filtered |
| `GetReactionSummaryQuery(peer, id)` | `ReactionSummary` |
| `GetReadCursorQuery(peer)` | `ReadCursor` |

**Reactive subscribers**

- `SyncEventReactor` — on `Sync.MessageReceived/Edited/Deleted/Reactions/ReadHistory`, applies to the proper `MessageStream` and re-emits domain events.
- `OutboxDispatcher` — long-running background loop that pulls eligible entries, respects FLOOD_WAIT (per-method timer), serializes per-peer sends.
- `AccountReactor` — handles login/logout/active-switch.

---

## 8. Cross-context interactions

**Emits**

- `OutgoingMessageAcked`, `MessageReceived`, `MessageEdited`, `MessageDeleted` → consumed by `Vianigram.Chats` to keep top-message stub fresh.
- `MessageReadByPeer`, `ReadHistoryRequested` → consumed by Chats for unread counter zeroing.
- `MessageSendingMedia` → consumed by `Vianigram.Media` to bind upload progress to a UI message.

**Consumes**

- From `Vianigram.Sync`: `MessageReceived`, `MessageEdited`, `MessageDeleted`, `ReactionsUpdated`, `MessagePinned`, `ReadHistoryReceived`. Sync is the only legal source for inbound state changes from the server; we never read pts/qts ourselves.
- From `Vianigram.Account`: `LoginCompleted` (hook stores), `ActiveAccountChanged` (rebind), `AccountLoggedOut` (wipe per-account tables).
- From Kernel: `NetworkConnectivityChanged` (drives the outbox dispatcher).

**Capabilities:** `messages.reactions`, `messages.editing` (always on for own messages), `messages.search.global` (gated behind a feature flag — server endpoints differ across DCs).

---

## 9. Storage strategy

**SQLite (`{LocalFolder}/accounts/{accountId}/messages.db`):**

- `messages(peer_kind, peer_id, message_id, client_id, state, date, edit_date, from_peer, content_kind, content_blob, entities_blob, reply_to_id, fwd_blob, media_ref, reactions_blob, flags, PRIMARY KEY(peer_kind, peer_id, message_id))`
- `coverage(peer_kind, peer_id, min_id, max_id, PRIMARY KEY(peer_kind, peer_id, min_id))` — non-overlapping ranges; merged on insert.
- `outbox(client_id PK, peer_kind, peer_id, op_kind, payload_blob, created_at, attempts, next_attempt_at, last_error)`
- `read_cursor(peer_kind, peer_id, inbox_max_id, outbox_max_id, PRIMARY KEY(peer_kind, peer_id))`
- `pinned(peer_kind, peer_id, message_id, position, PRIMARY KEY(peer_kind, peer_id, message_id))`

Indexes: `(peer_kind, peer_id, date DESC)` for chronological reads, `(state, next_attempt_at)` for outbox dispatcher.

**Memory-only:**

- A bounded LRU per peer of recently-read messages (up to ~200 per active dialog, 5 dialogs hot) for instant scroll.
- Outbox in-memory mirror of the SQLite outbox table for sub-millisecond peer-pending queries.

**Encryption needs:** Message bodies are not encrypted at rest by us — at-rest encryption for non-secret chats relies on platform (BitLocker/full-disk). For secret chats, *all* messages live in `Vianigram.SecretChats` storage with key-bound encryption, **not** here.

**Optimistic UI contract:**

1. `SendTextAsync` returns synchronously with a `MessageSnapshot` whose `state = PendingSend` and whose `id.IsClientLocal == true`.
2. The outbox dispatcher picks it up, sets `Sending`, calls `messages.sendMessage`.
3. On success, the use case rewrites `(client_id → message_id)` in a single transaction and emits `OutgoingMessageAcked` plus a *replacement* `MessageSnapshot` so the UI can swap by `client_id`.
4. On failure, `state = PendingSend` with `last_error`; the user sees a retry chip.

---

## 10. Performance considerations

- **Send latency to optimistic render:** target ≤ 16 ms (one frame) from `SendTextAsync` call to `MessageQueued` event delivery on the UI thread. The use case takes a single SQLite write inside a small transaction; the in-memory mirror is updated first and the SQLite write is awaited but bounded.
- **History scroll:** UI requests pages of 30 messages; if cached, served from in-memory LRU (P50 ≤ 4 ms). If not, served from SQLite (P50 ≤ 35 ms on 512 MB-class hardware). If we cross a coverage gap, we fall back to network and emit `HistoryRangeLoaded` when filled.
- **Outbox throttling:** per `messages.send*` method, we honor FLOOD_WAIT with a per-method timer. Per-peer, sends are serialized — no two sends to the same peer in flight simultaneously, to preserve order.
- **Edit/delete bursts:** coalesced via debounce window of 250 ms for reactions (frequent rapid taps), but never for sends (every send is real).
- **Reaction summary updates:** server may send many `updateMessageReactions` for popular channels; we collapse same-message updates within a 100 ms window before re-emitting.
- **Big group throughput:** in supergroups with 100k+ users, we may receive ~50 `updateNewMessage` per second during peaks. Inserts are batched into 250 ms windows and applied transactionally; we measure under-100 ms insert-to-render even under load.
- **Memory ceiling:** message bodies cap at 64 KiB each (post-truncation safety, real messages are tiny); media references are pointers, not bytes, so RSS is bounded by visible-message count, not history depth.

---

## 11. Telegram MTProto methods used

| Method | Purpose |
|---|---|
| `messages.sendMessage` | Plain text send. |
| `messages.sendMedia` | Media-bearing send (one media + optional caption). |
| `messages.sendMultiMedia` | Album send (2–10 media as a group). |
| `messages.editMessage` | Edit text or caption (own or admin). |
| `messages.editInlineBotMessage` | Edit a bot inline result (admin path). |
| `messages.deleteMessages` | 1:1 / basic-group delete. |
| `channels.deleteMessages` | Channel/megagroup delete. |
| `messages.forwardMessages` | Forward N messages from one peer to another. |
| `messages.sendReaction` | Set/clear reactions on a message. |
| `messages.getMessagesReactions` | Refresh reaction summaries. |
| `messages.readHistory` | 1:1 / basic-group inbox cursor. |
| `channels.readHistory` | Channel inbox cursor. |
| `messages.readMessageContents` | Mark voice/photo content as viewed. |
| `messages.readMentions` | Clear mention badge. |
| `messages.updatePinnedMessage` | Pin or unpin in 1:1 / chat. |
| `messages.unpinAllMessages` | Bulk unpin. |
| `messages.getHistory` | Window paging. |
| `messages.getMessages` / `channels.getMessages` | Resolve replies and forwards lazily. |
| `messages.search` | Per-peer search with filter (text, photos, links, voice, etc.). |
| `messages.searchGlobal` | Global search across dialogs (capability-gated). |
| `messages.getDiscussionMessage` | Channel comments resolution. |
| `messages.getReplies` | Thread paging in channel comments. |
| `messages.checkChatInvite` / `messages.importChatInvite` | Used during forward-to-channel flows. |

---

## 12. Open questions / future work

1. **Albums (multi-media) in optimistic UI.** A 5-photo album appears as 5 messages with the same `grouped_id`. Optimistic render must group correctly before any server ack; we model `OutboxEntry` with an optional `groupKey` and the projector emits a single grouped `MessageSnapshot` to the UI. Edge case: if the album is *partially* uploaded when the user taps Send, we hold the dispatcher until all parts have a `MediaRef`.
2. **Scheduled messages.** `messages.sendMessage` accepts `schedule_date`. The aggregate has a `Scheduled` state but the `OutboxDispatcher` does not yet honor it — currently we punt to server-side scheduling. Local rescheduling and editing of scheduled drafts is open.
3. **Comments threading in channels.** A channel post has a discussion thread in a linked supergroup. Modeled as a separate `MessageStream` keyed by `(linkedSupergroupPeer, threadRootId)`; navigation is handled at the application layer. Threading depth is 1; no nested threads.
4. **Voice transcripts.** `messages.transcribeAudio` returns text for a voice message. We treat the result as a transient projection bolted onto the carrier `Message` via `transcript: Maybe<string>`; we do not store transcripts as separate entities.
5. **Translations.** `messages.translateText` is similarly transient.
6. **Reference notes from PivoraTelegram.** `MessageRepository.cs` keeps an in-memory `Dictionary<long, List<Message>>` plus a `MessageHistoryCoverageResult`. The coverage idea is preserved verbatim. The single global lock + periodic flush is replaced by per-peer streams and a journaling outbox. PivoraTelegram's `OutgoingMessageFormatter` and `MessageMapper` move into our `Application/Mappers/` and gain a `Result<>`-typed surface.
7. **Telemetry.** `MessagesTelemetryEmitter` emits `messages.sent.count`, `messages.received.count`, histograms for `messages.send.latency_ms` (queued → acked) and `messages.history.fetch.latency_ms`, and a gauge `messages.outbox.depth`.
