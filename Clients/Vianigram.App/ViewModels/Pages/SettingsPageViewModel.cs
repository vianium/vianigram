// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SettingsPageViewModel.cs — settings root page VM.
// Routes section commands through INavigationService; Logout calls
// IAccountApi.LogoutAsync and navigates to Login. Self-profile is hydrated
// via IAccountApi.GetSelfAsync.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class SettingsPageViewModel : ObservableObject
    {
        private readonly IAccountApi _account;
        private readonly ISettingsApi _settings;
        private readonly INavigationService _nav;

        private string _displayName;
        private string _phone;
        private string _avatarLetter;
        private string _errorMessage;
        private bool _isBusy;
        private string _proxyStatus;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public SettingsPageViewModel()
            : this(null, null, null)
        {
        }

        public SettingsPageViewModel(IAccountApi account, ISettingsApi settings, INavigationService nav)
        {
            _account = account;
            _settings = settings;
            _nav = nav;
            _displayName = "Vianigram User";
            _phone = string.Empty;
            _avatarLetter = "V";
            _proxyStatus = "Direct connection";

            EditProfileCommand = new RelayCommand(_ => NavigateTo(Route.EditProfile));
            NotificationsCommand = new RelayCommand(_ => NavigateTo(Route.Settings));
            PrivacyCommand = new RelayCommand(_ => NavigateTo(Route.Settings));
            DataStorageCommand = new RelayCommand(_ => NavigateTo(Route.Settings));
            ChatSettingsCommand = new RelayCommand(_ => NavigateTo(Route.Settings));
            StickersCommand = new RelayCommand(_ => NavigateTo(Route.Settings));
            DevicesCommand = new RelayCommand(_ => NavigateTo(Route.ActiveSessions));
            LanguageCommand = new AsyncCommand(_ => OnLanguageAsync(), _ => !_isBusy);
            ProxyCommand = new RelayCommand(_ => NavigateTo(Route.ProxySettings));
            AboutCommand = new RelayCommand(_ => NavigateTo(Route.Settings));
            LogoutCommand = new AsyncCommand(_ => OnLogoutAsync(), _ => !_isBusy);
        }

        public string DisplayName
        {
            get { return _displayName; }
            set
            {
                if (SetProperty(ref _displayName, value))
                    OnPropertyChanged("AvatarLetter");
            }
        }

        public string Phone
        {
            get { return _phone; }
            set { SetProperty(ref _phone, value); }
        }

        public string AvatarLetter
        {
            get
            {
                if (!string.IsNullOrEmpty(_avatarLetter)) return _avatarLetter;
                if (!string.IsNullOrEmpty(_displayName))
                    return _displayName.Substring(0, 1).ToUpperInvariant();
                return "V";
            }
            set { SetProperty(ref _avatarLetter, value); }
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

        /// <summary>
        /// Subtitle for the "Proxy and connection" row. Refreshed on
        /// every Settings navigation via <see cref="LoadProxyStatusAsync"/>.
        /// </summary>
        public string ProxyStatus
        {
            get { return _proxyStatus; }
            private set { SetProperty(ref _proxyStatus, value); }
        }

        public ICommand EditProfileCommand { get; private set; }
        public ICommand NotificationsCommand { get; private set; }
        public ICommand PrivacyCommand { get; private set; }
        public ICommand DataStorageCommand { get; private set; }
        public ICommand ChatSettingsCommand { get; private set; }
        public ICommand StickersCommand { get; private set; }
        public ICommand DevicesCommand { get; private set; }
        public AsyncCommand LanguageCommand { get; private set; }
        public ICommand ProxyCommand { get; private set; }
        public ICommand AboutCommand { get; private set; }
        public AsyncCommand LogoutCommand { get; private set; }

        // ---- Navigation lifecycle --------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            var ignoreSelf = LoadSelfAsync(CancellationToken.None);
            var ignoreProxy = LoadProxyStatusAsync(CancellationToken.None);
        }

        private async Task LoadProxyStatusAsync(CancellationToken ct)
        {
            if (_settings == null)
            {
                ProxyStatus = "Direct connection";
                return;
            }
            try
            {
                Result<ProxyConfig, SettingsError> r =
                    await _settings.GetProxyAsync(ct).ConfigureAwait(true);
                // Result<T,TError> is a struct — cannot compare to null.
                ProxyConfig cfg = r.IsOk ? r.Value : null;
                if (cfg == null || !cfg.Enabled || string.IsNullOrEmpty(cfg.Host))
                {
                    ProxyStatus = "Direct connection";
                    return;
                }
                string suffix;
                switch (cfg.Mode)
                {
                    case ProxySecretMode.Secure:  suffix = " · Secure"; break;
                    case ProxySecretMode.FakeTls:
                        suffix = string.IsNullOrEmpty(cfg.FakeTlsDomain)
                            ? " · FakeTLS"
                            : " · FakeTLS (" + cfg.FakeTlsDomain + ")";
                        break;
                    default: suffix = string.Empty; break;
                }
                ProxyStatus = cfg.Host + ":" + cfg.Port + suffix;
            }
            catch (Exception)
            {
                ProxyStatus = "Direct connection";
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        private async Task LoadSelfAsync(CancellationToken ct)
        {
            if (_account == null) return;

            Result<SelfProfile, AccountError> result;
            try
            {
                result = await _account.GetSelfAsync(ct).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                AppLog.For("App.SettingsPage").Error("GetSelfAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatAccount(result.Error);
                return;
            }

            SelfProfile p = result.Value;
            if (p == null) return;

            string first = p.FirstName ?? string.Empty;
            string last = p.LastName ?? string.Empty;
            string display = (first + " " + last).Trim();
            DisplayName = display.Length == 0 ? "Vianigram User" : display;
            Phone = p.Phone ?? string.Empty;
            string letterSource = display.Length > 0 ? display : "V";
            AvatarLetter = char.ToUpperInvariant(letterSource[0]).ToString();
        }

        private async Task OnLanguageAsync()
        {
            ErrorMessage = null;
            if (_settings == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            IsBusy = true;
            try
            {
                Result<LanguagePack, SettingsError> result;
                try
                {
                    result = await _settings.GetLanguageAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.SettingsPage").Error("GetLanguageAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatSettings(result.Error);
                    return;
                }

                NavigateTo(Route.Settings);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task OnLogoutAsync()
        {
            ErrorMessage = null;
            if (_account == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            IsBusy = true;
            try
            {
                Result<Vianigram.Account.Domain.ValueObjects.Unit, AccountError> result;
                try
                {
                    result = await _account.LogoutAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.SettingsPage").Error("LogoutAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatAccount(result.Error);
                    return;
                }

                // Drop the cached ChatListPage / ChatPage instances before
                // navigating
                // to Welcome. The next sign-in will rebuild them
                // against the new IEventBus / IChatsApi / IMessagesApi
                // bound to the fresh session. Without this, the cached
                // pages would keep listening to the old (logged-out)
                // composition root and either leak subscriptions or
                // render the previous user's data on re-login.
                if (_nav != null)
                {
                    try { _nav.ClearCache(); }
                    catch (Exception ex)
                    {
                        AppLog.For("App.SettingsPage").Warn("nav.ClearCache threw: " + ex.Message);
                    }
                }

                // Wipe the tile to its zero-state so the next account
                // doesn't see this
                // user's recent messages on the start screen.
                try
                {
                    if (App.LiveTile != null) App.LiveTile.Clear();
                }
                catch (Exception ex)
                {
                    AppLog.For("App.SettingsPage").Warn("live-tile clear threw: " + ex.Message);
                }

                NavigateTo(Route.Welcome);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void NavigateTo(Route route)
        {
            if (_nav == null) return;
            try
            {
                _nav.NavigateTo(route);
            }
            catch (Exception ex)
            {
                AppLog.For("App.SettingsPage").Error("NavigateTo threw: " + ex);
            }
        }

        private static string FormatAccount(AccountError error)
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

        private static string FormatSettings(SettingsError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case SettingsErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case SettingsErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }
}
