// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ProfilePageViewModel.cs — profile page VM (read-only profile of self/other).
// Wires IContactsApi (block/unblock), ICallsApi (request voice/video call),
// IAccountApi (self profile via GetSelfAsync), and INavigationService
// for SendMessage / Edit / Call routes.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Ports.Inbound;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Domain.ValueObjects;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Kernel.Result;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.ValueObjects;
using Vianigram.Notifications.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class ProfilePageViewModel : ObservableObject
    {
        private readonly IContactsApi _contacts;
        private readonly ICallsApi _calls;
        private readonly IAccountApi _account;
        private readonly INotificationsApi _notifications;
        private readonly INavigationService _nav;

        private string _displayName;
        private string _username;
        private string _phone;
        private string _bio;
        private string _avatarLetter;
        private bool _isSelf;
        private bool _canCall;
        private bool _isBlocked;
        private bool _notificationsEnabled;
        private bool _suppressNotificationWrite;
        private bool _preferBackToChat;
        private string _statusText;
        private string _errorMessage;
        private bool _isBusy;
        private long _userId;
        private string _peerKey;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public ProfilePageViewModel()
            : this(null, null, null, null, null)
        {
        }

        public ProfilePageViewModel(
            IContactsApi contacts,
            ICallsApi calls,
            IAccountApi account,
            INotificationsApi notifications,
            INavigationService nav)
        {
            _contacts = contacts;
            _calls = calls;
            _account = account;
            _notifications = notifications;
            _nav = nav;

            // Empty seed: real data is filled by HydrateSelfAsync /
            // HydrateContactAsync / HydrateFromPeerCache in OnNavigatedTo.
            // Previously the page seeded a dummy "Mira Sato" profile that
            // remained visible whenever those hydrators failed or the user
            // wasn't in our contact list — confusing UX.
            _displayName = string.Empty;
            _username = string.Empty;
            _phone = string.Empty;
            _bio = string.Empty;
            _avatarLetter = "?";
            _statusText = string.Empty;
            _canCall = calls == null || calls.IsCallingAvailable;
            _notificationsEnabled = true;

            SendMessageCommand = new RelayCommand(_ => OnSendMessage(), _ => true);
            VoiceCallCommand = new AsyncCommand(_ => StartCallAsync(false), _ => CanCall && !IsBlocked);
            VideoCallCommand = new AsyncCommand(_ => StartCallAsync(true), _ => CanCall && !IsBlocked);
            MuteCommand = new AsyncCommand(_ => ToggleNotificationsAsync(), _ => !IsSelf && !string.IsNullOrEmpty(_peerKey));
            BlockCommand = new AsyncCommand(_ => ToggleBlockAsync(), _ => !IsSelf);
            EditCommand = new RelayCommand(_ => OnEdit(), _ => IsSelf);
        }

        // ---- Display fields ------------------------------------------

        public string DisplayName
        {
            get { return _displayName; }
            set
            {
                if (SetProperty(ref _displayName, value))
                    AvatarLetter = CreateInitials(_displayName);
            }
        }

        public string Username
        {
            get { return _username; }
            set { SetProperty(ref _username, NormalizeUsername(value)); }
        }

        public string Phone
        {
            get { return _phone; }
            set { SetProperty(ref _phone, value); }
        }

        public string Bio
        {
            get { return _bio; }
            set { SetProperty(ref _bio, value); }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        // Avatar bitmap shared with the chat list. When the peer is
        // already in the avatar fetcher's bitmap cache (because the user
        // has seen them in the dialog list) this assignment is
        // synchronous and the AvatarCircle renders the real photo
        // instead of initials. Nullable — initials stay visible until
        // the bitmap arrives.
        private Windows.UI.Xaml.Media.ImageSource _avatarImage;
        public Windows.UI.Xaml.Media.ImageSource AvatarImage
        {
            get { return _avatarImage; }
            private set { SetProperty(ref _avatarImage, value); }
        }

        public string StatusText
        {
            get { return _statusText; }
            set { SetProperty(ref _statusText, value); }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    OnPropertyChanged("HasError");
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_errorMessage); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        // ---- Capability flags ----------------------------------------

        public bool IsSelf
        {
            get { return _isSelf; }
            set
            {
                if (SetProperty(ref _isSelf, value))
                {
                    OnPropertyChanged("ShowOtherActions");
                    OnPropertyChanged("ShowSelfActions");
                    ((AsyncCommand)MuteCommand).RaiseCanExecuteChanged();
                    ((AsyncCommand)BlockCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)EditCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanCall
        {
            get { return _canCall; }
            set
            {
                if (SetProperty(ref _canCall, value))
                {
                    ((AsyncCommand)VoiceCallCommand).RaiseCanExecuteChanged();
                    ((AsyncCommand)VideoCallCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsBlocked
        {
            get { return _isBlocked; }
            set
            {
                if (SetProperty(ref _isBlocked, value))
                {
                    OnPropertyChanged("BlockButtonLabel");
                    ((AsyncCommand)VoiceCallCommand).RaiseCanExecuteChanged();
                    ((AsyncCommand)VideoCallCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool NotificationsEnabled
        {
            get { return _notificationsEnabled; }
            set
            {
                if (SetProperty(ref _notificationsEnabled, value))
                    OnPropertyChanged("MuteActionLabel");
            }
        }

        public string MuteActionLabel
        {
            get { return _notificationsEnabled ? Strings.Get("ProfileMuteAction") : Strings.Get("ProfileUnmuteAction"); }
        }

        public string BlockButtonLabel
        {
            get { return _isBlocked ? "Unblock User" : "Block User"; }
        }

        public bool ShowOtherActions { get { return !_isSelf; } }
        public bool ShowSelfActions { get { return _isSelf; } }

        public long UserId
        {
            get { return _userId; }
            set { _userId = value; }
        }

        // ---- Commands -------------------------------------------------

        public ICommand SendMessageCommand { get; private set; }
        public ICommand VoiceCallCommand { get; private set; }
        public ICommand VideoCallCommand { get; private set; }
        public ICommand MuteCommand { get; private set; }
        public ICommand BlockCommand { get; private set; }
        public ICommand EditCommand { get; private set; }

        // ---- Navigation lifecycle ------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            ProfilePageNavArgs args = parameter as ProfilePageNavArgs;
            if (args != null)
            {
                _peerKey = args.PeerKey ?? string.Empty;
                IsSelf = args.IsSelf;
                UserId = args.UserId;
                if (!string.IsNullOrEmpty(args.DisplayName)) DisplayName = args.DisplayName;
                if (!string.IsNullOrEmpty(args.StatusText)) StatusText = args.StatusText;
                if (!string.IsNullOrEmpty(args.Username)) Username = args.Username;
                if (!string.IsNullOrEmpty(args.Phone)) Phone = args.Phone;
                if (!string.IsNullOrEmpty(args.Bio)) Bio = args.Bio;
                _preferBackToChat = args.PreferBackToChat;
            }
            else
            {
                _peerKey = parameter as string;
                _preferBackToChat = false;
                if (parameter == null) IsSelf = true;
            }

            if (!string.IsNullOrEmpty(_peerKey) && args == null)
            {
                StatusText = "last seen recently";
            }
            if (UserId <= 0) UserId = TryParseUserId(_peerKey);
            RefreshCallAvailability();
            ((AsyncCommand)MuteCommand).RaiseCanExecuteChanged();

            if (IsSelf)
            {
                var ignore = HydrateSelfAsync(CancellationToken.None);
            }
            else
            {
                // Always seed from the peer cache first so we have *some*
                // real data while the contact-list lookup is in flight.
                // The cache is hydrated by every typed RPC response (dialog
                // list, history fetch, push update), so for any peer the
                // user has interacted with we get at least the display name
                // here without a network round-trip.
                HydrateFromPeerCache();
                var contactIgnore = HydrateContactAsync(CancellationToken.None);
                var ignore = HydrateNotificationsAsync(CancellationToken.None);
            }

            // Avatar hydration is independent of the self/other branch —
            // both paths can resolve an image from the shared
            // PeerAvatarFetcher cache. Fire-and-forget: on success the
            // AvatarCircle.Image binding replaces the initials with the
            // real photo; on failure the initials remain visible.
            var avatarIgnore = HydrateAvatarAsync(CancellationToken.None);
        }

        /// <summary>
        /// Resolve the small (160×160) avatar bitmap for this peer
        /// reusing the same fetcher / disk cache the ChatList uses, so
        /// a photo already downloaded for the dialog row renders here
        /// without an extra round-trip. Null assignment keeps the
        /// initials-only placeholder.
        /// </summary>
        private async Task HydrateAvatarAsync(CancellationToken ct)
        {
            try
            {
                Windows.UI.Xaml.Media.ImageSource bmp = null;

                if (IsSelf && _userId > 0)
                {
                    bmp = await AvatarResolver
                        .TryResolveSmallAsync(Vianigram.Media.Domain.ValueObjects.PeerPhotoKind.User, _userId, ct)
                        .ConfigureAwait(true);
                }
                else if (!string.IsNullOrEmpty(_peerKey))
                {
                    bmp = await AvatarResolver
                        .TryResolveSmallAsync(_peerKey, ct)
                        .ConfigureAwait(true);
                }

                if (bmp != null) AvatarImage = bmp;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.ProfilePage").Warn("HydrateAvatarAsync threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void HydrateFromPeerCache()
        {
            if (_userId <= 0) return;
            try
            {
                Vianigram.Composition.Infrastructure.IPeerCache cache = null;
                if (App.Composition != null)
                {
                    App.Composition.TryResolve<Vianigram.Composition.Infrastructure.IPeerCache>(out cache);
                }
                if (cache == null) return;
                string display = cache.GetUserDisplayName(_userId);
                if (!string.IsNullOrEmpty(display) && string.IsNullOrEmpty(DisplayName))
                {
                    DisplayName = display;
                }
            }
            catch (Exception ex)
            {
                AppLog.For("App.ProfilePage").Error("HydrateFromPeerCache threw: " + ex);
            }
        }

        private async Task HydrateSelfAsync(CancellationToken ct)
        {
            if (_account == null)
            {
                ErrorMessage = "Account service not available.";
                return;
            }

            Result<SelfProfile, AccountError> result;
            try
            {
                result = await _account.GetSelfAsync(ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.ProfilePage").Error("GetSelfAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatAccountError(result.Error);
                return;
            }

            SelfProfile p = result.Value;
            if (p == null) return;

            string first = p.FirstName ?? string.Empty;
            string last = p.LastName ?? string.Empty;
            string display = (first + " " + last).Trim();
            DisplayName = display.Length == 0 ? "Vianigram User" : display;
            Username = p.Username ?? string.Empty;
            Phone = p.Phone ?? string.Empty;
            Bio = p.Bio ?? string.Empty;
            UserId = p.UserId;

            string letterSource = display.Length > 0 ? display : "?";
            AvatarLetter = CreateInitials(letterSource);
        }

        private async Task HydrateNotificationsAsync(CancellationToken ct)
        {
            if (_notifications == null || string.IsNullOrEmpty(_peerKey)) return;

            try
            {
                Result<MuteRule, NotificationsError> result =
                    await _notifications.GetMuteAsync(_peerKey, ct).ConfigureAwait(true);
                if (result.IsFail) return;

                _suppressNotificationWrite = true;
                try
                {
                    MuteRule rule = result.Value;
                    NotificationsEnabled = rule == null || !rule.IsMutedAt(DateTime.UtcNow);
                }
                finally
                {
                    _suppressNotificationWrite = false;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.ProfilePage").Error("GetMuteAsync threw: " + ex);
            }
        }

        private async Task HydrateContactAsync(CancellationToken ct)
        {
            if (_contacts == null || _userId <= 0) return;

            try
            {
                Result<IList<Contact>, ContactsError> result =
                    await _contacts.GetContactsAsync(ct).ConfigureAwait(true);
                if (result.IsFail || result.Value == null) return;

                IList<Contact> contacts = result.Value;
                for (int i = 0; i < contacts.Count; i++)
                {
                    Contact c = contacts[i];
                    if (c == null || c.UserId.Value != _userId) continue;

                    if (!string.IsNullOrEmpty(c.DisplayName)) DisplayName = c.DisplayName;
                    if (!string.IsNullOrEmpty(c.Username)) Username = c.Username;
                    if (!string.IsNullOrEmpty(c.Phone)) Phone = c.Phone;
                    IsBlocked = c.IsBlocked;
                    return;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.ProfilePage").Error("GetContactsAsync threw: " + ex);
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Command handlers ----------------------------------------

        private void OnSendMessage()
        {
            if (_nav == null) return;
            if (_preferBackToChat && _nav.CanGoBack)
            {
                _nav.GoBack();
                return;
            }
            if (string.IsNullOrEmpty(_peerKey)) return;
            _nav.NavigateTo(Route.Chat, _peerKey);
        }

        private void OnEdit()
        {
            if (_nav != null) _nav.NavigateTo(Route.EditProfile);
        }

        public Task UpdateNotificationsEnabledAsync(bool enabled)
        {
            if (_suppressNotificationWrite) return CompletedTask();
            return SetNotificationsEnabledAsync(enabled);
        }

        private Task ToggleNotificationsAsync()
        {
            return SetNotificationsEnabledAsync(!NotificationsEnabled);
        }

        private async Task SetNotificationsEnabledAsync(bool enabled)
        {
            bool previous = NotificationsEnabled;
            NotificationsEnabled = enabled;

            if (_notifications == null || string.IsNullOrEmpty(_peerKey))
            {
                if (!enabled)
                    ErrorMessage = "Notifications service not available.";
                return;
            }

            ErrorMessage = null;
            try
            {
                MuteRule rule = enabled
                    ? MuteRule.DefaultFor(_peerKey)
                    : MuteRule.MutedForever(_peerKey);

                Result<Vianigram.Notifications.Domain.ValueObjects.Unit, NotificationsError> result =
                    await _notifications.SetMuteAsync(_peerKey, rule, CancellationToken.None).ConfigureAwait(true);
                if (result.IsFail)
                {
                    NotificationsEnabled = previous;
                    ErrorMessage = FormatNotificationsError(result.Error);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                NotificationsEnabled = previous;
                AppLog.For("App.ProfilePage").Error("SetMuteAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
            }
        }

        private async Task StartCallAsync(bool video)
        {
            ErrorMessage = null;
            if (_calls == null)
            {
                ErrorMessage = "Calls service not available.";
                return;
            }
            if (_userId <= 0)
            {
                ErrorMessage = "Cannot place call: unknown user.";
                return;
            }
            if (!_calls.IsCallingAvailable)
            {
                ErrorMessage = FormatCallingUnavailable(_calls.CallingUnavailableReason);
                return;
            }

            IsBusy = true;
            try
            {
                Result<CallSession, CallError> result;
                try
                {
                    result = await _calls.RequestCallAsync(_userId, video, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }
                if (result.IsFail)
                {
                    ErrorMessage = FormatCallError(result.Error);
                    return;
                }
                if (_nav != null && result.Value != null)
                {
                    _nav.NavigateTo(Route.Call, result.Value.CallId);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ToggleBlockAsync()
        {
            ErrorMessage = null;
            if (_contacts == null)
            {
                ErrorMessage = "Contacts service not available.";
                return;
            }
            if (_userId <= 0)
            {
                ErrorMessage = "Cannot block: unknown user.";
                return;
            }

            IsBusy = true;
            try
            {
                Result<Vianigram.Contacts.Domain.ValueObjects.Unit, ContactsError> result;
                try
                {
                    if (_isBlocked)
                    {
                        result = await _contacts.UnblockAsync(_userId, CancellationToken.None).ConfigureAwait(true);
                    }
                    else
                    {
                        result = await _contacts.BlockAsync(_userId, CancellationToken.None).ConfigureAwait(true);
                    }
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }
                if (result.IsFail)
                {
                    ErrorMessage = FormatContactsError(result.Error);
                    return;
                }
                IsBlocked = !IsBlocked;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string FormatContactsError(ContactsError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ContactsErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case ContactsErrorKind.PermissionDenied:
                    return "Permission denied.";
                case ContactsErrorKind.NotFound:
                    return "User not found.";
                case ContactsErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }

        private static string FormatAccountError(AccountError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case AccountErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case AccountErrorKind.PhoneNumberFlood:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }

        private static string FormatCallError(CallError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case CallErrorKind.NetworkError:
                    return "Network error.";
                case CallErrorKind.ParticipantUnavailable:
                    return "User unavailable.";
                case CallErrorKind.Busy:
                    return "User is busy.";
                case CallErrorKind.AlreadyInCall:
                    return "Already in a call.";
                case CallErrorKind.MediaPlaneFailed:
                    return FormatCallingUnavailable(error.Message);
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }

        private void RefreshCallAvailability()
        {
            if (_calls == null) return;
            CanCall = _calls.IsCallingAvailable;
        }

        private static string FormatCallingUnavailable(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "Calls are not available in this build.";
            return "Calls are not available in this build: " + reason + ".";
        }

        private static string FormatNotificationsError(NotificationsError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case NotificationsErrorKind.NetworkError:
                    return "Network error.";
                case NotificationsErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                case NotificationsErrorKind.PlatformDenied:
                    return "Notifications are disabled by the platform.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }

        private static string NormalizeUsername(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string trimmed = value.Trim();
            return trimmed.StartsWith("@", StringComparison.Ordinal) ? trimmed : ("@" + trimmed);
        }

        private static string CreateInitials(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName)) return "?";
            string[] parts = displayName.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1)
            {
                string single = parts[0];
                if (single.Length <= 1) return single.ToUpperInvariant();
                return single.Substring(0, 2).ToUpperInvariant();
            }
            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        private static long TryParseUserId(string peerKey)
        {
            if (string.IsNullOrEmpty(peerKey)) return 0;
            const string prefix = "user:";
            if (!peerKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 0;
            long id;
            return long.TryParse(peerKey.Substring(prefix.Length), out id) ? id : 0;
        }

        private static Task CompletedTask()
        {
            return Task.FromResult(0);
        }
    }

    public sealed class ProfilePageNavArgs
    {
        public string PeerKey { get; set; }
        public string DisplayName { get; set; }
        public string StatusText { get; set; }
        public string Username { get; set; }
        public string Phone { get; set; }
        public string Bio { get; set; }
        public long UserId { get; set; }
        public bool IsSelf { get; set; }
        public bool PreferBackToChat { get; set; }
    }
}
