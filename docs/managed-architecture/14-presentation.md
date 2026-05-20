# Presentation — Pages + ViewModels Architecture

> **Required prior reading:** [principles.md](principles.md). Assumes DDD+hex, managed Kernel, the ViewModel-as-adapter pattern (the same as the `vianium-managed-kernel` sibling; this doc is the Vianigram mirror).

## Bounded context

Presentation **is not a traditional bounded context** — it has no aggregate root of its own and no domain events. It is the **inbound adapter layer** that translates UI events (taps, scrolls, typing) into commands for the bounded contexts, and observes domain events to reflect state to the user.

- **Ubiquitous language:** page, view model, command binding, observable collection, navigation frame, data context, user control, bubble (a rendered message), input bar, attachment picker, panel (sticker / emoji), dispatcher.
- **Capabilities:** UI by feature flag (`SettingsPage` shows options according to the enabled capabilities — without a biometric API ⇒ the toggle is hidden; without the secret chats capability ⇒ the entry is hidden).
- **Constraints:** WP8.1 XAML; `INotifyPropertyChanged` binding; `ICommand` for actions; `Dispatcher.RunAsync` for UI updates from the threadpool.

## Goal

Replace the ~26 pages of `PivoraTelegram.App/Pages/` that mix heavy code-behind (`ChatPage.xaml.cs` ~2300 lines, `SettingsPage.xaml.cs` ~900 lines) with:

1. **Thin XAML pages** — only the UI declaration + DataContext + lifecycle handlers that delegate to the VM.
2. **ViewModels as an adapter** — without business logic; only:
   - Inject context APIs (`IMessagingApi`, `IStickersApi`, `INotificationsApi`, `IPrivacyApi`, `ISearchApi`, `ISettingsApi`, `IAuthApi`, `IVoipApi`, etc.).
   - Subscribe to `IEventBus` for domain events.
   - Expose `ObservableCollection<T>`, observable properties, `AsyncCommand`.
   - Map `Api/V1` DTOs to `RowViewModel` for binding.

## Composition of Presentation

```
Vianigram.App (XAML)
    │
    ├─ Pages/
    │   ├─ LoginPage.xaml + .xaml.cs
    │   ├─ QrLoginPage.xaml + .xaml.cs
    │   ├─ ChatListPage.xaml + .xaml.cs
    │   ├─ ChatPage.xaml + .xaml.cs                 (the densest)
    │   ├─ SearchPage.xaml + .xaml.cs
    │   ├─ ContactsPage.xaml + .xaml.cs
    │   ├─ ProfilePage.xaml + .xaml.cs
    │   ├─ EditProfilePage.xaml + .xaml.cs
    │   ├─ GroupInfoPage.xaml + .xaml.cs
    │   ├─ SettingsPage.xaml + .xaml.cs
    │   ├─ ProxySettingsPage.xaml + .xaml.cs
    │   ├─ PasscodePage.xaml + .xaml.cs
    │   ├─ SecretChatPage.xaml + .xaml.cs
    │   ├─ KeyFingerprintPage.xaml + .xaml.cs
    │   ├─ CallPage.xaml + .xaml.cs
    │   ├─ IncomingCallPage.xaml + .xaml.cs
    │   ├─ MediaViewerPage.xaml + .xaml.cs
    │   ├─ ForwardPage.xaml + .xaml.cs
    │   ├─ NewChatPage.xaml + .xaml.cs
    │   ├─ NewChannelPage.xaml + .xaml.cs
    │   ├─ PollPage.xaml + .xaml.cs
    │   ├─ ScheduledPage.xaml + .xaml.cs
    │   ├─ TopicsPage.xaml + .xaml.cs
    │   ├─ AccountSwitcherPage.xaml + .xaml.cs
    │   ├─ ActiveSessionsPage.xaml + .xaml.cs
    │   └─ BlockedUsersPage.xaml + .xaml.cs
    │
    ├─ Controls/                                     (reusable UserControls)
    │   ├─ MessageBubble.xaml + .cs
    │   ├─ PhotoBubble.xaml + .cs
    │   ├─ VoiceBubble.xaml + .cs
    │   ├─ DocumentBubble.xaml + .cs
    │   ├─ StickerBubble.xaml + .cs
    │   ├─ PollBubble.xaml + .cs
    │   ├─ InfoBubble.xaml + .cs                    (system messages: "X joined")
    │   ├─ ReactionBar.xaml + .cs
    │   ├─ ReplyBar.xaml + .cs
    │   ├─ TypingIndicator.xaml + .cs
    │   ├─ MediaPicker.xaml + .cs
    │   ├─ AudioRecorder.xaml + .cs
    │   ├─ EmojiPanel.xaml + .cs
    │   ├─ StickerPanel.xaml + .cs
    │   ├─ ChatListItem.xaml + .cs
    │   ├─ AvatarCircle.xaml + .cs
    │   ├─ UnreadBadge.xaml + .cs
    │   └─ DateSeparator.xaml + .cs
    │
    └─ Each Page's DataContext → a ViewModel:
         │
         ▼
Vianigram.ViewModels (sibling assembly)
    ├─ BaseViewModel.cs           ← INotifyPropertyChanged + SetProperty + DispatchOnUiAsync
    ├─ AsyncCommand.cs            ← ICommand async (typed + non-typed)
    ├─ Common/
    │   ├─ ProgressViewModel.cs
    │   ├─ ErrorBannerViewModel.cs
    │   └─ ConfirmDialogViewModel.cs
    │
    ├─ Auth/
    │   ├─ LoginPageViewModel.cs
    │   ├─ QrLoginPageViewModel.cs
    │   └─ AccountSwitcherViewModel.cs
    │
    ├─ Chats/
    │   ├─ ChatListPageViewModel.cs
    │   ├─ ChatListItemViewModel.cs
    │   ├─ ChatPageViewModel.cs                     (orchestrator)
    │   ├─ MessageBubbleViewModel.cs
    │   ├─ MessageInputViewModel.cs
    │   ├─ TypingViewModel.cs
    │   ├─ ReactionBarViewModel.cs
    │   └─ ReplyBarViewModel.cs
    │
    ├─ Contacts/
    │   ├─ ContactsPageViewModel.cs
    │   ├─ NewChatPageViewModel.cs
    │   └─ NewChannelPageViewModel.cs
    │
    ├─ Profiles/
    │   ├─ ProfilePageViewModel.cs
    │   ├─ EditProfilePageViewModel.cs
    │   └─ GroupInfoPageViewModel.cs
    │
    ├─ Search/
    │   └─ SearchPageViewModel.cs
    │
    ├─ Settings/
    │   ├─ SettingsPageViewModel.cs
    │   ├─ ProxySettingsViewModel.cs
    │   ├─ PasscodePageViewModel.cs
    │   ├─ ActiveSessionsViewModel.cs
    │   └─ BlockedUsersViewModel.cs
    │
    ├─ Calls/
    │   ├─ CallPageViewModel.cs
    │   └─ IncomingCallPageViewModel.cs
    │
    ├─ Media/
    │   ├─ MediaViewerViewModel.cs
    │   └─ MediaPickerViewModel.cs
    │
    ├─ Stickers/
    │   ├─ StickerPanelViewModel.cs
    │   └─ EmojiPanelViewModel.cs
    │
    ├─ Secret/
    │   ├─ SecretChatPageViewModel.cs
    │   └─ KeyFingerprintViewModel.cs
    │
    └─ Forward/
        └─ ForwardPageViewModel.cs
```

## Rules for ViewModels (in order of criticality)

1. **Zero business logic.** If an operation has validation rules or invariants, it lives in `Application/UseCases` of the corresponding context.
2. **Only dependencies on the contexts' `Api/V1`** + `Vianigram.Kernel` + the BCL. **Never** reference a context's `Domain/`, `Application/`, `Infrastructure/`.
3. **`AsyncCommand`** for everything. No `void` event handlers in code-behind except those of the Page lifecycle (`Loaded`, `OnNavigatedTo`, `OnNavigatedFrom`) and the back button.
4. **EventBus subscription** in the constructor; **`Dispose`** in the `Page.OnNavigatedFrom` handler.
5. **`Dispatcher.RunAsync`** when an event handler coming from the threadpool updates bound properties. The `DispatchOnUiAsync` helper is in `BaseViewModel`.
6. **`ObservableCollection<T>` mutations** only on the UI thread.
7. **Properties exposed to the UI are `record`-like (read-only)**, mutation via `SetProperty(ref _field, value)`.
8. **No Dispatcher capture** in the domain or use cases; the VM is the boundary.
9. **`async void` only in WP8.1 event handlers** (Loaded, Click). Any other async returns a `Task` even if the caller does not await it.
10. **`.Result` and `.Wait()` are absolutely prohibited.** If a constructor needs an async value, fire `_ = LoadInitialAsync();` and show a skeleton.

## `BaseViewModel`

```csharp
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.UI.Core;
using Windows.ApplicationModel.Core;

namespace Vianigram.ViewModels
{
    public abstract class BaseViewModel : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private readonly List<IDisposable> _subscriptions = new List<IDisposable>();
        private bool _disposed;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            var h = PropertyChanged;
            if (h != null) h(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        protected void RegisterSubscription(IDisposable sub) { _subscriptions.Add(sub); }

        protected Task DispatchOnUiAsync(Action work)
        {
            var dispatcher = CoreApplication.MainView.CoreWindow.Dispatcher;
            if (dispatcher.HasThreadAccess) { work(); return Task.FromResult(true); }
            return dispatcher.RunAsync(CoreDispatcherPriority.Normal, () => work()).AsTask();
        }

        public virtual void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var s in _subscriptions) { try { s.Dispose(); } catch { } }
            _subscriptions.Clear();
        }
    }
}
```

## Pages → ViewModels mapping

| Page | Main ViewModel | Sub-VMs / UserControls used | Primary commands | Subscribed events |
|---|---|---|---|---|
| `LoginPage` | `LoginPageViewModel` | `ProgressViewModel` | `RequestCodeCommand`, `SubmitCodeCommand`, `RequestCallCommand` | `AuthCodeRequested`, `AuthSuccess`, `AuthFailed` |
| `QrLoginPage` | `QrLoginPageViewModel` | — | `RefreshQrCommand`, `CancelCommand` | `QrTokenIssued`, `QrTokenAccepted`, `AuthSuccess` |
| `ChatListPage` | `ChatListPageViewModel` | `ChatListItemViewModel`, `UnreadBadge`, `AvatarCircle` | `OpenChatCommand`, `OpenSearchCommand`, `NewChatCommand`, `OpenSettingsCommand` | `DialogOrderChanged`, `IncomingMessageReceived`, `DialogReadAtChanged`, `BadgeUpdated` |
| `ChatPage` | `ChatPageViewModel` | `MessageBubble`, `PhotoBubble`, `VoiceBubble`, `DocumentBubble`, `StickerBubble`, `PollBubble`, `InfoBubble`, `ReactionBar`, `ReplyBar`, `TypingIndicator`, `MessageInputViewModel`, `MediaPicker`, `AudioRecorder`, `EmojiPanel`, `StickerPanel`, `DateSeparator` | `SendTextCommand`, `SendStickerCommand`, `SendMediaCommand`, `SendVoiceCommand`, `LoadEarlierCommand`, `MarkReadCommand`, `ToggleStickerPanelCommand`, `OpenAttachmentPickerCommand`, `OpenChatInfoCommand` | `IncomingMessageReceived`, `OutgoingMessageDelivered`, `MessageReactionsUpdated`, `TypingStarted`, `TypingStopped`, `DialogPinnedChanged` |
| `SearchPage` | `SearchPageViewModel` | `ChatListItemViewModel` (peer hits), `MessageBubble` (msg hits) | `SubmitQueryCommand`, `LoadNextPageCommand`, `ChangeFilterCommand`, `ClearHistoryCommand` | `SearchPageLoaded`, `SearchCompleted`, `SearchFailed` |
| `ContactsPage` | `ContactsPageViewModel` | `ChatListItemViewModel`, `AvatarCircle` | `OpenContactCommand`, `InviteContactCommand`, `RefreshCommand` | `ContactsImported`, `ContactStatusChanged` |
| `ProfilePage` | `ProfilePageViewModel` | `AvatarCircle` | `OpenChatCommand`, `BlockUserCommand`, `EditCommand`, `ShareContactCommand` | `UserProfileChanged`, `UserBlocked`, `UserUnblocked` |
| `EditProfilePage` | `EditProfilePageViewModel` | `AvatarCircle` | `SavePhotoCommand`, `SaveBioCommand`, `SaveUsernameCommand` | `ProfileSaved` |
| `GroupInfoPage` | `GroupInfoPageViewModel` | `AvatarCircle`, member list | `AddMemberCommand`, `LeaveCommand`, `MuteCommand`, `OpenMemberCommand`, `EditTitleCommand` | `GroupTitleChanged`, `MemberAdded`, `MemberRemoved`, `MuteRuleChanged` |
| `SettingsPage` | `SettingsPageViewModel` | `SettingCategoryViewModel` | `OpenSubpageCommand`, `LogoutCommand`, `ClearCacheCommand` | `PreferenceChanged<T>` (various) |
| `ProxySettingsPage` | `ProxySettingsViewModel` | — | `SaveProxyCommand`, `TestProxyCommand`, `RemoveProxyCommand` | `ProxyTested` |
| `PasscodePage` | `PasscodePageViewModel` | — | `SubmitPasscodeCommand`, `EnableBiometricCommand`, `ForgotPasscodeCommand` | `PasscodeFailedAttempt`, `PasscodeUnlocked`, `PasscodeLocked` |
| `SecretChatPage` | `SecretChatPageViewModel` | the same set as `ChatPage` | the same + `ViewKeyFingerprintCommand`, `SetSelfDestructTtlCommand` | `SecretChatRekeyed`, `SecretChatDiscarded` |
| `KeyFingerprintPage` | `KeyFingerprintViewModel` | — | `CompareCommand` | — |
| `CallPage` | `CallPageViewModel` | — | `MuteToggleCommand`, `SpeakerToggleCommand`, `HangupCommand` | `CallStateChanged`, `CallQualityUpdated` |
| `IncomingCallPage` | `IncomingCallPageViewModel` | `AvatarCircle` | `AcceptCommand`, `DeclineCommand` | `CallEnded` |
| `MediaViewerPage` | `MediaViewerViewModel` | — | `SaveCommand`, `ForwardCommand`, `ShareCommand`, `DeleteCommand` | `MediaDownloadProgress` |
| `ForwardPage` | `ForwardPageViewModel` | `ChatListItemViewModel` | `SelectTargetCommand`, `SubmitForwardCommand` | — |
| `NewChatPage` | `NewChatPageViewModel` | `ChatListItemViewModel` | `ToggleParticipantCommand`, `CreateGroupCommand` | — |
| `NewChannelPage` | `NewChannelPageViewModel` | — | `CreateChannelCommand`, `CheckUsernameCommand` | `UsernameAvailabilityChecked` |
| `PollPage` | `PollPageViewModel` | — | `AddOptionCommand`, `RemoveOptionCommand`, `SubmitPollCommand` | — |
| `ScheduledPage` | `ScheduledPageViewModel` | `MessageBubble` | `EditCommand`, `SendNowCommand`, `DeleteCommand` | `ScheduledMessageEdited`, `ScheduledMessageSent` |
| `TopicsPage` | `TopicsPageViewModel` | `ChatListItemViewModel` (topic hits) | `OpenTopicCommand`, `CreateTopicCommand` | `TopicCreated`, `TopicMessageNew` |
| `AccountSwitcherPage` | `AccountSwitcherViewModel` | `AvatarCircle`, `UnreadBadge` | `SwitchAccountCommand`, `AddAccountCommand`, `LogoutAccountCommand` | `AccountSwitched`, `AccountAdded`, `AccountRemoved` |
| `ActiveSessionsPage` | `ActiveSessionsViewModel` | — | `TerminateSessionCommand`, `TerminateAllOtherCommand`, `RefreshCommand` | `ActiveSessionTerminated`, `AllOtherSessionsTerminated` |
| `BlockedUsersPage` | `BlockedUsersViewModel` | `ChatListItemViewModel`, `AvatarCircle` | `UnblockUserCommand`, `LoadMoreCommand` | `UserBlocked`, `UserUnblocked` |

## UserControls (16+) — the pattern

Each UserControl is a **passive view** without logic:

- `MessageBubble` receives a `MessageBubbleViewModel` (`Sender`, `Body`, `SentUtc`, `IsOutgoing`, `Status`, `IsEdited`).
- `PhotoBubble` receives a `PhotoBubbleViewModel` (extends `MessageBubbleViewModel` with `Thumbnail`, `FullPhotoSource`, `DownloadProgress`).
- `VoiceBubble` receives a `VoiceBubbleViewModel` (`WaveformPoints`, `Duration`, `PlayCommand`, `PausCommand`, `Position`).
- `StickerBubble` receives a `StickerBubbleViewModel` (`StaticSource`, `AnimatedHandle`, `IsPlaying`).
- `PollBubble` receives a `PollBubbleViewModel` (`Options`, `MyVote`, `IsClosed`, `VoteCommand`).
- `ReactionBar` receives a `ReactionBarViewModel` (`Reactions`, `MyReaction`, `ToggleReactionCommand`).
- `ReplyBar` receives a `ReplyBarViewModel` (`OriginalSender`, `OriginalSnippet`, `MediaIcon`, `JumpToOriginalCommand`).
- `TypingIndicator` receives a `TypingViewModel` (`TypingUsers`).
- `MediaPicker` receives a `MediaPickerViewModel` — a popup that enumerates the device's photos/videos via `Windows.Storage.Pickers`.
- `AudioRecorder` receives an `AudioRecorderViewModel` (`StartRecordCommand`, `StopRecordCommand`, `Duration`, `WaveformPoints`).
- `EmojiPanel` receives an `EmojiPanelViewModel` (categories + emoji grid + search).
- `StickerPanel` receives a `StickerPanelViewModel` (packs tab strip + sticker grid).
- `ChatListItem` receives a `ChatListItemViewModel` (`PeerName`, `LastMessageSnippet`, `UnreadCount`, `IsMuted`, `IsVerified`, `IsPinned`, `LastMessageDate`).
- `AvatarCircle` receives `(string photoUri, string initials, Color fallbackColor)`.
- `UnreadBadge` receives `(int count, bool isMuted)` — a count > 99 shows "99+".
- `DateSeparator` receives `(DateTime localDate)` — a format according to the day (Today / Yesterday / Mon / dd MMM / dd MMM yyyy).
- `InfoBubble` receives `(string text)` — system messages: "X joined", "Group renamed".

Rules for UserControls:
- A DependencyProperty for the VM (when applicable as a DOM property).
- Code-behind only `InitializeComponent()` + a Loaded handler that optionally wakes up animations.
- **No business logic** — everything comes from the bound VM.
- **No singletons accessed directly**.

## Page lifecycle pattern

```csharp
public sealed partial class ChatPage : Page
{
    private ChatPageViewModel _vm;

    public ChatPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        var peerId = (long)e.Parameter;
        _vm = ((App)Application.Current).Composition.Resolve<Func<long, ChatPageViewModel>>()(peerId);
        DataContext = _vm;
        Windows.Phone.UI.Input.HardwareButtons.BackPressed += OnBack;
        base.OnNavigatedTo(e);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Windows.Phone.UI.Input.HardwareButtons.BackPressed -= OnBack;
        DataContext = null;
        if (_vm != null) { _vm.Dispose(); _vm = null; }
        base.OnNavigatedFrom(e);
    }

    private async void OnBack(object sender, Windows.Phone.UI.Input.BackPressedEventArgs e)
    {
        if (_vm == null) return;
        if (_vm.CanCollapsePanel)
        {
            e.Handled = true;
            await _vm.CollapsePanelCommand.ExecuteAsync(null);
        }
    }
}
```

Total: ~30 lines. Any additional code must live in the VM.

## Example — `ChatListPageViewModel`

```csharp
namespace Vianigram.ViewModels.Chats
{
    public sealed class ChatListPageViewModel : BaseViewModel
    {
        private readonly IDialogsApi _dialogs;
        private readonly INotificationsApi _notifications;
        private readonly INavigationServicePort _nav;
        private readonly IEventBus _events;
        private readonly ILogger _log;

        public ObservableCollection<ChatListItemViewModel> Chats { get; } = new ObservableCollection<ChatListItemViewModel>();

        private int _badge;
        public int Badge { get { return _badge; } private set { SetProperty(ref _badge, value); } }

        private bool _isRefreshing;
        public bool IsRefreshing { get { return _isRefreshing; } private set { SetProperty(ref _isRefreshing, value); } }

        public AsyncCommand RefreshCommand { get; }
        public AsyncCommand OpenSearchCommand { get; }
        public AsyncCommand OpenSettingsCommand { get; }
        public AsyncCommand<long> OpenChatCommand { get; }
        public AsyncCommand NewChatCommand { get; }

        public ChatListPageViewModel(
            IDialogsApi dialogs,
            INotificationsApi notifications,
            INavigationServicePort nav,
            IEventBus events,
            ILogger log)
        {
            _dialogs = dialogs; _notifications = notifications; _nav = nav; _events = events; _log = log;

            RefreshCommand = new AsyncCommand(RefreshAsync);
            OpenSearchCommand = new AsyncCommand(() => { _nav.NavigateTo("Search", null); return Task.FromResult(true); });
            OpenSettingsCommand = new AsyncCommand(() => { _nav.NavigateTo("Settings", null); return Task.FromResult(true); });
            OpenChatCommand = new AsyncCommand<long>(peerId => { _nav.NavigateTo("Chat", peerId); return Task.FromResult(true); });
            NewChatCommand = new AsyncCommand(() => { _nav.NavigateTo("NewChat", null); return Task.FromResult(true); });

            RegisterSubscription(_events.Subscribe<DialogOrderChanged>(async e => await DispatchOnUiAsync(() => OnOrderChanged(e))));
            RegisterSubscription(_events.Subscribe<IncomingMessageReceived>(async e => await DispatchOnUiAsync(() => OnIncoming(e))));
            RegisterSubscription(_events.Subscribe<BadgeUpdated>(async e => await DispatchOnUiAsync(() => Badge = e.NewCount)));

            _ = LoadInitialAsync();
        }

        private async Task LoadInitialAsync()
        {
            var snapshot = await _dialogs.ListAsync(limit: 30, CancellationToken.None).ConfigureAwait(true);
            if (!snapshot.IsOk) { _log.Warn("ChatList load failed: " + snapshot.Error); return; }

            await DispatchOnUiAsync(() =>
            {
                Chats.Clear();
                foreach (var d in snapshot.Value)
                    Chats.Add(new ChatListItemViewModel(d));
            });

            var badge = await _notifications.GetBadgeCountAsync(CancellationToken.None).ConfigureAwait(true);
            await DispatchOnUiAsync(() => Badge = badge.IsOk ? badge.Value : 0);
        }

        private async Task RefreshAsync()
        {
            IsRefreshing = true;
            try { await LoadInitialAsync().ConfigureAwait(true); }
            finally { IsRefreshing = false; }
        }

        private void OnOrderChanged(DialogOrderChanged e) { /* re-sort the observable */ }
        private void OnIncoming(IncomingMessageReceived e) { /* update the last message snippet of the matching item */ }
    }
}
```

## Banned anti-patterns

1. **Business logic in code-behind.** If you need an `if`, it probably goes in the use case.
2. **`.Result` and `.Wait()` over a `Task`.** Generates a deadlock on the WP8.1 UI thread.
3. **`async void`** outside event handlers (Loaded, Click, etc.). For everything else return a `Task`.
4. **Singleton access inside a VM.** `App.Current.Foo.Bar` does not appear. Inject by constructor.
5. **Direct `new ChatPageViewModel()`.** Always resolve from Composition.
6. **`Dispatcher` referenced in use cases / domain.** The VM is the threading boundary.
7. **`ObservableCollection` mutated on the threadpool.** Throws an exception in XAML. Use `DispatchOnUiAsync`.
8. **A VM exposing `Domain` types** (entities, internal value objects). Only `Api/V1` DTOs and `RowViewModel`s.
9. **Capturing a `CancellationToken` cross-page.** Each page has its CTS; on `Dispose`, it is cancelled.
10. **Code-behind > 100 lines.** A custom linter rejects it.

## Phases

### Phase 1 — `BaseViewModel` + `AsyncCommand` (week 1)

Establish `Vianigram.ViewModels` with `BaseViewModel`, `AsyncCommand<T>`, the `DispatchOnUiAsync` helper, unit tests.

### Phase 2 — Auth pages (week 2)

`LoginPageViewModel`, `QrLoginPageViewModel`, `AccountSwitcherViewModel`. Thin pages. Tested with mocks of `IAuthApi`.

### Phase 3 — ChatList + primary navigation (week 3)

`ChatListPageViewModel`, `ChatListItemViewModel`. `INavigationServicePort` wired. Hardware back delegation.

### Phase 4 — ChatPage (weeks 4–7) — the big one

`ChatPageViewModel` + 12 sub-VMs: a bubble per type, the input, the sticker panel, the emoji panel, the audio recorder, the media picker, the reply bar, the reaction bar, the typing indicator, a scroll virtualization helper. The orchestrator pattern.

### Phase 5 — Settings + Privacy pages (week 8)

`SettingsPageViewModel`, `ProxySettingsViewModel`, `PasscodePageViewModel`, `BlockedUsersViewModel`, `ActiveSessionsViewModel`.

### Phase 6 — Search + Contacts + Profiles (week 9)

`SearchPageViewModel`, `ContactsPageViewModel`, `ProfilePageViewModel`, `EditProfilePageViewModel`, `GroupInfoPageViewModel`.

### Phase 7 — Calls + secret + media viewer (week 10)

`CallPageViewModel`, `IncomingCallPageViewModel`, `SecretChatPageViewModel`, `KeyFingerprintViewModel`, `MediaViewerViewModel`.

### Phase 8 — Forward + new chat/channel + polls + scheduled + topics (week 11)

`ForwardPageViewModel`, `NewChatPageViewModel`, `NewChannelPageViewModel`, `PollPageViewModel`, `ScheduledPageViewModel`, `TopicsPageViewModel`.

### Phase 9 — Polish + memory leaks audit + a11y (week 12)

Verify that all the `Dispose` calls are made. Audit that `WeakEventManager` is not needed (subscriptions disposed). A screen reader pass.

## Cross-context dependencies

ViewModels reference **only the `Api/V1`** of each context:

- `Vianigram.Auth.Api.V1`
- `Vianigram.Messaging.Api.V1`
- `Vianigram.Dialogs.Api.V1`
- `Vianigram.Stickers.Api.V1`
- `Vianigram.Notifications.Api.V1`
- `Vianigram.Settings.Api.V1`
- `Vianigram.Search.Api.V1`
- `Vianigram.Privacy.Api.V1`
- `Vianium.VoIP.Api.V1` (from the sibling vianium-voip repo)
- `Vianigram.SecretChats.Api.V1`
- `Vianigram.Media.Api.V1`
- `Vianigram.Contacts.Api.V1`

Plus `Vianigram.Kernel` (events, logging, clock) and `Vianigram.Shell.Navigation.INavigationServicePort` to navigate between pages.

## Testing strategy

### Unit (ViewModel)

- Construct the VM with port mocks (the `Api/V1` interfaces).
- Fire commands → verify the mock was called with the correct args.
- Publish an event on the bus → verify the VM updated the observable property.
- `INotifyPropertyChanged` raised correctly.
- Subscriptions disposed on `Dispose()`.
- Cancellation: navigate-away mid-async ⇒ no UI mutation.

### Smoke E2E (Pages)

- A test app deployable to the WP8.1 emulator.
- Load a Page, verify the DataContext is bound.
- Tap commands, verify the UI reacts.
- Navigate-away → navigate-back: verify the VM is reconstructed with fresh state (or restores a snapshot if the page has cache enabled).

## Risks

1. **Threading bugs** if a VM updates a property on the threadpool without the Dispatcher → XAML throws. Mitigation: the `DispatchOnUiAsync` helper.
2. **Memory leak** if subscriptions are not disposed in `OnNavigatedFrom`. Mitigation: `RegisterSubscription` + automatic `Dispose`.
3. **Slow startup** if a VM does many awaits in the constructor. Mitigation: `LoadInitialAsync` fire-and-forget; show a skeleton/loading.
4. **Code-behind creep** if new devs do not understand the pattern. Mitigation: code review + a custom linter (no `*.xaml.cs` may have > 100 lines).
5. **WP8.1 navigation cache** retains Pages → a stale DataContext. Mitigation: `OnNavigatedFrom` explicitly releases the DataContext.
6. **Fragile `ChatPage` orchestration**: 12 sub-VMs with crossed events. Mitigation: the `ChatPageViewModel` is the owner and the sub-VMs receive sub-sections of the state by constructor; no "searching in Application.Current".
7. **Bubble virtualization**: a WP8.1 `ListView` with 5000 messages ⇒ memory. Use an `IncrementalLoadingCollection<T>` + recycle visual containers.

## Crosslinks

- [principles.md](principles.md)
- [00-overview.md](00-overview.md)
- [09-stickers.md](09-stickers.md), [10-notifications.md](10-notifications.md), [11-settings.md](11-settings.md), [12-search.md](12-search.md), [13-privacy.md](13-privacy.md) — the APIs that these VMs consume.
- [15-shell-and-host.md](15-shell-and-host.md) — Composition wires VMs + APIs.