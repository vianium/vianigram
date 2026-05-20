// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// GroupInfoPageViewModel.cs — group / channel info page VM.
// Wires IChatsApi (GetGroupInfoAsync / LeaveAsync), INotificationsApi (mute),
// and INavigationService (add members / edit / leave routes).

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Kernel.Result;
using Vianigram.Notifications.Domain;
using Vianigram.Notifications.Domain.ValueObjects;
using Vianigram.Notifications.Ports.Inbound;
using ChatsUnit = Vianigram.Chats.Domain.ValueObjects.Unit;

namespace Vianigram.App.ViewModels.Pages
{
    /// <summary>Row-level binding shape for a single group member.</summary>
    public sealed class MemberVm : ObservableObject
    {
        private string _displayName;
        private string _avatarLetter;
        private bool _isAdmin;

        public string DisplayName
        {
            get { return _displayName; }
            set { SetProperty(ref _displayName, value); }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        public bool IsAdmin
        {
            get { return _isAdmin; }
            set
            {
                if (SetProperty(ref _isAdmin, value))
                    OnPropertyChanged("AdminLabel");
            }
        }

        public string AdminLabel
        {
            get { return _isAdmin ? "admin" : string.Empty; }
        }
    }

    public sealed class GroupInfoPageViewModel : ObservableObject
    {
        private readonly IChatsApi _chats;
        private readonly INotificationsApi _notifications;
        private readonly INavigationService _nav;

        private string _title;
        private string _description;
        private int _memberCount;
        private bool _isAdmin;
        private bool _isMuted;
        private string _errorMessage;
        private bool _isBusy;
        private string _peerKey;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public GroupInfoPageViewModel()
            : this(null, null, null)
        {
        }

        public GroupInfoPageViewModel(IChatsApi chats, INotificationsApi notifications, INavigationService nav)
        {
            _chats = chats;
            _notifications = notifications;
            _nav = nav;

            _title = string.Empty;
            _description = string.Empty;

            Members = new ObservableCollection<MemberVm>();

            AddMembersCommand = new RelayCommand(_ => OnAddMembers(), _ => true);
            EditCommand = new RelayCommand(_ => OnEdit(), _ => IsAdmin);
            ToggleMuteCommand = new AsyncCommand(_ => ToggleMuteAsync(), _ => true);
            LeaveCommand = new AsyncCommand(_ => LeaveAsync(), _ => true);
        }

        // ---- Display fields ------------------------------------------

        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public string Description
        {
            get { return _description; }
            set { SetProperty(ref _description, value); }
        }

        public int MemberCount
        {
            get { return _memberCount; }
            set
            {
                if (SetProperty(ref _memberCount, value))
                    OnPropertyChanged("MemberCountLabel");
            }
        }

        public string MemberCountLabel
        {
            get
            {
                if (_memberCount <= 0) return "Group chat";
                if (_memberCount == 1) return "1 member";
                return _memberCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " members";
            }
        }

        public ObservableCollection<MemberVm> Members { get; private set; }

        public bool IsAdmin
        {
            get { return _isAdmin; }
            set
            {
                if (SetProperty(ref _isAdmin, value))
                {
                    OnPropertyChanged("CanEdit");
                    ((RelayCommand)EditCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanEdit { get { return _isAdmin; } }

        public bool IsMuted
        {
            get { return _isMuted; }
            set
            {
                if (SetProperty(ref _isMuted, value))
                    OnPropertyChanged("MuteButtonLabel");
            }
        }

        public string MuteButtonLabel
        {
            get { return _isMuted ? "Unmute" : "Mute"; }
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

        // ---- Commands -------------------------------------------------

        public ICommand AddMembersCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public ICommand ToggleMuteCommand { get; private set; }
        public ICommand LeaveCommand { get; private set; }

        // ---- Navigation lifecycle ------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            _peerKey = parameter as string;
            var ignore = HydrateAsync(CancellationToken.None);
        }

        private async Task HydrateAsync(CancellationToken ct)
        {
            if (_chats == null)
            {
                ErrorMessage = "Chats service not available.";
                return;
            }
            PeerId peer = ParsePeerKey(_peerKey);
            if (peer == null) return;

            Result<GroupInfo, ChatError> result;
            try
            {
                result = await _chats.GetGroupInfoAsync(peer, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.GroupInfoPage").Error("GetGroupInfoAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatChatError(result.Error);
                return;
            }

            GroupInfo info = result.Value;
            if (info == null) return;

            Title = info.Title ?? string.Empty;
            Description = info.Description ?? string.Empty;
            MemberCount = info.MemberCount;
            IsAdmin = info.IsAdmin || info.IsCreator;

            Members.Clear();
            if (info.Members != null)
            {
                for (int i = 0; i < info.Members.Count; i++)
                {
                    GroupMember m = info.Members[i];
                    if (m == null) continue;
                    string name = m.DisplayName ?? string.Empty;
                    string letter = "?";
                    if (name.Length > 0 && char.IsLetter(name[0]))
                        letter = char.ToUpperInvariant(name[0]).ToString();
                    Members.Add(new MemberVm
                    {
                        DisplayName = name,
                        AvatarLetter = letter,
                        IsAdmin = m.IsAdmin
                    });
                }
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Command handlers ----------------------------------------

        private void OnAddMembers()
        {
            if (_nav == null) return;
            _nav.NavigateTo(Route.NewChat, _peerKey);
        }

        private void OnEdit()
        {
            if (_nav != null) _nav.NavigateTo(Route.EditProfile);
        }

        private async Task ToggleMuteAsync()
        {
            ErrorMessage = null;
            if (_notifications == null)
            {
                ErrorMessage = "Notifications service not available.";
                return;
            }
            if (string.IsNullOrEmpty(_peerKey))
            {
                ErrorMessage = "Cannot mute: unknown peer.";
                return;
            }

            IsBusy = true;
            try
            {
                MuteRule rule = _isMuted ? MuteRule.DefaultFor(_peerKey) : MuteRule.MutedForever(_peerKey);
                Result<Vianigram.Notifications.Domain.ValueObjects.Unit, NotificationsError> result;
                try
                {
                    result = await _notifications.SetMuteAsync(_peerKey, rule, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }
                if (result.IsFail)
                {
                    ErrorMessage = FormatNotificationsError(result.Error);
                    return;
                }
                IsMuted = !IsMuted;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task LeaveAsync()
        {
            ErrorMessage = null;
            if (_chats == null)
            {
                ErrorMessage = "Chats service not available.";
                return;
            }
            PeerId peer = ParsePeerKey(_peerKey);
            if (peer == null)
            {
                ErrorMessage = "Cannot leave: unknown peer.";
                return;
            }

            IsBusy = true;
            try
            {
                Result<ChatsUnit, ChatError> result;
                try
                {
                    result = await _chats.LeaveAsync(peer, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.For("App.GroupInfoPage").Error("LeaveAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatChatError(result.Error);
                    return;
                }

                if (_nav != null) _nav.NavigateTo(Route.ChatList);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---- Helpers --------------------------------------------------

        private static PeerId ParsePeerKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int colon = key.IndexOf(':');
            if (colon <= 0 || colon == key.Length - 1) return null;
            string kind = key.Substring(0, colon);
            string idText = key.Substring(colon + 1);
            long id;
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return null;
            try
            {
                if (string.Equals(kind, "user", StringComparison.OrdinalIgnoreCase))
                    return PeerId.User(id, 0L);
                if (string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase))
                    return PeerId.Chat(id);
                if (string.Equals(kind, "channel", StringComparison.OrdinalIgnoreCase))
                    return PeerId.Channel(id, 0L);
            }
            catch (ArgumentException)
            {
            }
            return null;
        }

        private static string FormatChatError(ChatError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ChatErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case ChatErrorKind.AccessDenied:
                    return "Access denied.";
                case ChatErrorKind.PeerNotFound:
                    return "Group not found.";
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }

        private static string FormatNotificationsError(NotificationsError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case NotificationsErrorKind.NetworkError:
                    return "Network error.";
                case NotificationsErrorKind.PlatformDenied:
                    return "Notifications denied by platform.";
                case NotificationsErrorKind.NotFound:
                    return "Peer not found.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }
}
