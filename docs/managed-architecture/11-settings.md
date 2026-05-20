# Vianigram.Settings — User Preferences Bounded Context

> **Required prior reading:** [principles.md](principles.md) and [00-overview.md](00-overview.md). This context is the **configuration provider** for all the other contexts (Stickers, Notifications, Privacy, Search, Messaging, Theme). It uses the same snapshot + diff pattern as `Vianium.Browser.Settings`: strong typing via `PreferenceKey<T>`, schema versioning, per-key validators, and `LocalSettings` as an O(1) backend.

---

## 1. Bounded context

- **Ubiquitous language:** preference, key, value, category, schema, default, validator, snapshot, diff, migration, profile (a future multi-account preferences-merge), import/export, scope (`Application` | `Account` | `Chat`-future), reset, network policy, auto-download policy, proxy, passcode key.
- **Aggregate root:** `UserPreferences` — an immutable snapshot of the complete state. Mutations produce a new snapshot.
- **Secondary aggregates:**
  - `PreferenceCatalog` — the declarative registry of known keys (static).
  - `MigrationPipeline` — the migration steps between schema versions.
- **Value objects:**
  - `PreferenceKey<T>` — a strongly-typed key `("namespace.subkey", typeof(T))`.
  - `PreferenceValue<T>` — a wrapper `(value, modifiedUtc, source)`.
  - `PreferenceCategory` — `General`, `Appearance`, `Chat`, `Privacy`, `Notifications`, `Storage`, `Network`, `Stickers`, `Voice`, `Diagnostic`.
  - `PreferenceSource` — `Default`, `User`, `Imported`, `Migrated`.
  - `PreferenceScope` — `Application` (global, default), `Account` (per-Telegram-account), `Chat` (per-chat-future).
  - `SchemaVersion` — `(major, minor)`.
  - `ValidationOutcome` — `Ok | Reject(reason)`.
  - `AutoDownloadPolicy` — `(NetworkKind kind, Toggle photos, Toggle videos, Toggle voice, Toggle docs, long maxFileSizeMb)`.
  - `ProxyConfig` — `(ProxyKind kind: None|Mtproto|Socks5, string host, int port, string secretOrCreds)`.
  - `ThemeMode` — `Light | Dark | Auto` (Auto follows the OS theme via `UISettings.GetColorValue`).
  - `LanguagePackId` — `string` ISO 639 + region.
- **Domain events emitted:**
  - `PreferenceChanged<T>(key, oldValue, newValue, source)`.
  - `PreferencesReset(category)`.
  - `PreferencesImported(int countAffected)`.
  - `PreferencesExported(string targetPath)`.
  - `SchemaMigrated(SchemaVersion from, SchemaVersion to, int count)`.
  - `PreferenceValidationFailed(key, reason)`.
- **Capabilities exposed:**
  - `settings.export_import` — JSON dump/load (always on in debug, a flag in release).
  - `settings.profiles` — a future multi-account merge (off in V1).
  - `settings.per_chat_override` — a future per-chat (off in V1).
  - `settings.diagnostic_dump` — include the snapshot in crash reports.

---

## 2. Goal

PivoraTelegram today stores settings in `IsolatedStorageSettings.ApplicationSettings` with ad-hoc keys (`"send_on_enter"`, `"theme"`, `"font_size"`) and a manual parse on each read. This context produces:

1. **Type-safe access** — `var theme = (await _prefs.GetAsync(PreferenceKeys.ThemeMode, ct)).ValueOrDefault();` returns a `ThemeMode`, not a `string`.
2. **A single source of truth for defaults** — `PreferenceCatalog` declares the default of each key. No `??` or repeated `defaultValue:` in consumers.
3. **Observability by event** — `Vianigram.App` subscribes to `PreferenceChanged<ThemeMode>` and reapplies the theme instantly; `Vianigram.Notifications` subscribes to `PreferenceChanged<bool>` with key `notifications.preview_in_lockscreen`.
4. **Schema versioning** — V1 → V2 can rename `chat.send_on_enter` to `chat.send_button_behavior` with the type migrated from `bool` to `enum SendBehavior { Enter, EnterPlusShift, ButtonOnly }`.
5. **A Telegram-typical auto-download policy** — a `(network kind × media kind)` matrix with a cap per size.
6. **A validated proxy config** — Mtproto requires a `secret` (32 hex chars), Socks5 requires host:port (+ optional creds).
7. **Passcode integration** — `Vianigram.Privacy` consumes the key `privacy.passcode_hash` (read-only, write via a dedicated use case in Privacy).

---

## 3. Inventory of V1 keys

| Key | Type | Default | Category | Consumers |
|---|---|---|---|---|
| `appearance.theme_mode` | `ThemeMode` | `Auto` | Appearance | `Vianigram.App` (global theme) |
| `appearance.font_size` | `double` (75–200) | `100` | Appearance | `MessageBubble`, `ChatListItem` |
| `appearance.message_corner_radius` | `int` (0–24) | `12` | Appearance | bubbles |
| `appearance.wallpaper_id` | `string` | `"default"` | Appearance | the `ChatPage` background |
| `appearance.color_accent` | `string` (hex) | `"#0088CC"` | Appearance | theme |
| `chat.send_on_enter` | `bool` | `true` | Chat | the `MessageInput` UserControl |
| `chat.show_typing_indicator_to_others` | `bool` | `true` | Chat | `Vianigram.Messaging` |
| `chat.read_receipts_to_others` | `bool` | `true` | Chat | `Vianigram.Messaging` |
| `chat.auto_play_animated_stickers` | `bool` | `true` | Chat | `Vianigram.Stickers` |
| `chat.auto_play_videos` | `bool` | `false` | Chat | `Vianigram.Media` |
| `chat.large_emoji` | `bool` | `true` | Chat | `MessageBubble` |
| `chat.markdown_in_input` | `bool` | `true` | Chat | the input parser |
| `language.pack_id` | `LanguagePackId` | `"en"` | General | i18n |
| `network.proxy.config` | `ProxyConfig` | `(None,...)` | Network | sibling `vianium-mtproto` |
| `network.use_less_data` | `bool` | `false` | Network | media auto-download |
| `network.auto_download.cellular` | `AutoDownloadPolicy` | `(photos:on, videos:off, voice:on, docs:off, max:5MB)` | Network | the media downloader |
| `network.auto_download.wifi` | `AutoDownloadPolicy` | `(photos:on, videos:on, voice:on, docs:on, max:50MB)` | Network | the media downloader |
| `network.auto_download.roaming` | `AutoDownloadPolicy` | `(none, max:0)` | Network | the media downloader |
| `notifications.preview_in_lockscreen` | `bool` | `true` | Notifications | `Vianigram.Notifications` |
| `notifications.in_app_sound` | `bool` | `true` | Notifications | `Vianigram.Notifications` |
| `notifications.in_app_vibrate` | `bool` | `true` | Notifications | self |
| `privacy.passcode_enabled` | `bool` | `false` | Privacy | `Vianigram.Privacy` (read-only) |
| `privacy.passcode_lock_after_seconds` | `int` (0=immediate, 60, 300, 3600) | `300` | Privacy | `Vianigram.Privacy` |
| `privacy.passcode_biometric` | `bool` | `false` | Privacy | `Vianigram.Privacy` |
| `storage.cache_max_mb` | `int` | `300` | Storage | the media cache |
| `storage.keep_media_days` | `int` | `30` (0 = forever) | Storage | the media cache |
| `voice.use_proximity_speaker` | `bool` | `true` | Voice | `Vianium.VoIP` (call routing) |
| `diagnostic.send_crash_reports` | `bool` | `false` | Diagnostic | telemetry |
| `diagnostic.verbose_logging` | `bool` | `false` | Diagnostic | the logger |

Total V1: ~28 keys. Compared to PivoraTelegram (~12 wild keys), V1 captures more config but unified.

---

## 4. Native target — the `Vianigram.Settings` project

```
Core/Vianigram.Settings/
├── Vianigram.Settings.csproj                  (WP8.1)
├── Properties/AssemblyInfo.cs
│
├── Domain/
│   ├── ValueObjects/
│   │   ├── PreferenceKey.cs                   (PreferenceKey<T> + a non-generic base)
│   │   ├── PreferenceValue.cs
│   │   ├── PreferenceCategory.cs
│   │   ├── PreferenceSource.cs
│   │   ├── PreferenceScope.cs
│   │   ├── SchemaVersion.cs
│   │   ├── ValidationOutcome.cs
│   │   ├── ThemeMode.cs
│   │   ├── LanguagePackId.cs
│   │   ├── AutoDownloadPolicy.cs
│   │   ├── ProxyConfig.cs
│   │   └── NetworkKind.cs                     (Cellular, Wifi, Roaming, Unknown)
│   ├── Aggregates/
│   │   ├── UserPreferences.cs                 (root)
│   │   ├── PreferenceCatalog.cs
│   │   └── MigrationPipeline.cs
│   ├── Events/
│   │   ├── PreferenceChanged.cs
│   │   ├── PreferencesReset.cs
│   │   ├── PreferencesImported.cs
│   │   ├── PreferencesExported.cs
│   │   ├── SchemaMigrated.cs
│   │   └── PreferenceValidationFailed.cs
│   ├── Services/
│   │   ├── PreferenceSerializer.cs            (T ↔ canonical string)
│   │   ├── DefaultsResolver.cs
│   │   └── PreferenceDiffer.cs                (snapshot diff → events)
│   ├── Policies/
│   │   ├── ValidationPolicy.cs
│   │   └── BlobSizePolicy.cs                  (rejects blobs > 8KB)
│   └── Errors/
│       └── SettingsErrors.cs
│
├── Application/
│   ├── UseCases/
│   │   ├── GetPreferenceUseCase.cs
│   │   ├── SetPreferenceUseCase.cs
│   │   ├── ResetCategoryUseCase.cs
│   │   ├── ResetAllUseCase.cs
│   │   ├── ExportPreferencesUseCase.cs
│   │   ├── ImportPreferencesUseCase.cs
│   │   ├── SnapshotUseCase.cs
│   │   └── MigrateSchemaUseCase.cs
│   ├── Queries/
│   │   ├── ListByCategoryQuery.cs
│   │   └── ListAllQuery.cs
│   └── Commands/
│       ├── SetPreferenceCommand.cs
│       ├── ResetCategoryCommand.cs
│       └── ImportPreferencesCommand.cs
│
├── Ports/
│   ├── Inbound/
│   │   └── ISettingsApi.cs
│   └── Outbound/
│       ├── ISettingsStorage.cs                (KV abstraction)
│       ├── ISchemaMigrator.cs
│       ├── IPreferenceValidator.cs
│       ├── IPreferenceSerializer.cs
│       ├── ISnapshotExporter.cs
│       └── ISnapshotImporter.cs
│
├── Infrastructure/
│   ├── Storage/
│   │   ├── LocalSettingsStorage.cs            (impl Windows.Storage.LocalSettings)
│   │   ├── InMemorySettingsStorage.cs         (testing)
│   │   └── FileSettingsStorage.cs             (fallback LocalFolder/preferences.json)
│   ├── Schema/
│   │   ├── SchemaMigrator.cs
│   │   ├── MigrationStep_V1_to_V2.cs
│   │   └── MigrationManifest.cs
│   ├── Serialization/
│   │   ├── JsonSnapshotExporter.cs
│   │   ├── JsonSnapshotImporter.cs
│   │   └── CanonicalSerializers.cs
│   └── Validation/
│       ├── RangeValidator.cs
│       ├── EnumValidator.cs
│       ├── ProxyConfigValidator.cs
│       └── NoOpValidator.cs
│
├── Catalog/
│   └── PreferenceKeys.cs                      (static declaration of ALL the V1 keys)
│
└── Api/
    └── V1/
        ├── ISettingsApi.cs
        ├── PreferenceChangeNotification.cs
        ├── ImportResult.cs
        ├── ExportResult.cs
        └── ResetResult.cs
```

**Project references:** `Vianigram.Kernel` + BCL. No other context.

---

## 5. `PreferenceKey<T>` and the catalog

### `PreferenceKey<T>`

```csharp
namespace Vianigram.Settings.Domain.ValueObjects
{
    public abstract class PreferenceKeyBase
    {
        public string Name { get; }
        public PreferenceCategory Category { get; }
        public Type ValueType { get; }
        protected PreferenceKeyBase(string name, PreferenceCategory category, Type type)
        {
            Name = name; Category = category; ValueType = type;
        }
    }

    public sealed class PreferenceKey<T> : PreferenceKeyBase
    {
        public T Default { get; }
        public IPreferenceValidator<T> Validator { get; }

        public PreferenceKey(string name, PreferenceCategory category, T def, IPreferenceValidator<T> validator)
            : base(name, category, typeof(T))
        {
            Default = def;
            Validator = validator ?? new NoOpValidator<T>();
        }
    }
}
```

### `PreferenceCatalog` (excerpt)

```csharp
namespace Vianigram.Settings.Catalog
{
    public static class PreferenceKeys
    {
        public static readonly PreferenceKey<ThemeMode> ThemeMode =
            new PreferenceKey<ThemeMode>(
                "appearance.theme_mode", PreferenceCategory.Appearance,
                Domain.ValueObjects.ThemeMode.Auto, new EnumValidator<ThemeMode>());

        public static readonly PreferenceKey<double> FontSize =
            new PreferenceKey<double>(
                "appearance.font_size", PreferenceCategory.Appearance,
                100.0, new RangeValidator(75.0, 200.0));

        public static readonly PreferenceKey<bool> SendOnEnter =
            new PreferenceKey<bool>(
                "chat.send_on_enter", PreferenceCategory.Chat, true, null);

        public static readonly PreferenceKey<bool> AutoPlayAnimatedStickers =
            new PreferenceKey<bool>(
                "chat.auto_play_animated_stickers", PreferenceCategory.Chat, true, null);

        public static readonly PreferenceKey<AutoDownloadPolicy> AutoDownloadCellular =
            new PreferenceKey<AutoDownloadPolicy>(
                "network.auto_download.cellular", PreferenceCategory.Network,
                AutoDownloadPolicy.DefaultCellular, new AutoDownloadPolicyValidator());

        public static readonly PreferenceKey<AutoDownloadPolicy> AutoDownloadWifi =
            new PreferenceKey<AutoDownloadPolicy>(
                "network.auto_download.wifi", PreferenceCategory.Network,
                AutoDownloadPolicy.DefaultWifi, new AutoDownloadPolicyValidator());

        public static readonly PreferenceKey<ProxyConfig> Proxy =
            new PreferenceKey<ProxyConfig>(
                "network.proxy.config", PreferenceCategory.Network,
                ProxyConfig.None, new ProxyConfigValidator());

        public static readonly PreferenceKey<int> PasscodeLockAfterSeconds =
            new PreferenceKey<int>(
                "privacy.passcode_lock_after_seconds", PreferenceCategory.Privacy,
                300, new EnumValidator<int>(new[] { 0, 60, 300, 3600 }));

        public static readonly PreferenceKey<int> StorageCacheMaxMb =
            new PreferenceKey<int>(
                "storage.cache_max_mb", PreferenceCategory.Storage, 300,
                new RangeValidator(50, 2000));

        // ... (~28 keys total)
    }
}
```

---

## 6. Inbound — `ISettingsApi`

```csharp
namespace Vianigram.Settings.Api.V1
{
    public interface ISettingsApi
    {
        Task<Result<T, Error>> GetAsync<T>(PreferenceKey<T> key, CancellationToken ct);
        Task<Result<bool, Error>> SetAsync<T>(PreferenceKey<T> key, T value, CancellationToken ct);
        Task<Result<bool, Error>> ResetCategoryAsync(PreferenceCategory category, CancellationToken ct);
        Task<Result<bool, Error>> ResetAllAsync(CancellationToken ct);
        Task<Result<UserPreferences, Error>> SnapshotAsync(CancellationToken ct);
        Task<Result<ExportResult, Error>> ExportAsync(CancellationToken ct);
        Task<Result<ImportResult, Error>> ImportAsync(string json, CancellationToken ct);
    }
}
```

Usage pattern in consumers:

```csharp
var theme = (await _settings.GetAsync(PreferenceKeys.ThemeMode, ct).ConfigureAwait(false)).ValueOrDefault();
// theme is a ThemeMode (not a string), default Auto if there is no user value.

await _settings.SetAsync(PreferenceKeys.SendOnEnter, false, ct);
// The noop validator passes; it emits PreferenceChanged<bool>.
```

---

## 7. Outbound ports

### `ISettingsStorage`

```csharp
public interface ISettingsStorage
{
    Task<Result<object, Error>> GetAsync(string key, CancellationToken ct);
    Task<Result<bool, Error>> SetAsync(string key, object value, CancellationToken ct);
    Task<Result<bool, Error>> RemoveAsync(string key, CancellationToken ct);
    Task<Result<IReadOnlyDictionary<string, object>, Error>> GetAllAsync(CancellationToken ct);
}
```

Implementation: `LocalSettingsStorage` wraps `Windows.Storage.ApplicationData.Current.LocalSettings.Values`. WP8.1 accepts primitive types directly (string, int, long, double, bool, DateTime, TimeSpan, Guid, byte[], `ApplicationDataCompositeValue`). For composite types (`AutoDownloadPolicy`, `ProxyConfig`), serialize to canonical JSON and store as a string.

### `IPreferenceValidator<T>`

```csharp
public interface IPreferenceValidator<T>
{
    ValidationOutcome Validate(T value);
}
```

Implementations:
- `RangeValidator` (numeric, min/max).
- `EnumValidator<T>` (a discrete set of acceptable values).
- `ProxyConfigValidator` — the Mtproto secret must be 32 hex; the Socks5 host not empty; the port 1–65535.
- `AutoDownloadPolicyValidator` — maxFileSizeMb >= 0, kind matches the policy.

### `ISnapshotExporter` / `ISnapshotImporter`

Canonical JSON (sorted keys, no whitespace). Useful for "Export settings" in the Settings UI and for `diagnostic_dump` in bug reports.

---

## 8. Notable use cases

### `SetPreferenceUseCase<T>`

```csharp
public sealed class SetPreferenceUseCase<T>
{
    private readonly ISettingsStorage _storage;
    private readonly IPreferenceSerializer _serializer;
    private readonly IEventBus _bus;
    private readonly IClock _clock;
    private readonly ILogger _log;

    public async Task<Result<bool, Error>> ExecuteAsync(PreferenceKey<T> key, T value, CancellationToken ct)
    {
        var validation = key.Validator.Validate(value);
        if (validation.IsReject)
        {
            _bus.Publish(new PreferenceValidationFailed(key.Name, validation.Reason));
            return Result.Fail<bool, Error>(SettingsErrors.ValidationFailed(key.Name, validation.Reason));
        }

        var oldRaw = await _storage.GetAsync(key.Name, ct).ConfigureAwait(false);
        var oldValue = oldRaw.IsOk ? _serializer.Deserialize<T>(oldRaw.Value) : key.Default;

        var newRaw = _serializer.Serialize(value);
        var write = await _storage.SetAsync(key.Name, newRaw, ct).ConfigureAwait(false);
        if (!write.IsOk) return write;

        _bus.Publish(new PreferenceChanged<T>(key, oldValue, value, PreferenceSource.User, _clock.UtcNow));
        return Result.Ok<bool, Error>(true);
    }
}
```

### `MigrateSchemaUseCase`

At boot, read `__schema_version__`. If < current, apply the steps in order:

```csharp
public interface IMigrationStep
{
    SchemaVersion From { get; }
    SchemaVersion To { get; }
    Task<Result<int, Error>> ApplyAsync(ISettingsStorage storage, CancellationToken ct);
}
```

A concrete example: `MigrationStep_V1_to_V2`:

```csharp
// V1 had "chat.send_on_enter" (bool); V2 has "chat.send_button_behavior" (enum).
public async Task<Result<int, Error>> ApplyAsync(ISettingsStorage storage, CancellationToken ct)
{
    var old = await storage.GetAsync("chat.send_on_enter", ct).ConfigureAwait(false);
    if (!old.IsOk) return Result.Ok<int, Error>(0);

    var newValue = (bool)old.Value ? "Enter" : "ButtonOnly";
    await storage.SetAsync("chat.send_button_behavior", newValue, ct).ConfigureAwait(false);
    await storage.RemoveAsync("chat.send_on_enter", ct).ConfigureAwait(false);
    return Result.Ok<int, Error>(1);
}
```

---

## 9. Cross-context

Settings is a **source** consumed via an outbound `ISettingsLookup` port defined in other contexts. The adaptation lives in `Vianigram.Composition`:

```
Vianigram.Stickers.Ports.Outbound.IStickerSettingsLookup
    → impl in Composition.Adapters.StickerSettingsAdapter (wraps ISettingsApi)

Vianigram.Notifications.Ports.Outbound.INotificationSettingsLookup
    → impl in Composition.Adapters.NotificationSettingsAdapter

Vianigram.Privacy.Ports.Outbound.IPrivacySettingsLookup
    → impl in Composition.Adapters.PrivacySettingsAdapter
```

Each adapter exposes only the subset of keys that the consuming context needs, mapped to a domain DTO of the context. This preserves rule 3 (anti-corruption).

Events: each context that reacts to changes subscribes to `PreferenceChanged<T>` with a key filter. E.g.:

```csharp
events.Subscribe<PreferenceChanged<ThemeMode>>(e => _theme.Apply(e.NewValue));
events.Subscribe<PreferenceChanged<bool>>(e =>
{
    if (e.Key == PreferenceKeys.AutoPlayAnimatedStickers)
        _stickerPanel.SetAutoPlay(e.NewValue);
});
```

---

## 10. Storage

Primary backend: `LocalSettings` (`ApplicationData.Current.LocalSettings`).

- Synchronous O(1) access, but the exposed API is async for consistency and to allow an alternative backend (file-based for testing or fallback).
- WP8.1 cap: 8 KB per value, 64 KB total for `LocalSettings.Values`. Vianigram V1 is comfortably under it: ~28 keys × ~50 bytes = ~1.5 KB.
- For composite values (`AutoDownloadPolicy`, `ProxyConfig`) that are JSON of ~150 bytes, it still fits.

Fallback: `LocalFolder/preferences.json` when `LocalSettings` returns an error or when `BlobSizePolicy` rejects a value > 8 KB. Policy: if a preference does not fit, log a warning and use the file. In V1 no value exceeds that limit.

`__schema_version__` is persisted as an `int` major*1000+minor (`1001` = v1.1).

---

## 11. Performance

| Operation | Target | Notes |
|---|---|---|
| Get | < 1 ms | LocalSettings is an in-process registry |
| Set | < 5 ms | Includes persist + event publish |
| Snapshot everything | < 20 ms | 28 keys, parallel reading not needed |
| Migrate V1 → V2 | < 100 ms | Once per user |
| Validation | < 0.5 ms | Pure validators |

Hot path: `Get(key)` is called by consumers with a local cache. `Vianigram.App` caches `ThemeMode` and updates only via an event. `Vianigram.Messaging` caches `SendOnEnter`. This reduces the actual calls to `LocalSettings` to O(1) per session except when the event triggers a reload.

---

## 12. Auto-download policy — design

`AutoDownloadPolicy` is evaluated by the media downloader when a message with a photo/video/voice/doc arrives:

```csharp
public sealed class AutoDownloadPolicy
{
    public bool DownloadPhotos { get; }
    public bool DownloadVideos { get; }
    public bool DownloadVoiceMessages { get; }
    public bool DownloadDocuments { get; }
    public long MaxFileSizeBytes { get; }

    public bool ShouldDownload(MediaKind kind, long sizeBytes)
    {
        if (sizeBytes > MaxFileSizeBytes) return false;
        switch (kind)
        {
            case MediaKind.Photo: return DownloadPhotos;
            case MediaKind.Video: return DownloadVideos;
            case MediaKind.Voice: return DownloadVoiceMessages;
            case MediaKind.Document: return DownloadDocuments;
            default: return false;
        }
    }
}
```

The downloader (`Vianigram.Media`) determines the current `NetworkKind` via the `vianium-net` sibling adapter (cellular/wifi/roaming/unknown) and picks the corresponding policy.

---

## 13. Proxy config — invariants

```csharp
public enum ProxyKind { None, Mtproto, Socks5 }

public sealed class ProxyConfig
{
    public ProxyKind Kind { get; }
    public string Host { get; }
    public int Port { get; }
    public string Secret { get; }      // Mtproto: 32 hex chars; Socks5 password
    public string Username { get; }    // Socks5 only

    public static ProxyConfig None = new ProxyConfig(ProxyKind.None, null, 0, null, null);
    public static Result<ProxyConfig, Error> Mtproto(string host, int port, string secret) { /* validate hex 32 */ }
    public static Result<ProxyConfig, Error> Socks5(string host, int port, string user, string pass) { /* validate */ }
}
```

`ProxyConfigValidator` rejects:
- Mtproto without a secret of exactly 32 hex chars (`^[0-9a-fA-F]{32}$`).
- Any kind with a port outside [1, 65535].
- Any non-None kind with an empty host or a non-host (a simple `Uri.CheckHostName` check).

The `vianium-mtproto` sibling reads this value via an outbound adapter each time it opens a new connection (not per-packet).

---

## 14. Open questions

1. **Per-account preferences merge**: if the user has 3 accounts and changes `ThemeMode`, does it apply to all or only the active one? V1 decision: `Application` scope (global). V2 introduces an optional `Account` scope.
2. **Sync with the Telegram cloud**: Telegram has `account.saveAutoDownloadSettings` server-side (cross-device). Mirror our local? Probably yes in V1, fire-and-forget in the background. Tracker in `network.auto_download.*_synced_at`.
3. **The default theme `Auto`** on WP8.1: it requires reading `UISettings.GetColorValue(UIColorType.Background)` and comparing it with white to infer light/dark. The WP8.1 OS supports theme switching (Settings → "Theme: Light/Dark"). Our `Auto` reads at boot and subscribes to `UISettings.ColorValuesChanged`.
4. **i18n language pack**: `LanguagePackId` defines which string pack the UI uses. Telegram has `langpack.getLangPack` to hydrate; the storage of the translated bundle lives in the `Vianigram.I18n` context (future). In V1 English and Spanish are hardcoded.
5. **Encryption of the proxy secret**: the `Mtproto.secret` is not ultra-sensitive but a leak compromises the server. Use `DataProtectionProvider` to wrap it? Decision: yes, persist the wrapped blob. Trade-off: it requires an unwrap on each read; acceptable because the proxy is read when opening a connection, not per-packet.
6. **The diagnostic dump** must redact the passcode hash and the proxy secret before exporting — add to `ISnapshotExporter` a flag `redactSensitive: bool` (default `true` for "Export to share").

---

## 15. Crosslinks

- [00-overview.md](00-overview.md)
- [09-stickers.md](09-stickers.md) — consumes `chat.auto_play_animated_stickers`.
- [10-notifications.md](10-notifications.md) — consumes `notifications.*`.
- [12-search.md](12-search.md) — consumes its own keys (search history).
- [13-privacy.md](13-privacy.md) — read-only on `privacy.passcode_*`.
- [14-presentation.md](14-presentation.md) — the `SettingsPage` + sub-pages as adapters of `ISettingsApi`.
- [15-shell-and-host.md](15-shell-and-host.md) — composition wires the adapters.
