# Vianigram.Sync — Sync Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md), [01-account.md](01-account.md). DDD + hex + managed Kernel + standard C# patterns. Kernel concepts come from `Vianigram.Kernel`.
>
> **Native cross-link:** Sync sits directly on top of the sibling `vianium-mtproto`'s long-poll loop (`src\mtproto\`) and on the same sibling's `src\tl\` deserialization. It is the only managed context allowed to consume the raw `Updates` TL containers; it converts them into typed domain events for everyone else.
>
> **Roadmap position:** Phase 1.6 — depends on Account. Sync is the *event bus into the rest of the world* — it owns the pts/qts/seq state machine and is the canonical inbound source for every other context. Without Sync, Chats and Messages can still run from cache, but they will silently drift. Sync is therefore extracted before Calls and SecretChats so the system can be eventually-consistent end-to-end early.

---

## 1. Purpose

`Vianigram.Sync` owns the **server-truth reconciliation engine**: the loop that consumes incoming `Updates` from MTProto, applies them in pts/qts/seq order, detects gaps, and uses `updates.getDifference` / `updates.getChannelDifference` to fill them. It is the **only** context that touches the pts/qts/seq state, and the **only** context that subscribes to the raw long-poll. Every other context receives typed, ordered, gap-free domain events.

The hard line: **the pts/qts/seq state machine is the invariant.** No other context may read or write it, fetch differences, or interpret raw updates. If a test ever has to mock pts to validate something outside Sync, that's a smell — the dependency direction is wrong.

---

## 2. Aggregate root

`SyncCursor` — single root per `AccountId`. Invariants:

- Carries `(pts, qts, seq, date)` for the *common* update box plus a `Map<ChannelId, ChannelSyncState>` for per-channel cursors.
- `pts`, `qts`, `seq` are non-negative integers, monotonically advancing **except** when the server sends `updates.differenceTooLong`, which forces a *reseed* (treated atomically; see below).
- An incoming `Updates` container with `pts ≤ cursor.pts` is a duplicate and is dropped.
- An incoming `Updates` with `pts == cursor.pts + ptsCount` is in-order and applied.
- An incoming `Updates` with `pts > cursor.pts + ptsCount` is a *gap*: emit `GapDetected`, kick off `updates.getDifference`. While the gap is unresolved, *new* updates with higher pts are buffered (capped at 256) and replayed after gap fill.
- `updates.differenceTooLong` triggers a **reseed**: clear all per-context caches that depend on pts (notably Messages' coverage ranges), refetch dialogs and history from scratch.
- Per-channel cursors are independent of the common cursor; `pts_count` semantics are the same per channel.
- The cursor is **never** modified outside the Application layer's `ApplyUpdateUseCase` — every mutation is a typed command path.

There is also a non-aggregate **`UpdateBuffer`** entity that holds out-of-order updates during gap recovery. Bounded; if it overflows, we promote to `differenceTooLong`-equivalent reseed.

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `SyncCursor` | aggregate | The pts/qts/seq state. |
| `ChannelSyncState` | entity | `(channelId, pts, lastUpdateAt)`. |
| `UpdateBuffer` | entity | Out-of-order updates queued during gap recovery. |
| `LongPollSession` | entity | One in-flight long-poll RPC; restarts on disconnect or reseed. |
| `Pts` / `Qts` / `Seq` | VO struct | `int` wrappers; `Pts.Advance(by) → Pts` is the only mutation primitive. |
| `UpdateBoxKind` | VO enum | `Common`, `Secret` (qts-bound), `Channel(ChannelId)`. |
| `UpdateOriginator` | VO record | `(updateId, dateReceived, originDc, transportSessionId)` — for diagnostics, not domain state. |
| `GapKind` | VO enum | `CommonPtsGap`, `CommonQtsGap`, `CommonSeqGap`, `ChannelPtsGap`. |
| `ReseedReason` | VO enum | `DifferenceTooLong`, `ChannelDifferenceTooLong`, `BufferOverflow`, `LayerUpgrade`, `Manual`. |
| `RawUpdate` | VO sealed class | The TL `Update` boxed plus its bookkeeping fields (`pts_count`, `qts`, `date`). Internal — never escapes the context. |
| `SyncCursorSnapshot` | VO sealed | Outbound projection (used in diagnostics UI only). |

Note: this context emits *the typed domain events of every other context*. Those types (`MessageReceived`, `DialogChanged`, `UserStatusChanged`, etc.) are owned by their home contexts; Sync references them only as event types. The reverse — Sync emits an event that none of the other contexts have defined a handler for — is a configuration error caught at startup.

---

## 4. Domain events emitted

The list below mixes Sync-internal events (`Sync*`) with **re-emissions in other contexts' types**. Both are emitted on the same `IEventBus`.

**Sync-internal**

| Event | When | Payload |
|---|---|---|
| `LongPollStarted` | Long-poll RPC issued | `LongPollSession id` |
| `LongPollDisconnected` | Server or transport dropped | `Error` |
| `GapDetected` | pts/qts/seq jump detected | `GapKind`, `int from`, `int to`, `Maybe<ChannelId>` |
| `GapFilled` | `getDifference`/`getChannelDifference` applied | `GapKind`, `int filled`, `int messagesAdded`, `int dialogsTouched` |
| `ReseedRequired` | Server returned `differenceTooLong` or buffer overflowed | `ReseedReason` |
| `ReseedCompleted` | Reseed finished | `int dialogsRefreshed`, `int messagesRefreshed` |
| `CursorAdvanced` | Cursor moved forward | `int pts`, `int qts`, `int seq` |

**Re-emissions in other contexts' event types**

- `Vianigram.Messages.MessageReceived`
- `Vianigram.Messages.MessageEdited`
- `Vianigram.Messages.MessageDeleted`
- `Vianigram.Messages.ReactionsUpdated`
- `Vianigram.Messages.MessagePinned` / `MessageUnpinned`
- `Vianigram.Messages.MessageReadByPeer`
- `Vianigram.Messages.ReadHistoryReceived`
- `Vianigram.Chats.DialogChanged` (umbrella for top-message changes coming from the wire)
- `Vianigram.Contacts.UserStatusChanged`
- `Vianigram.Contacts.UserNameChanged` / `UserPhotoChanged`
- `Vianigram.Contacts.ContactsResetRequired`
- `Vianigram.Calls.CallSignal` (incoming call request, accept, discard)
- `Vianigram.SecretChats.EncryptedMessageReceived`

Sync owns the *mapping* from raw TL updates → typed domain event. The mapping is in `Application/Translators/`, one translator per `Update*` TL constructor.

---

## 5. Inbound ports

```csharp
namespace Vianigram.Sync.Ports.Inbound
{
    public interface ISyncApi
    {
        Task<Result<Unit, Error>> StartAsync();          // begin long-poll loop
        Task<Result<Unit, Error>> StopAsync();
        Task<Result<Unit, Error>> ForceDifferenceAsync(); // user-triggered "pull to refresh"-style hint
        Task<Result<Unit, Error>> ReseedAsync(ReseedReason reason);
        SyncCursorSnapshot GetCursor();
        SyncCursorSnapshot GetChannelCursor(ChannelId channel);
        SyncHealth GetHealth();
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }
}
```

`SyncHealth` is a diagnostic snapshot: `(running, lastUpdateAt, gapsInLastHour, reseedsInLastDay, longPollRtt)`. The UI may surface "syncing…" indicators from this.

---

## 6. Outbound ports

| Port | Purpose | Adapter |
|---|---|---|
| `ISyncCursorStore` | Persist `(pts, qts, seq, date)` and channel cursors. Persisted under `LocalSettings` (small, frequent writes). | `Infrastructure/Persistence/LocalSettingsCursorStore.cs` |
| `IRawUpdatesSource` | Subscribe to incoming `Updates` from `Vianigram.Core.Mtproto`. | `Infrastructure/Transport/MtprotoRawUpdatesAdapter.cs` |
| `IAuthorizedInvoker` | for `updates.getState`, `updates.getDifference`, `updates.getChannelDifference`. | injected from Account |
| `ITlSerializer` | decode raw TL Update containers. | injected |
| `IChatsObserver` | get the list of channels we care about (so channel-difference is bounded). | injected |
| `IClock`, `ILogger`, `ITelemetry` | Kernel | injected |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | Notes |
|---|---|---|
| `StartSyncCommand` | `StartSyncUseCase` | Loads cursor, calls `updates.getState` if cold, opens long-poll, applies `getDifference` if cursor advanced before disconnect. |
| `StopSyncCommand` | `StopSyncUseCase` | Gracefully cancels long-poll. |
| `ApplyUpdateCommand(rawUpdate)` | `ApplyUpdateUseCase` | The hot path. Deduplicates, orders, gap-checks, applies, emits typed event(s), persists cursor. |
| `RequestDifferenceCommand` | `RequestDifferenceUseCase` | Calls `updates.getDifference` with current cursor, applies in order, emits `GapFilled`. Idempotent — concurrent calls coalesce via a lock similar to PivoraTelegram's `_differenceLock`. |
| `RequestChannelDifferenceCommand(channel)` | `RequestChannelDifferenceUseCase` | `updates.getChannelDifference`. |
| `ReseedCommand(reason)` | `ReseedUseCase` | Emits `ReseedRequired`, refetches state, clears buffer, then emits `ReseedCompleted`. Other contexts re-fetch as appropriate. |
| `RefreshStateCommand` | `RefreshStateUseCase` | `updates.getState` only — used as health probe. |

**Queries**

| Query | Returns |
|---|---|
| `GetCursorQuery` | `SyncCursorSnapshot` |
| `GetChannelCursorQuery(channel)` | `SyncCursorSnapshot` (per-channel) |
| `GetHealthQuery` | `SyncHealth` |

**Reactive subscribers**

- `RawUpdateReactor` — listens on `IRawUpdatesSource`, dispatches to `ApplyUpdateUseCase`.
- `LifecycleReactor` — on suspend, persists cursor synchronously; on resume, calls `RequestDifferenceCommand`.
- `AccountReactor` — on `LoginCompleted` (start), `AccountLoggedOut` (stop, wipe cursor), `ActiveAccountChanged` (stop on old, start on new), `AuthKeyRotated` (force reseed because the prior cursor's session validity is gone).

---

## 8. Cross-context interactions

**Emits (re-emissions on others' event types)**

- To `Vianigram.Messages`: `MessageReceived/Edited/Deleted/Reactions/Pinned/Read*`.
- To `Vianigram.Chats`: `DialogChanged` (and indirectly via Messages events).
- To `Vianigram.Contacts`: `UserStatusChanged/UserNameChanged/UserPhotoChanged/ContactsResetRequired`.
- To `Vianigram.Calls`: `CallSignal(updatePhoneCall)`.
- To `Vianigram.SecretChats`: `EncryptedMessageReceived(updateNewEncryptedMessage)`.

Sync's emission contract is publishable: each translator function is unit-tested with golden inputs.

**Consumes**

- From `Vianigram.Account`: `LoginCompleted` (start), `AccountLoggedOut` (stop), `ActiveAccountChanged` (rebind), `AuthKeyRotated` (reseed).
- From Kernel: `NetworkConnectivityChanged` (re-arm long-poll on regain), `AppLifecycleResuming` (force `RequestDifferenceCommand`).
- From `Vianigram.Chats`: nothing direct, but Sync uses `IChatsObserver` to know which channels to track in its per-channel cursor map. New dialogs (channel joins) trigger immediate `RequestChannelDifferenceCommand` to seed.

**Capabilities:** `sync.background_pull` (gates background-task pulls), `sync.aggressive_reseed` (off by default; on flips reseed to fire on lower buffer thresholds for QA).

---

## 9. Storage strategy

**Persisted (`LocalSettings`):**

- `sync.{accountId}.pts`, `.qts`, `.seq`, `.date` — ints.
- `sync.{accountId}.channel_states` — small JSON blob mapping `channelId → (pts, lastUpdateAt)`. PivoraTelegram's `ChannelStatesKey = "sync_channel_states_v1"` is the conceptual model.
- `sync.{accountId}.last_long_poll_at` — for "time since last update" health.

`LocalSettings` is chosen over SQLite here because pts updates are *very* frequent (potentially every few seconds) and small; we don't want SQLite write amplification. Settings are atomic small writes.

**Memory-only:**

- `UpdateBuffer` — bounded queue of out-of-order updates during gap recovery (max 256 entries, ~50 KB worst case).
- `LongPollSession` state.
- The mapping channel→cursor mirrors persistence.

**Encryption needs:** None. pts/qts/seq are non-secret integers; channel cursors are channel IDs that are not particularly sensitive.

**Persistence cadence:** cursor writes are debounced 250 ms (so a burst of 50 updates writes once at the end). On `AppLifecycleSuspending`, an immediate forced flush guarantees no dropped updates after resume.

---

## 10. Performance considerations

- **Long-poll RTT:** Telegram long-poll typically returns within 0–25 s. We pin a single MTProto session for it and never share with `messages.*` or `upload.*` to avoid head-of-line blocking.
- **Hot path budget:** `ApplyUpdateUseCase` from raw TL bytes to event published on the bus — target P50 ≤ 2 ms, P99 ≤ 8 ms on 512 MB-class hardware. Most of the cost is TL deserialization; we avoid LINQ and reflection in this path.
- **Gap recovery:** `updates.getDifference` returns up to 1000 entries. We apply them in batch transactions per consumer (one SQLite tx for Messages, one for Contacts, one for Chats) to keep tail latency bounded.
- **Buffer behavior:** while gap is unresolved, new updates beyond the gap are buffered up to 256. Beyond that, we promote to reseed. This corresponds to PivoraTelegram's empirical observation that very busy supergroups can produce buffer pressure during reconnect.
- **Lock discipline:** PivoraTelegram's `_differenceLock`, `_stateLock`, `_channelStateLock` are preserved as separate concerns. We use a single `SemaphoreSlim` per concern and never hold two at once.
- **Reseed cost:** a reseed forces re-fetch of dialog list (one `getDialogs`) plus per-active-channel `channels.getFullChannel`. Total wall-clock ≤ 4 s on Wi-Fi for a typical user with 50 dialogs. Acceptable as a rare event.
- **Battery:** when the app is suspended and a `BackgroundTaskTrigger` runs us, we issue exactly one `getDifference` then exit; we do not re-open long-poll in background. PivoraTelegram already had this discipline; we preserve it.
- **Telemetry overhead:** the gap counter and reseed counter must update without locks in the hot path; we use `Interlocked` increments.

---

## 11. Telegram MTProto methods used

| Method | Purpose |
|---|---|
| `updates.getState` | Initial cursor seed on login or reseed. |
| `updates.getDifference` | Fill the common-box gap. Returns `(messages, encryptedMessages, otherUpdates, chats, users, state)`. |
| `updates.getChannelDifference` | Fill a channel-box gap. Per-channel. |
| (long-poll) `updates.*` containers | Pushed by the server on the persistent connection — these are not RPCs we issue, they are inbound dispatches. Translators in `Application/Translators/` map each TL constructor to typed events. |
| `help.getConfig` | Keep DC list fresh; not strictly Sync's job but the long-poll loop notices `CONNECTION_NOT_INITED` and triggers a `help.getConfig` indirectly through Account's transport. |
| (No `messages.*` calls.) | Sync explicitly does **not** call `messages.getDialogs` or `messages.getHistory`. Instead, on reseed it emits `ReseedRequired` and Chats / Messages refetch from their own use cases. This separation of concerns prevents cyclic re-entry. |

---

## 12. Open questions / future work

1. **Multi-account simultaneous long-polls.** In the current design we pause inactive accounts' long-polls and only run one. If we move to "all accounts always live" we need N long-polls and N Sync aggregates. The aggregate is already per-account, so it's a deployment-layer decision; the bounded context model is unaffected.
2. **Persisting buffered updates across app suspension.** Today, if we suspend mid-gap-recovery, the buffered updates are dropped (we'll re-getDifference on resume anyway). This is correct by construction but wastes work. An open question: persist the buffer? Probably not worth the complexity.
3. **Translator maintenance.** Telegram's TL schema bumps layer numbers regularly (158, 167, 174, …). Each layer adds new `Update*` constructors. We have a CI check that enumerates `TL_constructor → translator function` and fails if any new constructor lacks a translator (even a no-op). Tracked in the build.
4. **Channel forum threads.** When forum topics arrive, channel-difference must page within a topic. The current `ChannelSyncState` carries only one `pts` — extending to per-topic pts is open, blocked on Telegram's API stabilization in this area.
5. **Reference notes from PivoraTelegram.** `Sync/SyncEngine.cs` and `Client/UpdatesHandler.cs` are the direct ancestors. Pivora handles `_differenceTask` as an explicit field; we replace with a `SemaphoreSlim`-guarded coalesced use case. The TL constructor `0x2064674e` for channel difference is the same. The persisted keys (`sync_pts`, `sync_qts`, `sync_date`, `sync_seq`, `sync_channel_states_v1`) carry over as `sync.{accountId}.*` for multi-account scoping.
6. **Backoff strategy on long-poll errors.** Today, exponential backoff with jitter capped at 60 s. We may want to integrate with Kernel's `INetworkConnectivity` more tightly so that on `Disconnected → Connected`, we restart immediately (skip backoff). Hooks exist; tuning tracked in v1.1.
7. **Telemetry.** `SyncTelemetryEmitter` emits `sync.long_poll.rtt_ms` (histogram), `sync.gaps.detected` (counter, labeled by `GapKind`), `sync.reseeds` (counter, labeled by `ReseedReason`), `sync.buffer.depth` (gauge), `sync.cursor.pts` (gauge for diagnostics).
