# Vianigram.Notifications — Toast + Tile + Push Bounded Context

> **Required prior reading:** [principles.md](principles.md), [00-overview.md](00-overview.md). Assumes DDD + hexagonal + managed Kernel. This context covers **the entire user-notification surface**: in-app toasts, system toasts (when the app is not in the foreground), live tiles, badge counts, and the registration of the WP8.1 push channel against Telegram (`account.registerDevice`). It cooperates closely with [15-shell-and-host.md](15-shell-and-host.md) (the `Vianigram.Agent` background task), which is the one that receives the raw push and delivers it here.

---

## 1. Bounded context

- **Ubiquitous language:** notification, toast, tile, badge, push channel, push payload, mute, custom sound, vibration pattern, notification scope (global / per-chat / per-user / per-mention), notify exception, alert, scheduled tile, primary tile, secondary tile, push registration, channel URI, expiry.
- **Aggregate root:** `NotificationProfile` — the active user's global notification configuration + the list of per-chat exceptions (mute, custom sound, custom vibration). One per account.
- **Secondary aggregates:**
  - `BadgeState` — the total unread counter (chats with new unread messages). Updated by `Vianigram.Messaging` via an event and by sync with `getDifference`.
  - `PushSubscription` — the active channel URI + Telegram token + the last time it was renewed.
- **Value objects:**
  - `NotificationScope` — `Global`, `PrivateChats`, `GroupChats`, `Channels`, `Chat(peerId)`, `User(userId)`.
  - `MuteRule` — `(NotificationScope, MuteUntil DateTime?, ShowPreview bool, PlaySound bool, SoundFile string, Vibrate bool)`. `MuteUntil = null` ⇒ not muted; `MuteUntil = MaxValue` ⇒ muted forever.
  - `NotifyException` — `(PeerId, MuteRule)` — a per-chat override over the global.
  - `BadgeCount` — `int`, clamped to [0, 99]. Above 99 it shows "99+".
  - `ToastPayload` — `(Title, Body, IconUri, ChatPeerId, MessageId, ReceivedUtc)`.
  - `TileTemplate` — discriminated: `WideTileTemplate(BackgroundUri, RecentMessages[])`, `MediumTileTemplate(UnreadCount)`, `SmallTileTemplate(BadgeCount)`.
  - `ChannelUri` — `string` (the WNS URI assigned by Microsoft).
  - `TelegramPushToken` — `(Token bytes, AppSandbox 0/1)`. Telegram associates it with the device.
- **Domain events emitted:**
  - `IncomingMessageNotified(PeerId, MessageId)` — generated on firing a toast.
  - `MuteRuleChanged(scope, MuteRule old, MuteRule new)`.
  - `BadgeUpdated(int from, int to)`.
  - `TileRefreshed(TileTemplate template)`.
  - `PushChannelRegistered(ChannelUri uri, DateTime utc)`.
  - `PushChannelExpired(DateTime utc)`.
  - `PushChannelReregistered(ChannelUri oldUri, ChannelUri newUri)`.
  - `NotificationSuppressed(reason)` — silenced by mute, by focus on the current chat, by do-not-disturb.
- **Capabilities exposed:**
  - `notifications.toasts` — enabled by default.
  - `notifications.tile_updates` — primary tile updates.
  - `notifications.badge_updates` — badge count on the lock screen / start.
  - `notifications.custom_sound_per_chat` — the UI allows attaching an `.mp3`/`.wma` per-chat.
  - `notifications.preview_in_lockscreen` — privacy: if off, the toast only says "New message" without a body.
  - `notifications.scheduled_tile` — tile rotation (every N seconds the background changes).
  - `notifications.mention_only_in_groups` — only notify for groups if you are mentioned / replied to.

---

## 2. Goal

Replace `PivoraTelegram.App/Services/NotificationService.cs` (~400 lines) and `PivoraTelegram.Agent/PushTask.cs` with a clean context that:

1. **Is idempotent with respect to the notification source**: both `Vianigram.Agent` (when a raw push arrives) and `Vianigram.Messaging` (when a message arrives via sockets in the foreground) produce a toast through the same route — both invoke the same use case `RaiseToastUseCase`.
2. **Honors mute rules** consistently. Today there are three different places where mute is checked (UI bubble, push handler, badge count) and they diverge.
3. **Exposes the "what to show" logic as a domain service** (`ToastFormatter`) — the body format, truncate to 80 characters, sticker → "🖼 Sticker", voice → "🎙 Voice 0:08".
4. **Persists the push subscription robustly**: WP8.1 invalidates the `ChannelUri` when it expires or when the OS changes (re-OOBE, reset). Detect it and re-register against Telegram with `account.registerDevice` automatically.
5. **Synchronizes notify exceptions with the server** (`account.updateNotifySettings` / `account.getNotifyExceptions`). The user expects that mute on mobile = mute on desktop.
6. **Cooperation with `Vianigram.Agent`**: the background task does not build UI toasts; it delivers the decoded `PushPayload` to an API that lives in this context, and this context decides what to do with it.

---

## 3. C# baseline (PivoraTelegram)

`PivoraTelegram.App/Services/NotificationService.cs`:

- An `Instance` singleton, mixed with `App.Current` references.
- A `void ShowToast(string title, string body)` that internally does `ToastNotificationManager.CreateToastNotifier().Show(...)` directly with hardcoded XML → `XmlDocument doc = new XmlDocument(); doc.LoadXml("<toast><visual><binding template='ToastText02'>...");`. Impossible to test without the emulator.
- Mute persisted in `IsolatedStorageSettings.ApplicationSettings` with ad-hoc keys: `"mute_chat_12345"` (DateTime ticks as a long). A manual parse on each check.
- A `BadgeNumericNotificationContent` is built on each incoming message with `App.Current.Dispatcher.BeginInvoke` — race conditions when two messages arrive in <50ms.
- No `account.getNotifyExceptions` sync — desktop and mobile diverge.
- `PivoraTelegram.Agent/PushTask.cs` parses the push payload manually with `string.Split` over Telegram's wire format (`MIME-encoded JSON`); it fails with strings that have a `,` in the body.

---

## 4. Native target — the `Vianigram.Notifications` project

```
Core/Vianigram.Notifications/
├── Vianigram.Notifications.csproj             (WP8.1, NETFX_CORE, WINDOWS_PHONE_APP)
├── Properties/AssemblyInfo.cs
│
├── Domain/
│   ├── ValueObjects/
│   │   ├── NotificationScope.cs
│   │   ├── MuteRule.cs
│   │   ├── NotifyException.cs
│   │   ├── BadgeCount.cs
│   │   ├── ToastPayload.cs
│   │   ├── TileTemplate.cs
│   │   ├── ChannelUri.cs
│   │   └── TelegramPushToken.cs
│   ├── Aggregates/
│   │   ├── NotificationProfile.cs
│   │   ├── BadgeState.cs
│   │   └── PushSubscription.cs
│   ├── Events/
│   │   ├── IncomingMessageNotified.cs
│   │   ├── MuteRuleChanged.cs
│   │   ├── BadgeUpdated.cs
│   │   ├── TileRefreshed.cs
│   │   ├── PushChannelRegistered.cs
│   │   ├── PushChannelExpired.cs
│   │   ├── PushChannelReregistered.cs
│   │   └── NotificationSuppressed.cs
│   ├── Services/
│   │   ├── ToastFormatter.cs                  (message → truncated text + emoji prefix)
│   │   ├── MuteDecisionService.cs             (resolves global vs exception vs scope)
│   │   ├── BadgeAggregator.cs                 (sums counts per chat + clamp)
│   │   ├── TileTemplateBuilder.cs             (builds wide/medium/small XML)
│   │   └── DoNotDisturbScheduler.cs           (silent hours)
│   ├── Policies/
│   │   ├── PreviewInLockscreenPolicy.cs       (privacy gate)
│   │   ├── MentionOnlyInGroupsPolicy.cs
│   │   └── PushExpiryPolicy.cs                (re-register if older than 28 days)
│   └── Errors/
│       └── NotificationErrors.cs
│
├── Application/
│   ├── Commands/
│   │   ├── RaiseToastCommand.cs
│   │   ├── UpdateBadgeCommand.cs
│   │   ├── RefreshTileCommand.cs
│   │   ├── SetMuteCommand.cs
│   │   ├── RegisterPushCommand.cs
│   │   └── SyncNotifyExceptionsCommand.cs
│   ├── Queries/
│   │   ├── GetMuteRuleQuery.cs
│   │   ├── ListNotifyExceptionsQuery.cs
│   │   └── GetBadgeCountQuery.cs
│   ├── UseCases/
│   │   ├── RaiseToastUseCase.cs
│   │   ├── UpdateBadgeUseCase.cs
│   │   ├── RefreshTileUseCase.cs
│   │   ├── SetMuteRuleUseCase.cs
│   │   ├── RegisterPushChannelUseCase.cs
│   │   ├── HandlePushPayloadUseCase.cs        (cooperates with Vianigram.Agent)
│   │   ├── SyncNotifyExceptionsUseCase.cs
│   │   └── HandleAppFocusChangedUseCase.cs    (suppress when a chat is open)
│   └── Internal/
│       ├── ToastEmitter.cs
│       ├── TileEmitter.cs
│       └── BadgeEmitter.cs
│
├── Ports/
│   ├── Inbound/
│   │   └── INotificationsApi.cs
│   └── Outbound/
│       ├── IToastSink.cs                      (wraps ToastNotificationManager)
│       ├── ITileSink.cs                       (wraps TileUpdater)
│       ├── IBadgeSink.cs                      (wraps BadgeUpdater)
│       ├── IPushChannelSource.cs              (wraps PushNotificationChannelManager)
│       ├── INotificationsTlGateway.cs         (account.registerDevice, getNotifyExceptions, ...)
│       ├── INotificationStorage.cs            (LocalSettings + LocalFolder)
│       └── IClock.cs                          (re-export from the Kernel)
│
├── Infrastructure/
│   ├── Sinks/
│   │   ├── ToastNotificationSink.cs           (XmlDocument templates + ToastNotifier)
│   │   ├── TileTemplateSink.cs                (TileUpdater + ScheduledTileNotification)
│   │   └── BadgeNumericSink.cs                (BadgeUpdater)
│   ├── Push/
│   │   ├── WnsPushChannelSource.cs            (PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync)
│   │   └── PushPayloadParser.cs               (Telegram push wire format → ToastPayload)
│   ├── Tl/
│   │   ├── TlNotificationsGateway.cs          (account.registerDevice, etc.)
│   │   └── TlNotifyExceptionMapper.cs
│   ├── Persistence/
│   │   ├── LocalSettingsNotificationStorage.cs
│   │   └── NotificationProfileSnapshot.cs
│   └── Localization/
│       └── LocalizedToastStrings.cs           (i18n for "New message", "🎙 Voice 0:08")
│
└── Api/
    └── V1/
        ├── INotificationsApi.cs
        ├── ToastRequest.cs
        ├── MuteRequest.cs
        ├── BadgeUpdateRequest.cs
        ├── PushPayloadDto.cs
        └── NotificationApiErrors.cs
```

---

## 5. Inbound port — `INotificationsApi`

```csharp
namespace Vianigram.Notifications.Api.V1
{
    public interface INotificationsApi
    {
        Task<Result<bool, Error>> RaiseToastAsync(ToastRequest req, CancellationToken ct);
        Task<Result<bool, Error>> SetMuteAsync(MuteRequest req, CancellationToken ct);
        Task<Result<MuteRule, Error>> GetMuteRuleAsync(NotificationScope scope, CancellationToken ct);
        Task<Result<IReadOnlyList<NotifyException>, Error>> ListExceptionsAsync(CancellationToken ct);
        Task<Result<bool, Error>> UpdateBadgeAsync(int newCount, CancellationToken ct);
        Task<Result<bool, Error>> RefreshTileAsync(CancellationToken ct);
        Task<Result<bool, Error>> RegisterPushAsync(CancellationToken ct);
        Task<Result<bool, Error>> HandlePushPayloadAsync(PushPayloadDto payload, CancellationToken ct);
        Task<Result<bool, Error>> SyncNotifyExceptionsAsync(CancellationToken ct);
        Task<Result<bool, Error>> NotifyAppFocusChangedAsync(long? activeChatPeerId, CancellationToken ct);
    }
}
```

---

## 6. Outbound ports — WP8.1 specifics

### `IToastSink`

```csharp
public interface IToastSink
{
    Task<Result<bool, Error>> ShowAsync(ToastPayload payload, CancellationToken ct);
}
```

The implementation (`ToastNotificationSink`) builds the XML of the `ToastImageAndText02` template:

```xml
<toast launch="vianigram://chat/{peerId}/{messageId}">
  <visual>
    <binding template="ToastImageAndText02">
      <image id="1" src="ms-appdata:///local/avatars/{peerId}.jpg"/>
      <text id="1">{senderName}</text>
      <text id="2">{body}</text>
    </binding>
  </visual>
  <audio src="ms-appx:///Assets/Sounds/{soundFile}"/>
</toast>
```

The `launch` URI activates the app and is parsed in `App.OnLaunched` to navigate to the correct chat. This is documented in [15-shell-and-host.md](15-shell-and-host.md).

### `ITileSink`

```csharp
public interface ITileSink
{
    Task<Result<bool, Error>> UpdatePrimaryAsync(TileTemplate template, CancellationToken ct);
    Task<Result<bool, Error>> ClearAsync(CancellationToken ct);
}
```

Builds XML for `TileWide310x150ImageAndText01`, `TileSquare150x150Text04`, etc. WP8.1 exposes `TileUpdateManager.CreateTileUpdaterForApplication()`. For rotation, build up to 5 `ScheduledTileNotification` with increasing time offsets.

### `IBadgeSink`

```csharp
public interface IBadgeSink
{
    Task<Result<bool, Error>> SetCountAsync(int count, CancellationToken ct);
    Task<Result<bool, Error>> ClearAsync(CancellationToken ct);
}
```

`BadgeUpdateManager.CreateBadgeUpdaterForApplication().Update(new BadgeNotification(xml))` with the template `BadgeNumeric`. Clamp to [0, 99].

### `IPushChannelSource`

```csharp
public interface IPushChannelSource
{
    Task<Result<ChannelUri, Error>> CreateOrRefreshAsync(CancellationToken ct);
    event EventHandler<RawPushPayloadEventArgs> RawPushReceived;     // foreground
    Task<Result<bool, Error>> CloseAsync(CancellationToken ct);
}
```

WP8.1 API: `PushNotificationChannelManager.CreatePushNotificationChannelForApplicationAsync()`. Returns a `PushNotificationChannel` with a `Uri` (string). Subscribe to `PushNotificationReceived` for the foreground; the background task receives it via its `BackgroundTaskRegistration` contract.

### `INotificationsTlGateway`

```csharp
public interface INotificationsTlGateway
{
    Task<Result<bool, Error>> RegisterDeviceAsync(int tokenType, string token, bool noMuted, CancellationToken ct);
    Task<Result<bool, Error>> UnregisterDeviceAsync(int tokenType, string token, CancellationToken ct);
    Task<Result<TlNotifySettings, Error>> GetNotifySettingsAsync(NotificationScope scope, CancellationToken ct);
    Task<Result<bool, Error>> UpdateNotifySettingsAsync(NotificationScope scope, MuteRule rule, CancellationToken ct);
    Task<Result<IReadOnlyList<NotifyException>, Error>> GetNotifyExceptionsAsync(bool compareSound, CancellationToken ct);
}
```

`tokenType` for WNS = `8` per the TL schema (subject to change; verify in the active MTProto layer).

---

## 7. WP8.1 specifics — APIs and constraints

### Toast notifications

- API: `Windows.UI.Notifications.ToastNotificationManager`.
- Available templates: `ToastText01..04`, `ToastImageAndText01..04`. Vianigram uses `ToastImageAndText02` for an incoming message (avatar + sender + body).
- Toast lifetime: until the user dismisses it or ~5 seconds. WP8.1 does NOT support toast actions (inline Reply / Mark as Read) — a platform limitation. Tap → `App.OnLaunched(activationKind = ToastNotification)`.
- Priority: WP8.1 does not expose a high/low Priority. Every toast is `Default`.
- Sound: `<audio src="ms-appx:///Assets/Sounds/{soundFile}.wma"/>`. WP8.1 supports `.wma`, `.wav`, `.mp3` packaged in Assets. Cap: 30 seconds. A custom sound per-chat copies the file to `LocalFolder` and references it with `ms-appdata:///local/sounds/{file}`.

### Tile updates

- API: `Windows.UI.Notifications.TileUpdateManager`.
- Templates: `TileWide310x150ImageAndText01`, `TileSquare150x150Text04`, `TileSquare71x71Text04` (small).
- Live tile rotation: up to 5 templates queued, the OS rotates them every ~5 seconds. Use: in the wide tile, show the last 4 messages.
- Cyclic update: via `ScheduledTileNotification` with `DeliveryTime = utcNow.AddSeconds(N)`.

### Badge

- API: `Windows.UI.Notifications.BadgeUpdateManager`.
- Type: `BadgeNumeric` (0–99) or `BadgeGlyph` (`alert`, `available`, `away`, etc.). Vianigram uses numeric.
- The badge is NOT preserved between re-installs; it is set fresh at boot.

### WP8.1 push channel

- API: `Windows.Networking.PushNotifications.PushNotificationChannelManager`.
- `CreatePushNotificationChannelForApplicationAsync()` → a `PushNotificationChannel` with a `.Uri` property (the WNS channel URI).
- Channel TTL: ~30 days, after which it invalidates silently. Mitigation: re-create at boot if > 28 days since the last register.
- Foreground reception: subscribe to the `channel.PushNotificationReceived` event. WP8.1 delivers a `PushNotificationReceivedEventArgs` with a `RawNotification` (Telegram uses a raw notification) or a `ToastNotification` (a direct WNS toast, not used by Telegram).
- Background reception: register a `BackgroundTaskBuilder` with a `PushNotificationTrigger`. The task is `Vianigram.Agent` (see [15-shell-and-host.md](15-shell-and-host.md)).
- Token format: the WNS channel URI is an HTTPS URL (~200 characters). Telegram receives it as a string in `account.registerDevice`.

### Background task budget

WP8.1 background tasks have a hard budget:
- CPU: ~2 seconds.
- Wall-clock: ~25 seconds.
- Memory: ~16 MB on low-memory devices; ~75 MB on high-memory.

This means that `Vianigram.Agent` cannot do a full getDifference on each push; it is limited to:
1. Parsing the push payload.
2. Calling `INotificationsApi.HandlePushPayloadAsync` (which only emits the toast using data from the payload, without the network).
3. If the payload brings `silent=false`, also fire `BadgeUpdated`.
4. Return. The full message sync is done when the foreground app starts.

---

## 8. Notable use cases

### `RaiseToastUseCase`

```csharp
public sealed class RaiseToastUseCase
{
    private readonly IToastSink _toast;
    private readonly NotificationProfile _profile;
    private readonly ToastFormatter _formatter;
    private readonly MuteDecisionService _muteDecision;
    private readonly IEventBus _bus;
    private readonly IClock _clock;

    public async Task<Result<bool, Error>> ExecuteAsync(ToastRequest req, CancellationToken ct)
    {
        var scope = NotificationScope.ForChat(req.PeerId);
        var muteRule = _muteDecision.Resolve(_profile, scope);
        if (muteRule.IsMutedAt(_clock.UtcNow))
        {
            _bus.Publish(new NotificationSuppressed("muted"));
            return Result.Ok<bool, Error>(false);
        }

        if (req.IsAppForegroundOnSameChat)
        {
            _bus.Publish(new NotificationSuppressed("focus"));
            return Result.Ok<bool, Error>(false);
        }

        var payload = _formatter.Format(req, muteRule);
        var sent = await _toast.ShowAsync(payload, ct).ConfigureAwait(false);
        if (sent.IsOk && sent.Value)
            _bus.Publish(new IncomingMessageNotified(req.PeerId, req.MessageId));

        return sent;
    }
}
```

### `HandlePushPayloadUseCase`

Fired by the `Vianigram.Agent` background task. Parses the payload, decodes the sender + body from TL, fires `RaiseToastUseCase`, returns. Everything synchronous and fast (< 1 second).

---

## 9. Cross-context dependencies

| Outbound | Implemented by | Doc |
|---|---|---|
| `IToastSink`, `ITileSink`, `IBadgeSink` | Its own adapters in `Infrastructure/Sinks` | self |
| `IPushChannelSource` | Its own `WnsPushChannelSource` | self |
| `INotificationsTlGateway` | `Vianigram.Composition.Adapters.TlNotificationsGatewayAdapter` which uses the sibling `vianium-mtproto` (`src\tl\`) | [15-shell-and-host.md](15-shell-and-host.md) |
| `INotificationStorage` | Its own `LocalSettingsNotificationStorage` | self |

Events consumed:
- `Vianigram.Messaging` publishes `IncomingMessageReceived(peerId, messageId, senderName, body, isMention)` in the foreground; this context consumes it → `RaiseToastUseCase`.
- `Vianigram.Auth` publishes `AccountSwitched`; this context switches profiles (each account has its `NotificationProfile`).
- `Vianigram.Sync` publishes `UnreadCountChanged(peerId, newCount)`; this context recalculates the total badge.

Published events consumed by:
- `Vianigram.App` (presentation) listens to `BadgeUpdated` to refresh the `UnreadBadge` UserControl.
- `Vianigram.Agent` (host) listens to `PushChannelExpired` to register a new background task.

---

## 10. Storage

`LocalSettings` (`ApplicationData.Current.LocalSettings`):

| Key | Type | Meaning |
|---|---|---|
| `notifications.profile.global.mute_until` | `long` (ticks) | Global mute expiry |
| `notifications.profile.global.show_preview` | `bool` | Privacy preview toggle |
| `notifications.profile.global.sound_file` | `string` | Default sound |
| `notifications.profile.private.*` / `groups.*` / `channels.*` | the same schema | Per-scope override |
| `notifications.exceptions` | `string` (JSON) | The list of `NotifyException` (cap ~50, then go to LocalFolder) |
| `notifications.push.channel_uri` | `string` | The last registered channel URI |
| `notifications.push.last_register_utc` | `long` | Timestamp for the expiry policy |
| `notifications.badge.last_count` | `int` | Persisted badge for a fresh boot |

If the `notifications.exceptions` JSON grows above 8 KB (a reasonable LocalSettings cap), spill it to `LocalFolder/notifications/exceptions.json`.

---

## 11. Performance

| Metric | Target | Notes |
|---|---|---|
| Toast emit (in-process) | < 30 ms | XML build + `ToastNotifier.Show` |
| Push payload parse | < 10 ms | The typical payload is < 2 KB |
| Push → toast visible | < 250 ms | the background task budget minus parse minus sink |
| Badge update | < 15 ms | Simple XML |
| Tile refresh (wide+medium+small) | < 80 ms | Three sinks, parallel not needed |
| Mute decision | < 1 ms | An in-memory Dictionary lookup |

---

## 12. TL methods consumed

| Method | Use | When |
|---|---|---|
| `account.registerDevice` | Associate the channel URI with the account | Boot, channel refresh, after login |
| `account.unregisterDevice` | Clean up on logout | Logout |
| `account.updateNotifySettings` | Sync mute to the server | A user mute toggle |
| `account.getNotifyExceptions` | Sync mute from the server | Boot, on resume |
| `account.getNotifySettings` | Read scope settings | Settings page open |

The assumed schema layer = MTProto layer 158+ (modern Telegram). The TL gateway abstracts any schema difference.

---

## 13. Open questions

1. **WNS rate limits**: WNS allows ~1000 push/day per channel. Telegram normally respects it. Implement a defensive local throttle? Probably not — if Telegram exceeds it, it is a backend bug.
2. **Multi-account badge**: if there are 3 accounts, does the badge show the total sum or only that of the primary account? Decision: the total sum with a tooltip (visible on long-press of the tile) that details it per-account.
3. **WP8.1 Cortana integration**: there are `VoiceCommandService` APIs that allow "Cortana, send message to Mom in Vianigram". Out of V1; a placeholder in the `notifications.cortana_integration` capability.
4. **Lockscreen badge** vs **Start tile badge**: WP8.1 separates them. Vianigram must register for both (`Package.appxmanifest` declares `LockScreenNotification = "badge"`).
5. **Toast deduplication**: if 5 messages arrive in 2 seconds, 5 toasts or one consolidated "5 new messages"? The official Telegram behavior: consolidated after the 3rd. Implementation: ToastFormatter detects if the last toast for the same peer was < 5s ago → replaces it with a consolidated one.
6. **Persistence of the badge** during an OS reboot: the OS resets the badge to 0 on reboot. Mitigation: at boot, recalculate the badge from `Vianigram.Sync` and reapply it.

---

## 14. Crosslinks

- [00-overview.md](00-overview.md)
- [11-settings.md](11-settings.md) — the capability toggles and the sound file selection.
- [13-privacy.md](13-privacy.md) — the passcode lock interacts with preview-in-lockscreen.
- [14-presentation.md](14-presentation.md) — the `UnreadBadge` UserControl bound to `BadgeState`.
- [15-shell-and-host.md](15-shell-and-host.md) — the `Vianigram.Agent` push background task.
