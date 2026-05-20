// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SecretChatPageViewModel.cs
//
// Drives SecretChatPage (e2e). OnNavigatedTo projects the supplied
// SecretChatId via ISecretChatsApi.GetSession + GetEmojiKey, subscribes
// to MessageReceived / SessionChanged for live updates, and exposes
// Send / ViewKey / SetSelfDestruct commands. ViewKey routes through
// INavigationService to KeyFingerprintPage.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.App.ViewModels;
using Vianigram.Kernel.Result;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Domain.ValueObjects;
using Vianigram.SecretChats.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    /// <summary>
    /// Single bubble in SecretChatPage. Public + sealed so .NET Native can
    /// reflect for binding in Release configuration.
    /// </summary>
    public sealed class SecretMessageRow
    {
        public string Text { get; set; }
        public bool IsOutgoing { get; set; }
        public string TimeLabel { get; set; }
        public string BubbleAlignment { get { return IsOutgoing ? "Right" : "Left"; } }
        public int TtlSeconds { get; set; }
    }

    public sealed class SecretChatPageViewModel : BaseViewModel
    {
        private readonly ISecretChatsApi _secret;
        private readonly INavigationService _nav;

        private SecretChatId _chatId;
        private string _peerTitle;
        private string _avatarLetter;
        private string _composerText;
        private bool _isBusy;
        private bool _isKeyVerified;
        private int _selfDestructSeconds;
        private string _errorMessage;

        public SecretChatPageViewModel() : this(null, null)
        {
        }

        public SecretChatPageViewModel(ISecretChatsApi secret, INavigationService nav)
        {
            _secret = secret;
            _nav = nav;

            _peerTitle = string.Empty;
            _avatarLetter = "?";
            _composerText = string.Empty;
            _selfDestructSeconds = 0;

            Messages = new ObservableCollection<SecretMessageRow>();

            SendCommand = new AsyncCommand(_ => SendAsync(), _ => CanSend);
            ViewKeyCommand = new RelayCommand(_ => OnViewKey(), _ => true);
            SetSelfDestructTimerCommand = new AsyncCommand(p => OnSetTimerAsync(p), _ => true);
        }

        // ---- Bindable surface ---------------------------------------

        public string PeerTitle
        {
            get { return _peerTitle; }
            set { SetProperty(ref _peerTitle, value); }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        public ObservableCollection<SecretMessageRow> Messages { get; private set; }

        public string ComposerText
        {
            get { return _composerText; }
            set
            {
                if (SetProperty(ref _composerText, value))
                    OnPropertyChanged("CanSend");
            }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    OnPropertyChanged("CanSend");
            }
        }

        public bool CanSend
        {
            get { return !_isBusy && !string.IsNullOrWhiteSpace(_composerText); }
        }

        public bool IsKeyVerified
        {
            get { return _isKeyVerified; }
            set
            {
                if (SetProperty(ref _isKeyVerified, value))
                    OnPropertyChanged("KeyStatusText");
            }
        }

        public string KeyStatusText
        {
            get { return _isKeyVerified ? "Encrypted (verified)" : "Encrypted"; }
        }

        public int SelfDestructSeconds
        {
            get { return _selfDestructSeconds; }
            set
            {
                if (SetProperty(ref _selfDestructSeconds, value))
                {
                    OnPropertyChanged("SelfDestructLabel");
                    OnPropertyChanged("HasSelfDestruct");
                }
            }
        }

        public bool HasSelfDestruct
        {
            get { return _selfDestructSeconds > 0; }
        }

        public string SelfDestructLabel
        {
            get { return FormatSelfDestruct(_selfDestructSeconds); }
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

        public SecretChatId ChatId
        {
            get { return _chatId; }
        }

        // ---- Commands -------------------------------------------------

        public ICommand SendCommand { get; private set; }
        public ICommand ViewKeyCommand { get; private set; }
        public ICommand SetSelfDestructTimerCommand { get; private set; }

        // ---- Lifecycle -----------------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;

            if (parameter is SecretChatId)
            {
                _chatId = (SecretChatId)parameter;
            }
            else if (parameter is int)
            {
                _chatId = new SecretChatId((int)parameter);
            }

            if (_secret == null) return;

            // Project current session + emoji-key fingerprint flag.
            try
            {
                SecretSession session = _secret.GetSession(_chatId);
                if (session != null)
                {
                    // Peer title is rendered by the page from the directory; the
                    // VM only needs to know whether the session is established.
                    EmojiKey key = _secret.GetEmojiKey(_chatId);
                    IsKeyVerified = key != null;
                }
            }
            catch (Exception ex)
            {
                AppLog.For("App.SecretChatPage").Error("OnNavigatedTo project threw: " + ex);
            }

            var ignore = LoadHistoryAsync(CancellationToken.None);

            _secret.MessageReceived += OnMessageReceived;
            _secret.SessionChanged += OnSessionChanged;
        }

        private async Task LoadHistoryAsync(CancellationToken ct)
        {
            if (_secret == null) return;

            Result<SecretMessagePage, SecretChatError> result;
            try
            {
                result = await _secret.LoadHistoryAsync(_chatId, null, 100, ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                AppLog.For("App.SecretChatPage").Error("LoadHistoryAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatError(result.Error);
                return;
            }

            SecretMessagePage page = result.Value;
            if (page == null || page.Messages == null) return;

            Messages.Clear();
            for (int i = 0; i < page.Messages.Count; i++)
            {
                SecretMessage m = page.Messages[i];
                if (m == null) continue;
                Messages.Add(new SecretMessageRow
                {
                    Text = m.Body ?? string.Empty,
                    IsOutgoing = m.IsOutgoing,
                    TimeLabel = m.SentAt.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                    TtlSeconds = _selfDestructSeconds
                });
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
            if (_secret == null) return;
            _secret.MessageReceived -= OnMessageReceived;
            _secret.SessionChanged -= OnSessionChanged;
        }

        // ---- Subscription handlers ----------------------------------

        private void OnMessageReceived(object sender, SecretMessageReceivedEventArgs args)
        {
            if (args == null) return;
            if (args.ChatId != _chatId) return;

            var ignore = Dispatch.OnUiAsync(() =>
            {
                Messages.Add(new SecretMessageRow
                {
                    Text = string.Empty,
                    IsOutgoing = false,
                    TimeLabel = args.At.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture),
                    TtlSeconds = _selfDestructSeconds
                });
            });
        }

        private void OnSessionChanged(object sender, SecretChatChangedEventArgs args)
        {
            if (args == null) return;
            if (args.ChatId != _chatId) return;

            var ignore = Dispatch.OnUiAsync(() =>
            {
                try
                {
                    EmojiKey key = _secret.GetEmojiKey(_chatId);
                    IsKeyVerified = key != null;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.SecretChatPage").Error("GetEmojiKey threw: " + ex);
                }
            });
        }

        // ---- Command handlers ---------------------------------------

        private async Task SendAsync()
        {
            if (!CanSend) return;
            ErrorMessage = null;

            if (_secret == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            string body = _composerText;
            IsBusy = true;
            try
            {
                Result<Unit, SecretChatError> result;
                try
                {
                    result = await _secret.SendTextAsync(_chatId, body, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.SecretChatPage").Error("SendTextAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                Messages.Add(new SecretMessageRow
                {
                    Text = body,
                    IsOutgoing = true,
                    TimeLabel = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture),
                    TtlSeconds = _selfDestructSeconds
                });
                ComposerText = string.Empty;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnViewKey()
        {
            if (_nav == null) return;
            _nav.NavigateTo(Route.KeyFingerprint, _chatId);
        }

        private async Task OnSetTimerAsync(object parameter)
        {
            int seconds;
            if (parameter is int)
            {
                seconds = (int)parameter;
            }
            else if (parameter is string &&
                     int.TryParse((string)parameter, NumberStyles.Integer,
                                   CultureInfo.InvariantCulture, out seconds))
            {
                // parsed
            }
            else
            {
                seconds = 0;
            }
            if (seconds < 0) seconds = 0;

            ErrorMessage = null;
            if (_secret == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            Result<Unit, SecretChatError> result;
            try
            {
                result = await _secret.SetSelfDestructTimerAsync(_chatId, seconds, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.SecretChatPage").Error("SetSelfDestructTimerAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatError(result.Error);
                return;
            }

            SelfDestructSeconds = seconds;
        }

        // ---- Helpers --------------------------------------------------

        private static string FormatSelfDestruct(int seconds)
        {
            if (seconds <= 0) return "Off";
            if (seconds < 60) return seconds + "s";
            if (seconds < 3600) return (seconds / 60) + "m";
            if (seconds < 86400) return (seconds / 3600) + "h";
            if (seconds < 604800) return (seconds / 86400) + "d";
            return (seconds / 604800) + "w";
        }

        private static string FormatError(SecretChatError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case SecretChatErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case SecretChatErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                case SecretChatErrorKind.FingerprintMismatch:
                    return "Security check failed.";
                case SecretChatErrorKind.ChatNotFound:
                    return "Chat not found.";
                case SecretChatErrorKind.NotInExpectedState:
                    return "Chat not ready.";
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }
    }
}
