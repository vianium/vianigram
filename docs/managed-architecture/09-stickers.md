# Vianigram.Stickers — Sticker Library Bounded Context

> **Required prior reading:** [principles.md](principles.md) and [00-overview.md](00-overview.md). This doc assumes DDD + hexagonal + managed Kernel + the 6 non-negotiable rules, as well as `Result<T, Error>` and C# WP8.1 patterns (no `record`, no `nullable` annotations). The goal is to cleanly port PivoraTelegram's sticker subsystem to its own bounded context, with segregated storage and reuse of the `Vianigram.Core.Media` context for WebP/TGS decode.

---

## 1. Bounded context

- **Ubiquitous language:** sticker, sticker pack (set), pack metadata, pack thumbnail, recently used, favorites, animated sticker (TGS), Lottie animation, emoji shortcode, install, uninstall, archived pack, featured pack, sticker search, mask sticker, custom emoji.
- **Aggregate root:** `StickerLibrary` — the complete set of the active user's installed packs + the "recently used" list (the last 20 stickers used) + the favorites list (faved stickers). One per authenticated Telegram account.
- **Secondary entities:**
  - `StickerSet` — an installed pack, with metadata (id, short name, title, count, hash, archived, animated/video flag) and the collection of `Sticker` entities that compose it.
  - `Sticker` — an individual sticker within a pack: `(StickerId, FileReference, Emoji, Width, Height, IsAnimated, IsVideo, IsMask, ThumbBytes)`.
- **Value objects:**
  - `StickerSetId` — `long`. A stable ID assigned by Telegram.
  - `StickerSetShortName` — `string` unique per pack (`"AnimatedEmojies"`, `"HotCherry"`).
  - `StickerSetHash` — `long`. Telegram uses it for `not_modified` checks.
  - `StickerId` — `long`. The ID of the document (file).
  - `StickerKind` — enum: `Static` (WebP), `Animated` (TGS / compressed Lottie), `Video` (WebM, rare on WP).
  - `EmojiShortcode` — `string`. The emoji associated with the sticker (used to type an emoji and get suggested stickers).
  - `RecentlyUsedEntry` — `(StickerId, DateTime LastUsedUtc, int UseCount)`.
  - `FavoriteEntry` — `(StickerId, DateTime FavedAtUtc)`.
  - `PackVisibility` — enum: `Installed`, `Archived`, `Featured` (a catalog recommended by Telegram, not installed yet).
- **Domain events emitted:**
  - `StickerSetInstalled(StickerSetId, StickerSetShortName, int stickerCount)`.
  - `StickerSetUninstalled(StickerSetId)`.
  - `StickerSetArchived(StickerSetId)` / `StickerSetUnarchived(StickerSetId)`.
  - `StickerSent(StickerId, StickerSetId, DateTime utc)` — fired when the user sends a sticker; consumed by the "recently used" component.
  - `StickerFavorited(StickerId)` / `StickerUnfavorited(StickerId)`.
  - `StickerLibrarySynced(int packsAdded, int packsRemoved, DateTime utc)` — on completing a `messages.getAllStickers` with changes.
  - `StickerPackContentLoaded(StickerSetId, int count)` — lazy load completed.
  - `StickerSearchCompleted(string query, int resultCount, TimeSpan elapsed)`.
- **Capabilities exposed:**
  - `stickers.animated` — enables TGS decode (compressed Lottie). If the device cannot handle it, off → only static thumbs are shown.
  - `stickers.video` — WebM stickers; off by default on WP (we do not support WebM demux in V1).
  - `stickers.search_global` — enables `messages.searchStickerSets` for discovery.
  - `stickers.faved` — enables the favorites section.
  - `stickers.recently_used` — enables the MRU list.
  - `stickers.custom_emoji` — a premium feature; off in V1 until it is prioritized.

Stickers is a **storage-heavy + compute-medium** context: large blobs (a pack can occupy 2–5 MB on disk) and costly animated decoding (TGS = gzipped JSON + Lottie player). That is why it isolates storage in its own folder and delegates decoding to `Vianigram.Core.Media`.

---

## 2. Goal

Replace `PivoraTelegram.App/Services/StickerService.cs` (a ~600-line god-class that mixes TL fetching, JSON disk cache, an in-memory dictionary, UI thumbnail decode, and MRU tracking) with a context with DDD + hexagonal:

- **The `StickerLibrary` aggregate** owns packs + MRU + favorites, with invariants (a sticker cannot be in the MRU twice; favorites cap 100; MRU cap 20; archived implies not in the active list).
- **Segregated storage**: a SQLite metadata table for packs/stickers (fast lookup by id, the query "stickers with emoji X") + LocalFolder/`stickers/{packId}/{stickerId}.bin` for blobs. Never load all the blobs into memory.
- **Lazy load** of a pack's content: installing a pack writes the metadata and the thumb of the first sticker; the full content is downloaded when the user opens that pack's sticker panel for the first time.
- **Progressive preload**: when the user is on `ChatPage` with the sticker panel closed, preload the first 10 stickers of the most recent pack (the top of the MRU). The rest on-demand.
- **Decoupling from Media**: Stickers does NOT know `Lottie` or `WebP`; it delegates via the `IStickerDecoder` outbound port that `Vianigram.Composition` wires to `Vianigram.Core.Media`.
- **Per-account isolation**: if the user has multiple accounts (`AccountSwitcherPage`), each account has its own `StickerLibrary` aggregate and its own folder. The account switch fires `AccountSwitched` in another context and this aggregate is reconstituted from disk.
- **Global search**: `messages.searchStickerSets` with a 300ms debounce, paginated results; install from results goes through `messages.installStickerSet`.

---

## 3. C# baseline (PivoraTelegram)

`PivoraTelegram.App/Services/StickerService.cs` today:

1. **A singleton** via `StickerService.Instance`.
2. **A `Dictionary<long, StickerSetData>` in memory** that loads EVERYTHING at startup from `Stickers/sticker-cache.json` (a single JSON file with all the packs and all the blobs in base64 — it becomes huge and blocks startup by ~800 ms on a 512 MB device).
3. **Direct TL fetching** from the same class: `_client.SendRequestAsync(new MessagesGetAllStickers { Hash = _localHash })` — violates the anti-corruption layer (StickerService knows TL).
4. **Inline TGS decoding**: uses `gzip` + `JsonObject.Parse` and a C# Lottie player that runs on the UI thread → notorious janks when opening the sticker panel.
5. **A blockable MRU list**: a `List<long>` with `Add` + `RemoveAt(0)` on each send → a full UI repaint.
6. **No archived/installed separation**; archived stickers consume memory the same as installed ones.

Pathologies:

- Startup penalty: 800 ms loading base64 blobs that probably will never be used.
- Memory: ~30 MB of blobs in RAM even if the user does not open the panel.
- No short `not_modified`: the hash is sent but the result is fully rewritten every session.
- Coupling to TL: the day `MTProto` is replaced or the TL schema is changed, this class explodes.
- The UI blocked on TGS decode (~50–150 ms for the first frame of the first animated sticker).

---

## 4. Native target — the `Vianigram.Stickers` project

```
Core/Vianigram.Stickers/
├── Vianigram.Stickers.csproj                  (WP8.1, NETFX_CORE, WINDOWS_PHONE_APP)
├── Properties/AssemblyInfo.cs
│
├── Domain/                                     ← pure, only BCL + Vianigram.Kernel
│   ├── ValueObjects/
│   │   ├── StickerSetId.cs
│   │   ├── StickerSetShortName.cs
│   │   ├── StickerSetHash.cs
│   │   ├── StickerId.cs
│   │   ├── StickerKind.cs
│   │   ├── EmojiShortcode.cs
│   │   ├── PackVisibility.cs
│   │   ├── RecentlyUsedEntry.cs
│   │   └── FavoriteEntry.cs
│   ├── Entities/
│   │   ├── Sticker.cs
│   │   └── StickerSet.cs
│   ├── Aggregates/
│   │   └── StickerLibrary.cs                  (root: packs + MRU + favorites)
│   ├── Events/
│   │   ├── StickerSetInstalled.cs
│   │   ├── StickerSetUninstalled.cs
│   │   ├── StickerSetArchived.cs
│   │   ├── StickerSetUnarchived.cs
│   │   ├── StickerSent.cs
│   │   ├── StickerFavorited.cs
│   │   ├── StickerUnfavorited.cs
│   │   ├── StickerLibrarySynced.cs
│   │   ├── StickerPackContentLoaded.cs
│   │   └── StickerSearchCompleted.cs
│   ├── Services/
│   │   ├── MruRanker.cs                       (how the MRU list is ordered: recency + count)
│   │   ├── PackInvariants.cs                  (max 200 installed packs; max 120 stickers/pack)
│   │   └── DecodeBudget.cs                    (per-session cap of TGS frames decoded)
│   ├── Policies/
│   │   ├── MruLimitsPolicy.cs                 (cap 20)
│   │   ├── FavoritesLimitsPolicy.cs           (cap 100)
│   │   └── PackInstallLimitsPolicy.cs         (cap 200 packs)
│   └── Errors/
│       └── StickerErrors.cs                   (PackNotFound, StickerNotFound, BlobMissing, DecodeFailed, QuotaExceeded)
│
├── Application/
│   ├── Commands/
│   │   ├── InstallStickerSetCommand.cs
│   │   ├── UninstallStickerSetCommand.cs
│   │   ├── ArchiveStickerSetCommand.cs
│   │   ├── SendStickerCommand.cs              (registers MRU + emits StickerSent)
│   │   ├── FavoriteStickerCommand.cs
│   │   ├── UnfavoriteStickerCommand.cs
│   │   └── SearchStickerSetsCommand.cs
│   ├── Queries/
│   │   ├── ListInstalledPacksQuery.cs
│   │   ├── GetPackContentQuery.cs
│   │   ├── ListRecentlyUsedQuery.cs
│   │   ├── ListFavoritesQuery.cs
│   │   ├── GetStickerByEmojiQuery.cs
│   │   └── GetStickerBlobQuery.cs
│   ├── UseCases/
│   │   ├── SyncStickerLibraryUseCase.cs       (messages.getAllStickers + diff)
│   │   ├── InstallStickerSetUseCase.cs
│   │   ├── UninstallStickerSetUseCase.cs
│   │   ├── ArchiveStickerSetUseCase.cs
│   │   ├── SendStickerUseCase.cs
│   │   ├── FavoriteStickerUseCase.cs
│   │   ├── LoadPackContentUseCase.cs          (lazy: pull stickers + thumbs)
│   │   ├── SearchStickerSetsUseCase.cs
│   │   └── PreloadActivePackUseCase.cs
│   └── Internal/
│       ├── PackContentLoader.cs               (parallelizes the blob fetch with a cap)
│       └── StickerLibraryDiffer.cs            (computes added/removed between snapshots)
│
├── Ports/
│   ├── Inbound/
│   │   └── IStickersApi.cs
│   └── Outbound/
│       ├── IStickerTlGateway.cs               (ACL towards the sibling vianium-mtproto src\tl\)
│       ├── IStickerBlobStore.cs               (LocalFolder/stickers/...)
│       ├── IStickerMetadataStore.cs           (SQLite)
│       ├── IStickerDecoder.cs                 (delegates to Vianigram.Core.Media: WebP + TGS)
│       └── IClock.cs                          (re-export from the Kernel, MRU timestamps)
│
├── Infrastructure/
│   ├── Tl/
│   │   ├── TlStickerGateway.cs                (impl IStickerTlGateway)
│   │   └── TlStickerMappers.cs                (TL DTOs ↔ Domain VOs)
│   ├── Persistence/
│   │   ├── SqliteStickerMetadataStore.cs      (impl IStickerMetadataStore)
│   │   ├── LocalFolderStickerBlobStore.cs     (impl IStickerBlobStore; a folder per pack)
│   │   └── StickerLibrarySnapshot.cs          (snapshot/restore DTO)
│   └── Decoding/
│       └── MediaPortStickerDecoder.cs         (delegates to Vianigram.Core.Media)
│
└── Api/
    └── V1/
        ├── IStickersApi.cs
        ├── PackDto.cs
        ├── StickerDto.cs
        ├── InstallPackRequest.cs
        ├── SearchPacksRequest.cs
        ├── SearchPacksResponse.cs
        ├── RecentlyUsedDto.cs
        └── StickerApiErrors.cs
```

**Project references of the .csproj:**
- `Vianigram.Kernel` (always).
- BCL.
- ❌ Zero `Vianigram.<OtherContext>` (this includes the sibling `vianium-mtproto` (`src\tl\`), `Vianigram.Core.Media`). These are wired via adapters in `Vianigram.Composition`.

---

## 5. Inbound port — `IStickersApi`

```csharp
namespace Vianigram.Stickers.Api.V1
{
    public interface IStickersApi
    {
        Task<Result<IReadOnlyList<PackDto>, Error>> ListInstalledAsync(CancellationToken ct);
        Task<Result<PackDto, Error>> GetPackAsync(long stickerSetId, CancellationToken ct);
        Task<Result<IReadOnlyList<StickerDto>, Error>> GetPackContentAsync(long stickerSetId, CancellationToken ct);
        Task<Result<IReadOnlyList<StickerDto>, Error>> GetRecentlyUsedAsync(int max, CancellationToken ct);
        Task<Result<IReadOnlyList<StickerDto>, Error>> GetFavoritesAsync(CancellationToken ct);
        Task<Result<IReadOnlyList<StickerDto>, Error>> GetByEmojiAsync(string emoji, CancellationToken ct);

        Task<Result<bool, Error>> InstallAsync(string shortName, CancellationToken ct);
        Task<Result<bool, Error>> UninstallAsync(long stickerSetId, CancellationToken ct);
        Task<Result<bool, Error>> ArchiveAsync(long stickerSetId, CancellationToken ct);

        Task<Result<bool, Error>> SendAsync(long stickerId, long stickerSetId, CancellationToken ct);
        Task<Result<bool, Error>> FavoriteAsync(long stickerId, CancellationToken ct);
        Task<Result<bool, Error>> UnfavoriteAsync(long stickerId, CancellationToken ct);

        Task<Result<SearchPacksResponse, Error>> SearchPacksAsync(string query, int max, CancellationToken ct);
        Task<Result<bool, Error>> SyncAsync(CancellationToken ct);
    }
}
```

---

## 6. Outbound ports

### `IStickerTlGateway`

A complete ACL towards the TL schema. Stickers does not know `MessagesGetAllStickers` or `InputStickerSetShortName`; it passes primitives.

```csharp
namespace Vianigram.Stickers.Ports.Outbound
{
    public interface IStickerTlGateway
    {
        Task<Result<TlAllStickersSnapshot, Error>> GetAllStickersAsync(long currentHash, CancellationToken ct);
        Task<Result<TlStickerSet, Error>> GetStickerSetByShortNameAsync(string shortName, CancellationToken ct);
        Task<Result<TlStickerSet, Error>> GetStickerSetByIdAsync(long id, long accessHash, CancellationToken ct);
        Task<Result<bool, Error>> InstallStickerSetAsync(long id, long accessHash, bool archived, CancellationToken ct);
        Task<Result<bool, Error>> UninstallStickerSetAsync(long id, long accessHash, CancellationToken ct);
        Task<Result<TlFavedStickers, Error>> GetFavedStickersAsync(long currentHash, CancellationToken ct);
        Task<Result<bool, Error>> FaveStickerAsync(long stickerId, long accessHash, bool unfave, CancellationToken ct);
        Task<Result<TlStickerSearchResult, Error>> SearchStickerSetsAsync(string query, long currentHash, CancellationToken ct);
        Task<Result<byte[], Error>> DownloadBlobAsync(TlFileLocation loc, CancellationToken ct);
    }
}
```

### `IStickerBlobStore`

```csharp
public interface IStickerBlobStore
{
    Task<Result<bool, Error>> SaveAsync(long stickerSetId, long stickerId, byte[] payload, StickerKind kind, CancellationToken ct);
    Task<Result<byte[], Error>> LoadAsync(long stickerSetId, long stickerId, CancellationToken ct);
    Task<Result<bool, Error>> ExistsAsync(long stickerSetId, long stickerId, CancellationToken ct);
    Task<Result<bool, Error>> DeletePackAsync(long stickerSetId, CancellationToken ct);
    Task<Result<long, Error>> EstimateSizeBytesAsync(CancellationToken ct);
}
```

Folder layout: `LocalFolder/stickers/{packId}/{stickerId}.bin` + `{packId}/_thumb.bin`. A pack uninstall deletes the whole folder — a fast path.

### `IStickerMetadataStore`

SQLite with tables:

```
stickers_pack(id INTEGER PRIMARY KEY, short_name TEXT, title TEXT, hash INTEGER, count INTEGER,
              archived INTEGER, animated INTEGER, video INTEGER, installed_at INTEGER)
stickers_sticker(id INTEGER PRIMARY KEY, pack_id INTEGER, emoji TEXT, kind INTEGER,
                 width INTEGER, height INTEGER, has_blob INTEGER, FOREIGN KEY(pack_id) REFERENCES stickers_pack(id))
stickers_recent(sticker_id INTEGER PRIMARY KEY, last_used INTEGER, use_count INTEGER)
stickers_faved(sticker_id INTEGER PRIMARY KEY, faved_at INTEGER)
INDEX idx_sticker_emoji ON stickers_sticker(emoji)
INDEX idx_sticker_pack ON stickers_sticker(pack_id)
```

Reason: the query "stickers for emoji 🍒" must be O(log n), not an in-memory scan. SQLite is in the BCL via `Microsoft.Data.Sqlite` ported for WP8.1 (alternative: `SQLitePCL.raw`).

### `IStickerDecoder`

```csharp
public interface IStickerDecoder
{
    Task<Result<DecodedFrame, Error>> DecodeStaticAsync(byte[] webpBytes, CancellationToken ct);
    Task<Result<TgsAnimationHandle, Error>> OpenAnimationAsync(byte[] tgsBytes, CancellationToken ct);
    Task<Result<DecodedFrame, Error>> RenderFrameAsync(TgsAnimationHandle handle, double timeSeconds, CancellationToken ct);
    Task<Result<bool, Error>> CloseAnimationAsync(TgsAnimationHandle handle, CancellationToken ct);
}
```

The implementation wires to `Vianigram.Core.Media`, which has a native WebP decoder + a Lottie player. Stickers only consumes the handle; the lifecycle of decoded frames lives on the Media side to avoid native memory leaks.

---

## 7. Notable use cases

### `SyncStickerLibraryUseCase`

```csharp
public sealed class SyncStickerLibraryUseCase
{
    private readonly IStickerTlGateway _tl;
    private readonly IStickerMetadataStore _meta;
    private readonly IStickerBlobStore _blobs;
    private readonly IEventBus _bus;
    private readonly IClock _clock;
    private readonly ILogger _log;

    public async Task<Result<SyncResult, Error>> ExecuteAsync(CancellationToken ct)
    {
        var localHash = await _meta.GetCurrentHashAsync(ct).ConfigureAwait(false);
        if (!localHash.IsOk) return Result.Fail<SyncResult, Error>(localHash.Error);

        var fetched = await _tl.GetAllStickersAsync(localHash.Value, ct).ConfigureAwait(false);
        if (!fetched.IsOk) return Result.Fail<SyncResult, Error>(fetched.Error);

        if (fetched.Value.NotModified)
            return Result.Ok<SyncResult, Error>(new SyncResult(0, 0, true));

        var diff = StickerLibraryDiffer.Compute(localSnapshot: await _meta.SnapshotAsync(ct), remote: fetched.Value);
        foreach (var added in diff.PacksAdded)
            await _meta.UpsertPackAsync(added, ct).ConfigureAwait(false);
        foreach (var removed in diff.PacksRemoved)
        {
            await _meta.DeletePackAsync(removed, ct).ConfigureAwait(false);
            await _blobs.DeletePackAsync(removed, ct).ConfigureAwait(false);
        }
        await _meta.SetCurrentHashAsync(fetched.Value.Hash, ct).ConfigureAwait(false);

        _bus.Publish(new StickerLibrarySynced(diff.PacksAdded.Count, diff.PacksRemoved.Count, _clock.UtcNow));
        return Result.Ok<SyncResult, Error>(new SyncResult(diff.PacksAdded.Count, diff.PacksRemoved.Count, false));
    }
}
```

### `LoadPackContentUseCase`

Lazy: it is only called when the UI opens the pack. It brings stickers + thumbs in a batch, full-resolution blobs on demand at the first render.

### `SendStickerUseCase`

Ordered side-effects:
1. Verifies that the sticker exists in metadata.
2. Publishes `StickerSent` (another context — `Vianigram.Messaging` — listens and produces the TL `messages.sendMedia`).
3. Updates the MRU: bumps it or adds it to the top.
4. If the MRU > 20, evicts the last one.
5. Persists the MRU.

`StickerSent` does not send the sticker — it only notifies. This preserves the separation: Stickers does not know messaging.

---

## 8. Cross-context dependencies

| Outbound | Implemented by | Doc |
|---|---|---|
| `IStickerTlGateway` | `Vianigram.Composition.Adapters.TlStickerGatewayAdapter` which uses the sibling `vianium-mtproto` (`src\tl\`) | [15-shell-and-host.md](15-shell-and-host.md) |
| `IStickerBlobStore` | `LocalFolderStickerBlobStore` (its own infra) | self |
| `IStickerMetadataStore` | `SqliteStickerMetadataStore` (its own infra, ported SQLite) | self |
| `IStickerDecoder` | `Vianigram.Composition.Adapters.MediaStickerDecoderAdapter` which uses `Vianigram.Core.Media` | [15-shell-and-host.md](15-shell-and-host.md) |

Events consumed by other contexts:
- `Vianigram.Messaging` listens to `StickerSent` to include the sticker in the send.
- `Vianigram.App` (presentation) listens to `StickerLibrarySynced` to refresh the panel.

Events consumed from other contexts:
- `Vianigram.Auth` publishes `AccountSwitched`; this context listens and reconstitutes its aggregate from the folder/SQLite of the new account.

---

## 9. Storage detail

### Folder layout

```
LocalFolder/
└── accounts/
    └── {accountId}/
        └── stickers/
            ├── meta.db                       (SQLite metadata)
            ├── library-snapshot.json         (fast boot snapshot)
            └── packs/
                ├── 1234567/
                │   ├── _thumb.webp
                │   ├── 9876543.webp          (static)
                │   └── 9876544.tgs           (animated)
                └── 2345678/
                    └── ...
```

### Expected size

- A typical pack: 20 stickers × 30–60 KB = ~1 MB for WebP, ~300–600 KB for TGS.
- A typical user: 8–15 installed packs → 8–15 MB on disk.
- Hard cap: `LocalFolder` on WP8.1 can normally grow unlimited but we monitor it via `EstimateSizeBytesAsync`. If > 100 MB, suggest uninstalling unused packs (UI Settings → Storage).

### Snapshot file

`library-snapshot.json` is written on each successful `Sync` to speed up the boot: if the snapshot exists and the SQLite integrity is good, the aggregate is reconstituted in <50 ms without doing a full SQL scan. It is only a cache; SQLite is the source of truth.

---

## 10. Performance

| Metric | Target | Strategy |
|---|---|---|
| Aggregate boot | < 80 ms | JSON snapshot + lazy SQLite attach |
| Opening the sticker panel | < 150 ms | Preload the top-1 pack at boot; render thumbs first |
| Rendering a static thumb | < 8 ms | WebP decode in `Vianigram.Core.Media` (WinMD) |
| First TGS frame | < 80 ms | Async decode; show the thumb while it is being prepared |
| Send sticker → MRU update visible | < 50 ms | UpdateMruRanker is in-memory; persist deferred 500 ms |
| Global pack search | P95 < 600 ms | 300 ms debounce in the VM + a TL roundtrip |
| Steady-state memory with the panel closed | < 6 MB | Only metadata + thumbs loaded |
| Memory with the panel open, scrolling | < 18 MB | An LRU cache of N=64 decoded frames |

### Hot paths

1. **`GetByEmojiAsync(emoji)`** — used on every keystroke of the input when the emoji panel is visible. A SQLite indexed query, < 5 ms for a medium library.
2. **`RenderFrameAsync(handle, t)`** — TGS playback target 30 fps. The per-frame budget is 33 ms; we reserve 8 ms for Lottie compute, 8 ms for WriteableBitmap upload, the rest for layout.
3. **MRU bump** — on send, do NOT write to disk synchronously. Accumulate in a buffer and persist every 500 ms or on suspend.

---

## 11. TL methods consumed

| Method | Use | Frequency |
|---|---|---|
| `messages.getAllStickers` | Initial sync + each time we receive an `updateNewStickerSet` | Boot + on-demand |
| `messages.getStickerSet` | When a message includes a sticker from a non-installed pack and we want to preview it | On message receive |
| `messages.installStickerSet` | The user installs from search or from a sticker preview | User action |
| `messages.uninstallStickerSet` | The user uninstalls | User action |
| `messages.getFavedStickers` | Boot + sync | Boot |
| `messages.faveSticker` | Long-tap → favorite | User action |
| `messages.searchStickerSets` | A search in discovery | User action, debounced |
| `messages.getRecentStickers` | Initial sync of the server-side MRU (Telegram also keeps the MRU server-side) | Boot — if our local MRU is empty, hydrate from the server |
| `messages.saveRecentSticker` | After a send, sync the MRU to the server | Throttled, on-suspend |
| `upload.getFile` | Download the sticker's blob | Lazy on first use |

The equivalent TL constructors (`InputStickerSetShortName`, `InputDocument`, etc.) live behind `IStickerTlGateway`. Stickers only sends primitives (`shortName: string`, `id: long`, `accessHash: long`).

---

## 12. Animated stickers (TGS) — pipeline

1. **Download**: a TGS blob = gzip(Lottie JSON). 30–60 KB typical, arrives via `upload.getFile`.
2. **Inflate**: `Vianigram.Core.Media` does the gzip decompress natively (not `System.IO.Compression` to avoid the managed overhead).
3. **Parse Lottie**: the native decoder uses Skottie or its own C++ Lottie player ported to WinMD.
4. **Open animation**: `IStickerDecoder.OpenAnimationAsync(bytes)` returns a `TgsAnimationHandle` that the caller uses to request frames.
5. **Render**: 30 fps. Each frame returns a `DecodedFrame` with a pre-multiplied RGBA buffer. The ViewModel blits it to a `WriteableBitmap`.
6. **Close**: when the sticker leaves the viewport, call `CloseAnimationAsync` to free the native resources.

If the `stickers.animated` capability is off (a 512 MB device in "static stickers" mode), every TGS is replaced by its static WebP thumbnail and the animated pipeline is never called.

---

## 13. Open questions

1. **WebM video stickers** — Telegram introduced them in 2022. WP8.1 support requires a WebM demuxer + a VP8/VP9 decoder. Current decision: off in V1 (`stickers.video = false`); fallback to a static thumb.
2. **Custom emoji** (a Telegram Premium feature). Behavior similar to stickers but painted inline in messages. A subaggregate of `StickerLibrary` or its own context? Probably a subaggregate; the invariant rules are the same.
3. **Sync with the server MRU**: Telegram keeps `messages.getRecentStickers` server-side. Do we merge it at boot or only if the local MRU is empty? Risk: if the user used the web app, the server's MRU may be noise. Preliminary decision: hydrate only if local is empty + offer "merge the server's MRU" in Settings.
4. **Premium animated emoji reactions** — out of V1.
5. **Maximum cache size before a prompt** — propose 100 MB. If the client has < 200 MB free in LocalFolder, drop it to 50 MB and show a warning.

---

## 14. Crosslinks

- [00-overview.md](00-overview.md) — the order of phases.
- [11-settings.md](11-settings.md) — the `stickers.animated` toggle and the storage quota.
- [14-presentation.md](14-presentation.md) — the `StickerPanel` and `StickerBubble` UserControls.
- [15-shell-and-host.md](15-shell-and-host.md) — the cross-context adapters.
