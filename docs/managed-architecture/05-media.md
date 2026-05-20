# Vianigram.Media — Media Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md), [01-account.md](01-account.md). DDD + hex + managed Kernel + standard C# patterns. Kernel concepts come from `Vianigram.Kernel`.
>
> **Native cross-link:** all media bytes flow through `Vianigram.Core.Media` (Opus encode/decode, WebP decode, JPEG thumbnail extract, animated WebP stickers). Connection pooling and chunk pacing reuse the patterns established by `Vianium.Core.Http`. Same DDD+hex skeleton, different technology.
>
> **Roadmap position:** Phase 1.5 — depends on Account. Heavy I/O context, the most performance-sensitive of the eight on a 512 MB device (limited RAM, slow flash, weak network). It is the only context that *legitimately* holds large byte buffers, so its memory and disk discipline is enforced more strictly than elsewhere.

---

## 1. Purpose

`Vianigram.Media` owns **all media transfer**: photo, video, voice (Opus), round video, document, sticker (static and animated), and animated GIF (MP4). It is the only context that touches the `upload.*` MTProto family and the only one that writes binary blobs to `LocalFolder`. It exposes opaque `MediaRef` handles to peers (Messages, Chats, Contacts) that resolve to either a local file path (cached) or a download-on-demand placeholder.

It also owns the **gallery cache** policy: how much disk we let media consume, what gets evicted, and how the LRU is maintained across app sessions.

The hard line: **bytes only enter and leave through this context.** Messages knows there's a "video referenced by this message"; Calls knows "the dial-tone is a media asset"; nobody else allocates a `byte[]` for media.

---

## 2. Aggregate root

`MediaTransfer` — one root per active transfer (upload or download). Identity = `TransferId(Guid)`. Invariants:

- A transfer is in exactly one state: `Queued`, `Active`, `Paused`, `Completed`, `Failed`, `Cancelled`. Transitions are explicit.
- An upload commits to `MediaRef` only after **all parts are saved** and `messages.send*Media` has consumed it; if abandoned, the server-side parts go stale and are GCed by Telegram.
- Chunk count = `ceil(size / chunkSize)`; chunk indices are 0-based; `chunkSize` is fixed for the lifetime of the transfer (selected at start time based on `size`).
- Concurrent in-flight chunks per transfer ∈ `[1, 8]`; tuned by `IConnectionPoolMonitor`.
- For uploads: every saved part is durably persisted as `(file_id, part_index)` in SQLite — losing it means re-uploading from scratch on next launch.
- A `MediaTransfer` references a `MediaAsset`, the destination/source identity. Asset survives transfers; transfer is ephemeral.

There is also a **`MediaCache`** aggregate (one per `AccountId`, scoped) that owns the LRU policy across all completed `MediaAsset` blobs. Eviction emits `MediaAssetEvicted`.

---

## 3. Domain entities and value objects

| Type | Kind | Description |
|---|---|---|
| `MediaTransfer` | aggregate | One in-flight upload or download. |
| `MediaCache` | aggregate | Per-account LRU/quota manager for completed assets. |
| `MediaAsset` | entity | Identity = `(MediaKind, FileLocation)`. Has size, mime, optional thumb, on-disk path once cached. |
| `Chunk` | entity | One sub-range of a transfer; identity = `(TransferId, ChunkIndex)`. |
| `Thumbnail` | entity | A small precomputed image (JPEG progressive header) Telegram inlines in TL. |
| `TransferId` | VO struct | `Guid`, opaque. |
| `MediaKind` | VO enum | `Photo`, `Video`, `Voice`, `RoundVideo`, `Document`, `StickerStatic`, `StickerAnimated`, `Gif`. |
| `MediaRef` | VO record | `(kind, location, accessHash, dcId, size, mimeType, thumbRef?)` — opaque handle exposed across contexts. |
| `FileLocation` | VO union | `Photo(volumeId, localId, secret)` | `Document(id, accessHash, fileReference)` | `EncryptedFile(id, accessHash)` | `WebPage(url, hash)`. |
| `ChunkSize` | VO struct | Power-of-two ∈ `{64KB, 128KB, 256KB, 512KB, 1MB}`. |
| `TransferState` | VO enum | `Queued`, `Active`, `Paused`, `Completed`, `Failed`, `Cancelled`. |
| `TransferProgress` | VO record | `(bytesDone, bytesTotal, chunksDone, chunksTotal, ratePerSec)`. |
| `Md5Sum` | VO struct | 16-byte hash; required by `upload.saveFilePart` for small files. |
| `CdnRedirect` | VO record | When a download is redirected to CDN: `(cdnDcId, fileToken, encryptionKey, encryptionIv)`. |
| `MediaAssetSnapshot` / `TransferSnapshot` | VO sealed | Outbound projections. |

---

## 4. Domain events emitted

| Event | When | Payload |
|---|---|---|
| `TransferQueued` | `EnqueueDownload` / `EnqueueUpload` returns | `TransferSnapshot` |
| `TransferStarted` | Dispatcher promotes `Queued → Active` | `TransferId` |
| `TransferProgress` | Throttled to 4 Hz max per transfer | `TransferId`, `TransferProgress` |
| `TransferPaused` | User-paused or backoff | `TransferId`, `PauseReason` |
| `TransferResumed` | Returning from pause | `TransferId` |
| `TransferCompleted` | All chunks settled, file finalized on disk | `TransferId`, `MediaAssetSnapshot` |
| `TransferFailed` | Terminal error after retries exhausted | `TransferId`, `Error` |
| `TransferCancelled` | User cancel | `TransferId` |
| `MediaAssetCached` | A new asset hit the cache | `MediaAssetSnapshot` |
| `MediaAssetEvicted` | LRU evicted an asset | `MediaAssetSnapshot`, `EvictionReason` |
| `CacheQuotaChanged` | Quota changed (settings or device pressure) | `long bytesQuota`, `long bytesUsed` |
| `FloodWaitOnTransfer` | Server returned `FLOOD_WAIT_X` on `upload.*` | `TransferId`, `int seconds` |

---

## 5. Inbound ports

```csharp
namespace Vianigram.Media.Ports.Inbound
{
    public interface IMediaApi
    {
        Task<Result<TransferId, Error>> EnqueueUploadAsync(MediaKind kind, IInputBlobSource source, UploadOptions options);
        Task<Result<TransferId, Error>> EnqueueDownloadAsync(MediaRef media, DownloadPriority priority);
        Task<Result<Unit, Error>> CancelAsync(TransferId id);
        Task<Result<Unit, Error>> PauseAsync(TransferId id);
        Task<Result<Unit, Error>> ResumeAsync(TransferId id);
        Maybe<TransferSnapshot> GetTransfer(TransferId id);
        IReadOnlyList<TransferSnapshot> ListActiveTransfers();
        Task<Result<MediaAssetSnapshot, Error>> ResolveAsync(MediaRef media, ResolveMode mode);  // CacheOnly | CacheOrFetch
        Maybe<string> GetCachedPath(MediaRef media);
        Task<Result<Unit, Error>> PrefetchThumbnailAsync(MediaRef media);
        Task<Result<Unit, Error>> EvictAssetAsync(FileLocation loc);
        CacheStatsSnapshot GetCacheStats();
        SubscriptionToken Subscribe<TEvent>(Action<TEvent> handler) where TEvent : DomainEventBase;
    }
}
```

`IInputBlobSource` is an abstraction over device file pickers, photo capture, and "share-into-app" sources.

---

## 6. Outbound ports

| Port | Purpose | Adapter |
|---|---|---|
| `IBlobStore` | Read/write large files in `LocalFolder/media/` with atomic rename. | `Infrastructure/Storage/LocalFolderBlobStore.cs` |
| `ITransferLog` | SQLite metadata (`transfers`, `chunks_progress`, `assets`). | `Infrastructure/Persistence/SqliteTransferLog.cs` |
| `IConnectionPoolMonitor` | Get current concurrency budget across contexts. | `Infrastructure/Net/ConnectionPoolMonitor.cs` |
| `IAuthorizedInvoker` | from `Vianigram.Account` — but Media uses a *parallel* fleet of secondary `media`-only sockets registered under the same auth_key. | injected |
| `IMediaCodecPort` | Bridge to `Vianigram.Core.Media`: thumbnail extract, Opus decode for voice playback, WebP decode, animated WebP frame extraction, MP4 demux for GIF. | `Infrastructure/Codecs/NativeMediaCodecAdapter.cs` |
| `IDeviceStoragePort` | Query free disk; surface low-storage signals. | `Infrastructure/Device/StorageMonitor.cs` |
| `IBackgroundTransferPort` | OS background download API for very large files when app is suspended. | `Infrastructure/BgTransfer/WinBackgroundTransfer.cs` |
| `IMd5Hasher` | Stream-friendly MD5 (incremental). | `Infrastructure/Hash/IncrementalMd5.cs` |

---

## 7. Application use cases / commands

**Commands**

| Command | Use case | Notes |
|---|---|---|
| `EnqueueUploadCommand(source, options)` | `EnqueueUploadUseCase` | Picks `chunkSize`, decides `saveFilePart` vs `saveBigFilePart` (cutoff at 10 MiB). |
| `EnqueueDownloadCommand(media, priority)` | `EnqueueDownloadUseCase` | Cache-first: returns synchronously if `ResolveMode.CacheOnly` and present. |
| `CancelTransferCommand(id)` | `CancelTransferUseCase` | Frees chunks, marks dispatcher to skip. |
| `PauseTransferCommand` / `ResumeTransferCommand` | `PauseResumeUseCase` | Survives app launches. |
| `EvictAssetCommand` | `EvictAssetUseCase` | Manual or LRU-driven. |
| `RefreshCacheStatsCommand` | `RefreshCacheStatsUseCase` | Recomputes `bytesUsed`. |
| `SetCacheQuotaCommand` | `SetCacheQuotaUseCase` | User-set quota. |

**Queries**

| Query | Returns |
|---|---|
| `GetTransferQuery(id)` | `Maybe<TransferSnapshot>` |
| `ListActiveTransfersQuery` | `IReadOnlyList<TransferSnapshot>` |
| `ResolveCachedQuery(media)` | `Maybe<string path>` (sync) |
| `GetCacheStatsQuery` | `CacheStatsSnapshot(bytesUsed, assetCount, oldestAccessAt, quota)` |

**Reactive subscribers**

- `TransferDispatcher` — pulls queued transfers, schedules chunks, applies adaptive concurrency.
- `LruEvictor` — runs when `bytesUsed > quota * 0.95`, evicts oldest until below `quota * 0.85`.
- `LowStorageReactor` — listens to `IDeviceStoragePort` signals and triggers aggressive eviction when device free disk < 100 MiB.
- `LifecycleReactor` — on suspend, persists in-flight progress and hands long downloads to `IBackgroundTransferPort` if supported.

---

## 8. Cross-context interactions

**Emits**

- `TransferProgress`, `TransferCompleted`, `TransferFailed` → consumed by Messages (bind to outbound `OutboxEntry`) and Presentation (UI progress chips).
- `MediaAssetCached` → consumed by Chats (refresh dialog avatar/preview) and Contacts (refresh user photo).
- `MediaAssetEvicted` → consumed by Presentation only (UI may need to drop ImageSource references).

**Consumes**

- From `Vianigram.Messages`: `MessageSendingMedia(peer, clientId, mediaRef)` — pre-binds the upload to the optimistic message so progress is visible.
- From `Vianigram.Account`: `LoginCompleted`, `ActiveAccountChanged`, `AccountLoggedOut` (wipe per-account `media/` folder *unless* user opts to keep, see open questions).
- From Kernel: `NetworkConnectivityChanged`, `MemoryPressureHigh`, `BatterySaverChanged` — the dispatcher pauses non-essential downloads under pressure.

**Capabilities:** `media.parallel_chunks` (default on; off on very low-end), `media.cdn` (gates `upload.getCdnFile` path), `media.background_transfer` (gates use of OS API).

---

## 9. Storage strategy

**SQLite (`{LocalFolder}/accounts/{accountId}/media.db`):**

- `transfers(transfer_id PK, kind, direction, state, asset_kind, file_location_blob, size, chunk_size, started_at, completed_at, error, paused_reason)`
- `chunks_progress(transfer_id, chunk_index, status, bytes, attempts, last_error, PRIMARY KEY(transfer_id, chunk_index))`
- `assets(file_loc_kind, file_loc_blob, mime, size, on_disk_path, thumb_path, last_accessed_at, cached_at, PRIMARY KEY(file_loc_kind, file_loc_blob))`

Indexes: `(state)` on transfers for dispatcher queries; `(last_accessed_at)` on assets for LRU.

**Filesystem (`{LocalFolder}/accounts/{accountId}/media/`):**

- Subdirectories by `MediaKind` (`photos/`, `videos/`, `voice/`, etc.) to keep enumeration cheap.
- Filenames are `{file_loc_hash}.bin` plus `{file_loc_hash}.thumb.jpg` for thumbnails.
- Atomic write: `*.partial` → fsync → rename. Crash-safety guarantee: an asset listed in `assets` table either exists fully on disk or doesn't exist (no half-written files visible).

**Memory-only:**

- Active transfer chunk buffers. Each ≤ 1 MiB; capped at 8 chunks × 4 transfers = 32 MiB worst case (way too high for a 512 MB device; the dispatcher actually caps total in-flight bytes at 4 MiB and reduces concurrency to fit).
- Decoded thumbnail bitmaps (UI ImageSource) — those are the UI's problem, we just hand back paths.

**Encryption needs:** None at rest for normal-chat media (OS-level only). Secret-chat media is processed differently — see `08-secret-chats.md`. Voice messages and stickers are not specially treated.

**MD5 verification:** required by Telegram for `upload.saveFilePart` (small files); we maintain incremental MD5 across chunks. For downloads, file hash is verified opportunistically against `Document.id`-paired hash where present.

**File-reference rotation:** Telegram's `file_reference` for documents expires (~24h). On `FILE_REFERENCE_EXPIRED`, we re-resolve from the parent message via `messages.getMessages` (out-of-band) and retry.

---

## 10. Performance considerations

- **Parallel chunks:** default 4; bumped to 8 on Wi-Fi when bandwidth probe shows ≥ 2 Mbps; reduced to 2 on 3G; 1 on 2G or `BatterySaver`. The probe is a 256 KB warmup chunk; result cached for 10 min per network.
- **Adaptive chunk size:** start at 64 KiB; doubles up to 1 MiB if RTT-adjusted throughput improves; halves on `FLOOD_WAIT` or repeated timeouts.
- **FLOOD_WAIT respect:** any `upload.*` reply with `FLOOD_WAIT_X` triggers a per-method timer; *all* uploads wait, not just the offending transfer. Downloads are independent.
- **Connection reuse:** Media uses dedicated MTProto sessions on the data center's `media` sub-DC where available, leaving the main session free for `messages.*`. Sessions are pooled (max 4 per DC).
- **Disk I/O:** writes are sequential per chunk, `FlushAsync` only at `*.partial → final` rename time. SSD-style randomness is avoided.
- **Voice playback latency:** Opus decode happens in `Vianigram.Core.Media`; managed code only orchestrates. Target: 80 ms from tap to audio for a fully-cached voice message.
- **Thumbnail-first display:** photos render the inline TL `Thumbnail` immediately (already in the `MessageRef`), kick off the full-resolution download in background. The UI swaps when ready.
- **Cache quota defaults:** 200 MB on devices with < 4 GB total storage, 1 GB otherwise. User-overridable.
- **Eviction batching:** LRU runs at most every 30 s, evicts in batches of 50 to amortize SQLite write overhead.
- **Background transfer:** very large videos use `IBackgroundTransferPort` so they can complete while the app is suspended; on resume we reconcile progress.
- **Hash check cost:** incremental MD5 adds ~5% CPU during upload; negligible.

---

## 11. Telegram MTProto methods used

| Method | Purpose |
|---|---|
| `upload.saveFilePart` | Save one chunk of a small (< 10 MiB) file. |
| `upload.saveBigFilePart` | Save one chunk of a large (≥ 10 MiB) file with explicit total parts. |
| `upload.getFile` | Download one chunk. |
| `upload.getCdnFile` | Download from CDN after server `redirect`. |
| `upload.reuploadCdnFile` | Re-upload a missing piece to CDN when CDN says `cdnFileReuploadNeeded`. |
| `upload.getCdnFileHashes` | Get integrity hashes for CDN downloads. |
| `upload.getFileHashes` | Verify integrity of regular downloads when supported. |
| `upload.getWebFile` | Download web-attached files (link previews). |
| `messages.uploadMedia` | Upload media for forwarding without sending (used in scheduled paths). |
| `messages.uploadEncryptedFile` | Encrypted-file upload for secret chats (called *via* SecretChats' delegating port). |
| `messages.getDocumentByHash` | Server-side dedup: avoid re-uploading a document we've already seen. |
| `phone.getCallConfig` | Indirect — used by `Vianigram.Calls`, but media-plane RTP setup uses our chunking infrastructure for capture buffers. |

> Note: `upload.*` methods *never* travel on the same MTProto session as `messages.*` for big transfers; the Account-provided `IAuthorizedInvoker` is wrapped by an internal `MediaInvoker` that picks media sub-sessions transparently.

---

## 12. Open questions / future work

1. **Cross-device dedup.** When the user sends the same photo twice, server-side it has the same `file_id`. We could dedup at upload time via `messages.getDocumentByHash` to avoid re-uploading. Today we skip; will revisit when telemetry shows non-trivial duplicate uploads.
2. **Per-dialog cache pinning.** The user may want "always keep this channel's media offline." We don't model that yet — requires a `pinned_in_cache` column on `assets` and a separate eviction tier.
3. **Sticker pack lifecycle.** Stickers are `MediaKind.StickerStatic/Animated` for transfer purposes, but a *sticker pack* is a higher-level object owned by a future `Vianigram.Stickers` context. Today, individual stickers cache here and the future pack context will hold the `setId` → `[stickerRef]` mapping.
4. **Animated WebP performance.** WP8.1 has no out-of-the-box WebP animation pipeline; we decode in `Vianigram.Core.Media` and surface frames at intervals. CPU cost is real — large animated stickers may hit 8% on a 512 MB ARMv7 device. Open question: cap frame rate or pre-render to a small atlas?
5. **Voice message waveform.** We store the server-provided 100-byte waveform in `assets.thumb_path`'s sibling JSON; rendering is the UI's concern. We do not regenerate locally.
6. **Reuse of `Vianium.Core.Http` patterns.** The browser's connection pool (per-host limits, idle reaper, cookie-jar isolation) inspires the design here; we adapt to MTProto sessions which are stickier than HTTP/1.1 keep-alives.
7. **Background-task disk pressure.** When `IBackgroundTransferPort` runs while app is suspended, it can fill the cache past the quota. We tolerate ≤ 10% overshoot; the next foreground LRU pass corrects it.
8. **Reference notes from PivoraTelegram.** `Client/FileManager.cs` and `Client/DownloadManager.cs` are the closest analogues. They mix file picker logic with chunk dispatch and share a global `_lock`. We separate transfer state from blob I/O, hoist the `MD5Hasher` into a port, and split the "find a free slot" logic into the explicit dispatcher.
9. **Telemetry.** `MediaTelemetryEmitter` emits `media.transfer.completed{kind,direction}`, histograms for `media.upload.throughput_kbps` and `media.download.first_byte_ms`, gauges for `media.cache.bytes_used`, `media.dispatcher.in_flight_chunks`, and counters for `media.flood_wait{method}`.
