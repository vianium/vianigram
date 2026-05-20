// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PhoneNumberPageViewModel.cs
//
// Inbound adapter for PhoneNumberPage. Splits the phone number into a
// country selector and a national-number input, recomposes
// the E.164 form for IAccountApi.SendPhoneCodeAsync, and exposes the
// result as ErrorMessage / IsBusy state for the page.
//
// The view-model NEVER throws across its public surface — IAccountApi
// already returns Result<Unit, AccountError> and we map that to UI state.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Kernel.Result;

namespace Vianigram.App.ViewModels
{
    public sealed class PhoneNumberPageViewModel : ObservableObject
    {
        // Cap how long we'll wait for IAccountApi.SendPhoneCodeAsync. The
        // MTProto layer can spend 30–90s on a cold handshake; beyond a
        // minute we give the user an explicit "try again" affordance
        // instead of an indefinite spinner.
        private static readonly TimeSpan SendCodeTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan LoginWarmupDebounce = TimeSpan.FromMilliseconds(700);

        private readonly IAccountApi _account;
        private readonly IPhoneLoginPreparationApi _loginPreparation;
        private readonly INavigationService _nav;

        private string _countryName;
        private string _countryCode;
        private string _phonePlaceholder;
        private string _nationalNumber;
        private string _errorMessage;
        private bool _isBusy;
        private TelegramCountryEntry _country;
        private int _loginWarmupGeneration;
        private string _loginWarmupPhone;
        private Task _loginWarmupTask;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public PhoneNumberPageViewModel()
            : this(null, null)
        {
        }

        public PhoneNumberPageViewModel(IAccountApi account, INavigationService nav)
        {
            _account = account;
            _loginPreparation = account as IPhoneLoginPreparationApi;
            _nav = nav;

            _nationalNumber = string.Empty;
            ApplyCountry(CountrySelectionService.Current);

            BackCommand = new RelayCommand(_ => OnBack());
            NextCommand = new AsyncCommand(NextAsync, _ => CanSubmit);
            SelectCountryCommand = new RelayCommand(_ => OnSelectCountry());
        }

        public string CountryName
        {
            get { return _countryName; }
            set { SetProperty(ref _countryName, value); }
        }

        public string CountryCode
        {
            get { return _countryCode; }
            set
            {
                if (SetProperty(ref _countryCode, value))
                    OnPropertyChanged("PhoneNumber");
            }
        }

        public string PhonePlaceholder
        {
            get { return _phonePlaceholder; }
            private set { SetProperty(ref _phonePlaceholder, value); }
        }

        public string NationalNumber
        {
            get { return _nationalNumber; }
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _nationalNumber, normalized))
                {
                    RaisePhoneStateChanged();
                }
            }
        }

        /// <summary>
        /// Composed E.164 number sent to <see cref="IAccountApi.SendPhoneCodeAsync"/>.
        /// Reads digits from the masked local field so users can type
        /// "90 1234 5678" while the wire form is "+819012345678".
        /// </summary>
        public string PhoneNumber
        {
            get
            {
                string digits = TelegramCountryCatalog.StripToLocalDigits(_country, _nationalNumber);
                if (digits.Length == 0) return _countryCode ?? string.Empty;
                return (_countryCode ?? string.Empty) + digits;
            }
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
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged("CanSubmit");
                    var nc = NextCommand as AsyncCommand;
                    if (nc != null) nc.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanSubmit
        {
            get { return !_isBusy && TelegramCountryCatalog.StripToLocalDigits(_country, _nationalNumber).Length > 0; }
        }

        public ICommand BackCommand { get; private set; }
        public ICommand NextCommand { get; private set; }
        public ICommand SelectCountryCommand { get; private set; }

        public async Task RefreshCountrySelectionAsync()
        {
            TelegramCountryEntry country =
                await CountrySelectionService.EnsureCurrentAsync().ConfigureAwait(true);
            ApplyCountry(country);
        }

        public string FormatNationalNumber(string value)
        {
            return TelegramCountryCatalog.FormatLocalPhoneNumber(_country, value);
        }

        /// <summary>
        /// Returns true on success (caller may navigate to SmsCodePage),
        /// false on validation/network/protocol failure (ErrorMessage is set).
        /// </summary>
        public async Task<bool> SendCodeAsync(CancellationToken ct)
        {
            ErrorMessage = null;

            if (_account == null)
            {
                ErrorMessage = Strings.Get("ErrorsAccountUnavailable");
                return false;
            }

            if (TelegramCountryCatalog.StripToLocalDigits(_country, _nationalNumber).Length == 0)
            {
                ErrorMessage = Strings.Get("ErrorsPhoneMissingNumber");
                return false;
            }

            IsBusy = true;
            try
            {
                // The Account layer expects E.164 form (PhoneNumber.TryParse
                // requires the leading '+'); it normalizes to the pure-digit
                // wire format internally before encoding auth.sendCode (see
                // TlEncoder.NormalizePhoneNumberForAuth).
                AppLog.For("App.PhoneNumberPage").Info("SendCodeAsync begin phone=" + PhoneNumber);
                Result<Unit, AccountError> result;
                try
                {
                    // Race the RPC against an explicit timeout. WP-side
                    // CancellationToken support inside the MTProto layer is
                    // best-effort; Task.WhenAny guarantees we always escape.
                    Task<Result<Unit, AccountError>> sendTask =
                        _account.SendPhoneCodeAsync(PhoneNumber, ct);
                    Task timeoutTask = Task.Delay(SendCodeTimeout, ct);

                    Task completed = await Task.WhenAny(sendTask, timeoutTask).ConfigureAwait(true);

                    if (!object.ReferenceEquals(completed, sendTask))
                    {
                        AppLog.For("App.PhoneNumberPage").Warn(
                            "SendCodeAsync timed out after " +
                            ((int)SendCodeTimeout.TotalSeconds).ToString(CultureInfo.InvariantCulture) +
                            "s — surfacing retry UX.");
                        ErrorMessage = Strings.Get("ErrorsPhoneTimeout");
                        return false;
                    }

                    result = await sendTask.ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return false;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.PhoneNumberPage").Error("SendCodeAsync threw: " + ex);
                    ErrorMessage = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("ErrorsPhoneUnexpected"),
                        ex.GetType().Name);
                    return false;
                }

                if (result.IsOk)
                {
                    AppLog.For("App.PhoneNumberPage").Info("SendCodeAsync ok");
                    return true;
                }

                AppLog.For("App.PhoneNumberPage").Warn(
                    "SendCodeAsync failed: " +
                    (result.Error == null ? "(no error)" : result.Error.Kind.ToString()));
                ErrorMessage = FormatError(result.Error);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task NextAsync(object parameter)
        {
            bool ok = await SendCodeAsync(CancellationToken.None).ConfigureAwait(true);
            if (ok && _nav != null)
            {
                _nav.NavigateTo(Route.SmsCode, PhoneNumber);
            }
        }

        private void OnBack()
        {
            if (_nav == null) return;
            if (_nav.CanGoBack) _nav.GoBack();
            else _nav.NavigateTo(Route.Welcome);
        }

        private void OnSelectCountry()
        {
            if (_nav != null) _nav.NavigateTo(Route.CountryPicker);
        }

        private void ApplyCountry(TelegramCountryEntry country)
        {
            if (country == null) return;

            _country = country;
            CountryName = country.DisplayName;
            CountryCode = country.DialCode;
            PhonePlaceholder = TelegramCountryCatalog.CreateLocalPhonePlaceholder(country);

            string reformatted = FormatNationalNumber(_nationalNumber);
            if (!string.Equals(_nationalNumber, reformatted, StringComparison.Ordinal))
            {
                _nationalNumber = reformatted;
                OnPropertyChanged("NationalNumber");
            }

            RaisePhoneStateChanged();
        }

        private void RaisePhoneStateChanged()
        {
            OnPropertyChanged("PhoneNumber");
            OnPropertyChanged("CanSubmit");
            var nc = NextCommand as AsyncCommand;
            if (nc != null) nc.RaiseCanExecuteChanged();
            ScheduleLoginWarmup();
        }

        private void ScheduleLoginWarmup()
        {
            if (_loginPreparation == null)
            {
                return;
            }

            string localDigits = TelegramCountryCatalog.StripToLocalDigits(_country, _nationalNumber);
            if (localDigits.Length == 0)
            {
                return;
            }

            int expectedDigits = TelegramCountryCatalog.GetExpectedLocalDigitCount(_country);
            if (expectedDigits > 0 && localDigits.Length < expectedDigits)
            {
                return;
            }
            if (expectedDigits <= 0 && localDigits.Length < 7)
            {
                return;
            }

            string phone = PhoneNumber;
            var parsed = Vianigram.Account.Domain.ValueObjects.PhoneNumber.TryParse(phone);
            if (parsed.IsFail)
            {
                return;
            }

            if (string.Equals(_loginWarmupPhone, phone, StringComparison.Ordinal))
            {
                return;
            }

            _loginWarmupPhone = phone;
            int generation = ++_loginWarmupGeneration;
            _loginWarmupTask = WarmUpLoginAsync(phone, generation);
        }

        private async Task WarmUpLoginAsync(string phone, int generation)
        {
            try
            {
                await Task.Delay(LoginWarmupDebounce).ConfigureAwait(false);
                if (generation != _loginWarmupGeneration ||
                    !string.Equals(_loginWarmupPhone, phone, StringComparison.Ordinal))
                {
                    return;
                }

                AppLog.For("App.PhoneNumberPage").Info("PreparePhoneLoginAsync begin phone=" + phone);
                await _loginPreparation.PreparePhoneLoginAsync(phone, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.For("App.PhoneNumberPage").Warn(
                    "PreparePhoneLoginAsync ignored: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string FormatError(AccountError error)
        {
            if (error == null) return Strings.Get("ErrorsPhoneUnexpected");

            switch (error.Kind)
            {
                case AccountErrorKind.InvalidPhoneNumber:
                    return Strings.Get("ErrorsPhoneInvalid");
                case AccountErrorKind.PhoneNumberFlood:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("ErrorsPhoneFlood"),
                        retry);
                case AccountErrorKind.NetworkError:
                    string detail = string.IsNullOrEmpty(error.Message)
                        ? Strings.Get("ErrorsPhoneNetworkNoConnection")
                        : error.Message;
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("ErrorsPhoneNetwork"),
                        detail);
                case AccountErrorKind.DcMigrationRequired:
                    return Strings.Get("ErrorsPhoneDcMigration");
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }

    }
}
