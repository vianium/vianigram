# Vianigram.Search — Search Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md). This context covers the **four search modalities in Telegram**: per-chat (messages within a chat), global (messages across all dialogs), public (discovering public users/channels/bots by username), and in-channel (searching a channel's posts). The TL backend does the heavy lifting; this context is a coordinator with debounce + pagination + cancellation + a local history of recent queries.

---

## 1. Bounded context

- **Ubiquitous language:** query, search session, scope (`InChat`, `Global`, `Public`, `InChannel`), filter (`MediaPhoto`, `MediaVideo`, `Mentions`, `Voice`, `Music`, `Url`, `Doc`, `Hashtag`), result page, paging cursor (`add_offset`, `offset_id`, `offset_date`), debounce, autocomplete history, recent searches (peers visited via search), search analytics.
- **Aggregate root:** `SearchSession` — the state of an active search: `(SearchScope, query, filter, paging cursor, results loaded so far, isLoading)`. There is at most one active `SearchSession` per scope; the lifecycle is page-bound: the `SearchPage` creates it, uses it, disposes it when navigating away.
- **Secondary aggregates:**
  - `SearchHistory` — the local record of the user's last N=20 queries and the last M=10 peers opened via search. An MRU type. Persisted in `LocalSettings`.
- **Value objects:**
  - `SearchScope` — `InChat(peerId)`, `Global(filter)`, `PublicByUsername(prefix)`, `InChannel(peerId, filter)`.
  - `SearchFilter` — discriminated: `Empty`, `Photos`, `Videos`, `Documents`, `Voice`, `Music`, `Mentions`, `Urls`, `Hashtags`, `Mentioned`, `Pinned`.
  - `SearchQuery` — a normalized `string` (trim + collapse whitespace); 0 < length < 256.
  - `PagingCursor` — `(int addOffset, long offsetId, long offsetDate, int limit)`. Page size default 20.
  - `SearchResultItem` — discriminated: `MessageHit(peerId, messageId, snippet, sentUtc, senderName, mediaIcon)`, `PeerHit(peerId, kind, displayName, username, photoSmallRef)`, `BotHit(...)`, `ChannelHit(...)`.
  - `SearchTimings` — `(int suggestionMs, int firstResultMs, int totalMs)`.
- **Domain events emitted:**
  - `SearchStarted(scope, query, filter)`.
  - `SearchPageLoaded(scope, query, int pageNumber, int hitsCount)`.
  - `SearchCancelled(scope, reason)`.
  - `SearchCompleted(scope, query, int totalHits, TimeSpan elapsed)`.
  - `SearchFailed(scope, query, Error)`.
  - `RecentQueryRecorded(query)`.
  - `RecentPeerRecorded(peerId)`.
- **Capabilities exposed:**
  - `search.global` — enables the Global modality (off for private users who prefer not to use the Telegram global).
  - `search.public` — discovery via `contacts.search`.
  - `search.history_persisted` — keeps the autocomplete history (off = privacy mode).
  - `search.filters` — enables the media type filters.

---

## 2. Goal

PivoraTelegram has `SearchPage.xaml.cs` with ~700 lines that mix: reading from `_client.SearchAsync`, parsing TL DTOs, client-side filtering, infinite scroll without cancellation, dialog history persisted in raw `IsolatedStorage`. Replace it with:

1. **A `SearchSession` aggregate** that encapsulates the state and has explicit transitions: `Idle → Loading → PageLoaded → LoadingMore → ... → Completed | Cancelled | Failed`. The VM does not handle `_isLoading` or `_canLoadMore` flags; it reads them from the aggregate.
2. **A 250ms debounce** on the input — the use case knows it; the VM just sends each keystroke to the use case.
3. **Explicit cancellation via `CancellationTokenSource`**: each new query cancels the previous one.
4. **Correct O(N) pagination**: `messages.search` returns `messages.Messages` with `count` (total) and the page's list; a cursor for the next.
5. **Filters as first-class**: changing from `SearchFilter.Empty` to `SearchFilter.Photos` resets the session.
6. **An anti-corruption layer**: Search does not know `MessagesSearch` TL; everything goes through `ISearchTlGateway`.
7. **Telemetry** — `SearchTimings` is published with each `SearchCompleted` to monitor performance.

---

## 3. Search modalities — how they map

| Scope | TL method | Notes |
|---|---|---|
| `InChat(peerId)` | `messages.search(peer, q, filter, ...)` | The server does full-text indexing. |
| `Global(filter)` | `messages.searchGlobal(folder_id, q, filter, offset_*)` | Cross-dialog. Expensive on the server. |
| `PublicByUsername(prefix)` | `contacts.search(q, limit)` | Returns public users + chats whose username begins with the prefix. |
| `InChannel(peerId, filter)` | `channels.searchPosts(channel, hashtag, ...)` (or `messages.search` for non-hashtag) | Channels offers a dedicated API for hashtag search. |

`SearchFilter` maps to TL constructors:

| Filter VO | TL constructor |
|---|---|
| `Empty` | `inputMessagesFilterEmpty` |
| `Photos` | `inputMessagesFilterPhotos` |
| `Videos` | `inputMessagesFilterVideo` |
| `Documents` | `inputMessagesFilterDocument` |
| `Voice` | `inputMessagesFilterVoice` |
| `Music` | `inputMessagesFilterMusic` |
| `Mentions` | `inputMessagesFilterMyMentions` |
| `Urls` | `inputMessagesFilterUrl` |
| `Pinned` | `inputMessagesFilterPinned` |

---

## 4. Native target — the `Vianigram.Search` project

```
Core/Vianigram.Search/
├── Vianigram.Search.csproj                    (WP8.1)
├── Properties/AssemblyInfo.cs
│
├── Domain/
│   ├── ValueObjects/
│   │   ├── SearchScope.cs
│   │   ├── SearchFilter.cs
│   │   ├── SearchQuery.cs
│   │   ├── PagingCursor.cs
│   │   ├── SearchResultItem.cs
│   │   ├── PeerKind.cs                        (User, Bot, Group, Channel)
│   │   └── SearchTimings.cs
│   ├── Aggregates/
│   │   ├── SearchSession.cs                   (root)
│   │   └── SearchHistory.cs
│   ├── Events/
│   │   ├── SearchStarted.cs
│   │   ├── SearchPageLoaded.cs
│   │   ├── SearchCancelled.cs
│   │   ├── SearchCompleted.cs
│   │   ├── SearchFailed.cs
│   │   ├── RecentQueryRecorded.cs
│   │   └── RecentPeerRecorded.cs
│   ├── Services/
│   │   ├── QueryNormalizer.cs                 (trim, collapse, lowercase ICU)
│   │   ├── ResultMerger.cs                    (merge new page + dedupe by msgId)
│   │   └── DebounceClock.cs                   (computes "should fire?")
│   ├── Policies/
│   │   ├── SearchHistoryLimitsPolicy.cs       (cap 20 queries, 10 peers)
│   │   └── PageSizePolicy.cs                  (limit=20)
│   └── Errors/
│       └── SearchErrors.cs
│
├── Application/
│   ├── Commands/
│   │   ├── ExecuteSearchCommand.cs
│   │   ├── LoadNextPageCommand.cs
│   │   ├── CancelSearchCommand.cs
│   │   ├── ChangeFilterCommand.cs
│   │   └── ClearHistoryCommand.cs
│   ├── Queries/
│   │   ├── GetRecentQueriesQuery.cs
│   │   └── GetRecentPeersQuery.cs
│   ├── UseCases/
│   │   ├── ExecuteSearchUseCase.cs            (debounce + first page)
│   │   ├── LoadNextPageUseCase.cs
│   │   ├── CancelActiveSearchUseCase.cs
│   │   ├── RecordRecentQueryUseCase.cs
│   │   ├── RecordRecentPeerUseCase.cs
│   │   └── ClearHistoryUseCase.cs
│   └── Internal/
│       ├── DebouncedSearchExecutor.cs
│       └── PaginationCoordinator.cs
│
├── Ports/
│   ├── Inbound/
│   │   └── ISearchApi.cs
│   └── Outbound/
│       ├── ISearchTlGateway.cs                (ACL towards the sibling vianium-mtproto src\tl\)
│       ├── ISearchHistoryStore.cs             (LocalSettings)
│       └── IClock.cs                          (re-export from the Kernel)
│
├── Infrastructure/
│   ├── Tl/
│   │   ├── TlSearchGateway.cs
│   │   └── TlSearchMappers.cs
│   └── Persistence/
│       └── LocalSettingsSearchHistoryStore.cs
│
└── Api/
    └── V1/
        ├── ISearchApi.cs
        ├── SearchRequest.cs
        ├── SearchResponse.cs
        ├── SearchPageDto.cs
        ├── SearchResultDto.cs
        └── SearchApiErrors.cs
```

---

## 5. Inbound — `ISearchApi`

```csharp
namespace Vianigram.Search.Api.V1
{
    public interface ISearchApi
    {
        Task<Result<SearchPageDto, Error>> ExecuteAsync(SearchRequest req, CancellationToken ct);
        Task<Result<SearchPageDto, Error>> LoadNextPageAsync(Guid sessionId, CancellationToken ct);
        Task<Result<bool, Error>> CancelAsync(Guid sessionId, CancellationToken ct);
        Task<Result<IReadOnlyList<string>, Error>> GetRecentQueriesAsync(int max, CancellationToken ct);
        Task<Result<IReadOnlyList<long>, Error>> GetRecentPeersAsync(int max, CancellationToken ct);
        Task<Result<bool, Error>> ClearHistoryAsync(CancellationToken ct);
    }
}
```

`SearchRequest`:

```csharp
public sealed class SearchRequest
{
    public SearchScope Scope { get; set; }
    public string Query { get; set; }
    public SearchFilter Filter { get; set; }
    public Guid? ContinuationOf { get; set; }   // if it is the same session updated
}
```

---

## 6. Outbound — `ISearchTlGateway`

```csharp
public interface ISearchTlGateway
{
    Task<Result<TlSearchResultPage, Error>> SearchInChatAsync(
        long peerId, string query, SearchFilter filter, PagingCursor cursor, CancellationToken ct);

    Task<Result<TlSearchResultPage, Error>> SearchGlobalAsync(
        string query, SearchFilter filter, PagingCursor cursor, CancellationToken ct);

    Task<Result<TlPublicSearchResult, Error>> SearchPublicAsync(
        string prefix, int limit, CancellationToken ct);

    Task<Result<TlSearchResultPage, Error>> SearchPostsInChannelAsync(
        long channelId, string hashtagOrQuery, PagingCursor cursor, CancellationToken ct);
}

public sealed class TlSearchResultPage
{
    public int TotalCount { get; }
    public IReadOnlyList<TlMessage> Messages { get; }
    public IReadOnlyList<TlPeer> ResolvedPeers { get; }
    public PagingCursor NextCursor { get; }
}
```

---

## 7. Notable use cases

### `ExecuteSearchUseCase`

```csharp
public sealed class ExecuteSearchUseCase
{
    private readonly ISearchTlGateway _tl;
    private readonly ISearchHistoryStore _history;
    private readonly DebouncedSearchExecutor _debouncer;
    private readonly IEventBus _bus;
    private readonly IClock _clock;
    private readonly ILogger _log;

    private readonly Dictionary<Guid, CancellationTokenSource> _active = new Dictionary<Guid, CancellationTokenSource>();

    public async Task<Result<SearchPageDto, Error>> ExecuteAsync(SearchRequest req, CancellationToken outerCt)
    {
        // Cancel previous session in same VM context (the VM passes the same ContinuationOf or a new sessionId).
        if (req.ContinuationOf.HasValue && _active.TryGetValue(req.ContinuationOf.Value, out var prev))
        {
            prev.Cancel();
            _active.Remove(req.ContinuationOf.Value);
        }

        var sessionId = Guid.NewGuid();
        var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        _active[sessionId] = cts;

        try
        {
            var queryResult = SearchQuery.Create(req.Query);
            if (!queryResult.IsOk) return Result.Fail<SearchPageDto, Error>(queryResult.Error);

            // Debounce 250ms — wait, then check if cancelled
            var fired = await _debouncer.WaitForFireAsync(sessionId, TimeSpan.FromMilliseconds(250), cts.Token).ConfigureAwait(false);
            if (!fired) return Result.Fail<SearchPageDto, Error>(SearchErrors.Cancelled);

            _bus.Publish(new SearchStarted(req.Scope, queryResult.Value.Text, req.Filter));

            var startUtc = _clock.UtcNow;
            var cursor = PagingCursor.FirstPage(limit: 20);
            var firstPage = await ExecuteScopeAsync(req.Scope, queryResult.Value, req.Filter, cursor, cts.Token).ConfigureAwait(false);
            if (!firstPage.IsOk) { _bus.Publish(new SearchFailed(req.Scope, queryResult.Value.Text, firstPage.Error)); return firstPage; }

            await _history.RecordQueryAsync(queryResult.Value.Text, cts.Token).ConfigureAwait(false);
            _bus.Publish(new SearchPageLoaded(req.Scope, queryResult.Value.Text, 1, firstPage.Value.Hits.Count));
            _bus.Publish(new SearchCompleted(req.Scope, queryResult.Value.Text, firstPage.Value.TotalCount, _clock.UtcNow - startUtc));

            firstPage.Value.SessionId = sessionId;
            return firstPage;
        }
        finally
        {
            _active.Remove(sessionId);
            cts.Dispose();
        }
    }

    private Task<Result<SearchPageDto, Error>> ExecuteScopeAsync(SearchScope scope, SearchQuery q, SearchFilter f, PagingCursor c, CancellationToken ct)
    {
        switch (scope.Kind)
        {
            case SearchScopeKind.InChat:
                return ExecuteInChatAsync(scope.PeerId, q, f, c, ct);
            case SearchScopeKind.Global:
                return ExecuteGlobalAsync(q, f, c, ct);
            case SearchScopeKind.PublicByUsername:
                return ExecutePublicAsync(q, ct);
            case SearchScopeKind.InChannel:
                return ExecuteInChannelAsync(scope.PeerId, q, f, c, ct);
            default:
                return Task.FromResult(Result.Fail<SearchPageDto, Error>(SearchErrors.UnknownScope));
        }
    }
    // ... ExecuteInChatAsync, ExecuteGlobalAsync, etc. map to the TL gateway
}
```

### `LoadNextPageUseCase`

Takes the cursor from the last response and sends another request. If the response has 0 new messages or `cursor.IsEmpty`, it marks the session as `Completed`.

### `CancelActiveSearchUseCase`

Cancels the `CancellationTokenSource` registered by session id. Publishes `SearchCancelled(reason: "user")`.

---

## 8. Debouncing

`DebouncedSearchExecutor` keeps a queue: each call with `(sessionId, fireAfter)` arms a timer. If another call arrives with the same `sessionId` before the fire, the timer is reset. If it arrives with a different sessionId, both timers run in parallel (the VM cancels the previous one on its side).

A simple implementation without `System.Reactive`:

```csharp
internal sealed class DebouncedSearchExecutor
{
    private readonly Dictionary<Guid, DateTime> _scheduled = new Dictionary<Guid, DateTime>();
    private readonly object _lock = new object();
    private readonly IClock _clock;

    public DebouncedSearchExecutor(IClock clock) { _clock = clock; }

    public async Task<bool> WaitForFireAsync(Guid sessionId, TimeSpan delay, CancellationToken ct)
    {
        var deadline = _clock.UtcNow + delay;
        lock (_lock) { _scheduled[sessionId] = deadline; }

        while (true)
        {
            var now = _clock.UtcNow;
            DateTime current;
            lock (_lock) { if (!_scheduled.TryGetValue(sessionId, out current)) return false; }
            if (now >= current)
            {
                lock (_lock) { _scheduled.Remove(sessionId); }
                return true;
            }
            try { await Task.Delay(current - now, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { lock (_lock) _scheduled.Remove(sessionId); return false; }
        }
    }
}
```

`VM` push: each keystroke of the input fires `ExecuteAsync`; the debouncer makes only the last one win.

---

## 9. Cross-context

| Outbound | Implemented by | Doc |
|---|---|---|
| `ISearchTlGateway` | `Vianigram.Composition.Adapters.TlSearchGatewayAdapter` (wraps the sibling `vianium-mtproto` `src\tl\`) | [15-shell-and-host.md](15-shell-and-host.md) |
| `ISearchHistoryStore` | `LocalSettingsSearchHistoryStore` (its own infra) | self |

Published events consumed by:
- `Vianigram.App` (`SearchPage`) listens to `SearchPageLoaded` to refresh the observable list.
- An optional telemetry/analytics listens to `SearchCompleted` for latency histograms.

Events consumed:
- `Vianigram.Auth.AccountSwitched` invalidates `SearchHistory` (the new user should not see the previous one's queries).

---

## 10. Storage

`LocalSettings`:

| Key | Type | Meaning |
|---|---|---|
| `search.history.queries` | `string` (JSON `string[]`) | The MRU of queries; cap 20 |
| `search.history.peers` | `string` (JSON `long[]`) | The MRU of peer ids; cap 10 |
| `search.history.cleared_at` | `long` (ticks) | For audit |

Total ~ 1 KB, plenty of room. There are no heavy blobs; search results are NOT cached (they always go fresh to the server).

---

## 11. Performance

| Metric | Target | Strategy |
|---|---|---|
| Time from input → first page visible | P95 < 800 ms | 250 ms debounce + a TL roundtrip + render |
| Cancellation (input changes while loading) | < 50 ms | CTS.Cancel + skip render |
| Load next page | P95 < 600 ms | Only a TL roundtrip |
| Active session memory | < 2 MB | Page size 20 × ~20 visible hits + 4 pages of history |
| Recent queries lookup | < 5 ms | LocalSettings + JSON parse |

### Hot path

1. **The input changes**: the VM cancels the previous CTS, creates a new one, calls `ExecuteAsync`. The debouncer absorbs bursts up to 250 ms after the last keystroke.
2. **Scrolling near the end**: the VM detects it and calls `LoadNextPageAsync`. Only one call in-flight per session.
3. **Tap on a peer hit**: `RecordRecentPeerUseCase` records it; the VM navigates to `ChatPage`.

---

## 12. UI filters ↔ Domain

`SearchPage` shows a `Pivot` or tabs with:

- "All" → `Empty`
- "Media" → `Photos | Videos` combined (actually two requests; or use a combined `MediaTypes` filter if the TL schema allows it — V1 does two requests and a client-side merge)
- "Files" → `Documents`
- "Voice" → `Voice`
- "Music" → `Music`
- "Links" → `Urls`
- "@" → `Mentions`

Changing tab = a new session, a new query (the query string is preserved).

---

## 13. TL methods consumed

| Method | Scope | Frequency |
|---|---|---|
| `messages.search` | `InChat`, `InChannel` non-hashtag | Each query |
| `messages.searchGlobal` | `Global` | Each query |
| `contacts.search` | `PublicByUsername` | Each keystroke (debounced) |
| `channels.searchPosts` | `InChannel` with a hashtag or full-text if the schema supports it | Each query |
| `messages.getMessages` | On-demand resolution of referenced messages | Only if the page response has IDs without content (rare) |

---

## 14. Open questions

1. **Hashtag-aware search**: `#vianium` should detect the `#` and send `inputMessagesFilterEmpty` with the exact query, or use `messages.search` with the `top_msg_id` flag. Decision: detect the `#` prefix and pass the query intact to TL (the Telegram server does the highlighting).
2. **Voice search**: `Windows.Media.SpeechRecognition` allows mic → string. Capability `search.voice`, off in V1.
3. **Search-as-you-type vs explicit submit**: V1 is search-as-you-type (debounced). If the user is on cellular without Wifi, it could be expensive; consult `network.use_less_data` and increase the debounce to 600ms if it is active.
4. **Result snippet with highlighting**: TL returns a raw `Message.message: string`; we have to build the snippet locally (find the match and truncate 30 chars before/after). A domain service `SnippetExtractor`.
5. **Search in secret chat messages**: secret chats are client-side only; there is no server-side TL search. V1 decision: do NOT include secret chats in `Global` or `InChat` search; show a "Secret chats are searched separately" message if applicable. V2 introduces local search in the secret messages cache.
6. **Cancellation of a TL request in flight**: the `CancellationToken` must propagate down to the `vianium-mtproto` sibling. Verify that the adapter respects the CT and does not leave an orphaned request.

---

## 15. Crosslinks

- [00-overview.md](00-overview.md)
- [11-settings.md](11-settings.md) — the `search.global`, `search.history_persisted` capabilities.
- [14-presentation.md](14-presentation.md) — the `SearchPage` and `SearchViewModel` consume this context.
- [15-shell-and-host.md](15-shell-and-host.md) — the TL gateway adapter.
