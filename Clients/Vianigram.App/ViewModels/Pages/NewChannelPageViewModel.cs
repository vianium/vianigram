// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// NewChannelPageViewModel.cs — channel-creation form VM.
// Username check goes through IChatsApi.CheckChannelUsernameAsync; create goes
// through IChatsApi.CreateChannelAsync. On success the page navigates to the
// new chat via INavigationService.

using System;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Kernel.Result;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class NewChannelPageViewModel : ObservableObject
    {
        private readonly IChatsApi _chats;
        private readonly INavigationService _nav;

        private string _title;
        private string _description;
        private string _username;
        private bool _isPublic;
        private bool _isCheckingUsername;
        private bool _usernameAvailable;
        private string _usernameError;
        private string _statusText;
        private string _errorMessage;
        private bool _isBusy;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public NewChannelPageViewModel()
            : this(null, null)
        {
        }

        public NewChannelPageViewModel(IChatsApi chats, INavigationService nav)
        {
            _chats = chats;
            _nav = nav;
            CreateCommand = new AsyncCommand(_ => CreateAsync(), _ => CanCreate);
            CheckUsernameCommand = new AsyncCommand(_ => CheckUsernameAsync(), _ => !_isCheckingUsername && _isPublic);
        }

        public AsyncCommand CreateCommand { get; private set; }
        public AsyncCommand CheckUsernameCommand { get; private set; }

        public string Title
        {
            get { return _title; }
            set
            {
                if (SetProperty(ref _title, value))
                {
                    OnPropertyChanged("CanCreate");
                    var cmd = CreateCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public string Description
        {
            get { return _description; }
            set { SetProperty(ref _description, value); }
        }

        public string Username
        {
            get { return _username; }
            set
            {
                if (SetProperty(ref _username, value))
                {
                    UsernameError = null;
                    UsernameAvailable = false;
                    OnPropertyChanged("CanCreate");
                    var cmd = CreateCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsPublic
        {
            get { return _isPublic; }
            set
            {
                if (SetProperty(ref _isPublic, value))
                {
                    UsernameError = null;
                    UsernameAvailable = false;
                    OnPropertyChanged("CanCreate");
                    var cmd = CreateCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                    var cu = CheckUsernameCommand;
                    if (cu != null) cu.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsCheckingUsername
        {
            get { return _isCheckingUsername; }
            private set
            {
                if (SetProperty(ref _isCheckingUsername, value))
                {
                    var cu = CheckUsernameCommand;
                    if (cu != null) cu.RaiseCanExecuteChanged();
                }
            }
        }

        public bool UsernameAvailable
        {
            get { return _usernameAvailable; }
            private set { SetProperty(ref _usernameAvailable, value); }
        }

        public string UsernameError
        {
            get { return _usernameError; }
            private set
            {
                if (SetProperty(ref _usernameError, value))
                    OnPropertyChanged("HasUsernameError");
            }
        }

        public bool HasUsernameError
        {
            get { return !string.IsNullOrEmpty(_usernameError); }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
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

        public bool CanCreate
        {
            get
            {
                if (_isBusy) return false;
                if (string.IsNullOrWhiteSpace(_title)) return false;
                if (_isPublic && string.IsNullOrWhiteSpace(_username)) return false;
                return true;
            }
        }

        // ---- Navigation lifecycle ---------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        private async Task CheckUsernameAsync()
        {
            if (!_isPublic) return;
            UsernameError = null;
            UsernameAvailable = false;

            if (_chats == null)
            {
                UsernameError = "Service not available";
                return;
            }

            string u = _username != null ? _username.Trim() : string.Empty;
            if (u.Length < 5)
            {
                UsernameError = "Username must be at least 5 characters.";
                return;
            }

            if (!IsValidUsername(u))
            {
                UsernameError = "Letters, digits and underscores only; must start with a letter.";
                return;
            }

            IsCheckingUsername = true;
            try
            {
                Result<bool, ChatError> result;
                try
                {
                    result = await _chats.CheckChannelUsernameAsync(u, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.For("App.NewChannelPage").Error("CheckChannelUsernameAsync threw: " + ex);
                    UsernameError = "Could not verify username.";
                    return;
                }

                if (result.IsFail)
                {
                    UsernameError = FormatChatError(result.Error);
                    return;
                }

                UsernameAvailable = result.Value;
                if (!result.Value) UsernameError = "Username already taken.";
            }
            finally
            {
                IsCheckingUsername = false;
            }
        }

        private async Task CreateAsync()
        {
            if (!CanCreate) return;

            ErrorMessage = null;
            if (_chats == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            IsBusy = true;
            StatusText = "Creating channel...";

            try
            {
                string username = _isPublic && _username != null ? _username.Trim() : string.Empty;

                Result<Dialog, ChatError> result;
                try
                {
                    result = await _chats.CreateChannelAsync(
                        _title ?? string.Empty,
                        _description ?? string.Empty,
                        _isPublic,
                        username,
                        CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.For("App.NewChannelPage").Error("CreateChannelAsync threw: " + ex);
                    ErrorMessage = "Create failed: " + ex.GetType().Name;
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatChatError(result.Error);
                    return;
                }

                StatusText = "Channel \"" + (_title ?? string.Empty) + "\" created";
                if (_nav != null && result.Value != null && result.Value.Peer != null)
                {
                    _nav.NavigateTo(Route.Chat, result.Value.Peer.ToString());
                }
            }
            finally
            {
                IsBusy = false;
            }
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
                    return "Peer not found.";
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }

        private static bool IsValidUsername(string u)
        {
            if (string.IsNullOrEmpty(u)) return false;
            char first = u[0];
            if (!((first >= 'a' && first <= 'z') || (first >= 'A' && first <= 'Z'))) return false;
            for (int i = 1; i < u.Length; i++)
            {
                char c = u[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')
                          || (c >= '0' && c <= '9') || c == '_';
                if (!ok) return false;
            }
            return true;
        }
    }
}
