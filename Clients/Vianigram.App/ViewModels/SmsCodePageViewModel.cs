// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SmsCodePageViewModel.cs
//
// Second step of phone login: user enters the Telegram code, we call
// IAccountApi.VerifyCodeAsync. If Telegram initially delivered the code in-app,
// the page can later request the next delivery method via auth.resendCode.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.Account.Application.Commands;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Kernel.Result;
using Windows.UI.Xaml;

namespace Vianigram.App.ViewModels
{
    public enum VerifyOutcome
    {
        Fail = 0,
        Success = 1,
        TwoFaRequired = 2,
        SignUpRequired = 3
    }

    public sealed class SmsCodePageViewModel : ObservableObject
    {
        public const int CodeLength = 5;

        private readonly IAccountApi _account;
        private readonly INavigationService _nav;
        private readonly string _phoneNumber;
        private readonly DispatcherTimer _resendTimer;

        private string _code;
        private string _errorMessage;
        private string _instructionText;
        private string _resendText;
        private string _resendStatusText;
        private bool _isBusy;
        private bool _canResend;
        private SentCodeType? _sentCodeType;
        private SentCodeType? _nextCodeType;
        private DateTime? _codeExpiresAtUtc;
        private string _passwordHint;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public SmsCodePageViewModel()
            : this(null, null, null)
        {
        }

        public SmsCodePageViewModel(IAccountApi account, string phoneNumber)
            : this(account, phoneNumber, null)
        {
        }

        public SmsCodePageViewModel(IAccountApi account, string phoneNumber, INavigationService nav)
        {
            _account = account;
            _nav = nav;
            _phoneNumber = phoneNumber ?? string.Empty;
            Title = Strings.Get("SmsCodeTitle");
            PlaceholderText = Strings.Get("SmsCodePlaceholder");
            VerifyText = Strings.Get("SmsCodeVerifyButton");
            _code = string.Empty;

            _resendTimer = new DispatcherTimer();
            _resendTimer.Interval = TimeSpan.FromSeconds(1);
            _resendTimer.Tick += OnResendTimerTick;

            BackCommand = new RelayCommand(_ => OnBack());
            EditNumberCommand = new RelayCommand(_ => OnEditNumber());
            ResendCommand = new AsyncCommand(_ => ResendAsync(CancellationToken.None), _ => CanResend);

            RefreshCodeStateFromAccount();
        }

        public ICommand BackCommand { get; private set; }
        public ICommand EditNumberCommand { get; private set; }
        public ICommand ResendCommand { get; private set; }

        public string Title { get; private set; }

        public string InstructionText
        {
            get { return _instructionText; }
            private set { SetProperty(ref _instructionText, value); }
        }

        public string PlaceholderText { get; private set; }

        public string VerifyText { get; private set; }

        public string ResendText
        {
            get { return _resendText; }
            private set { SetProperty(ref _resendText, value); }
        }

        public string ResendStatusText
        {
            get { return _resendStatusText; }
            private set
            {
                if (SetProperty(ref _resendStatusText, value))
                {
                    OnPropertyChanged("HasResendStatus");
                }
            }
        }

        public string PhoneNumber
        {
            get { return _phoneNumber; }
        }

        public string PasswordHint
        {
            get { return _passwordHint; }
            private set { SetProperty(ref _passwordHint, value); }
        }

        public string Code
        {
            get { return _code; }
            set
            {
                string normalized = SanitizeCode(value);
                if (SetProperty(ref _code, normalized))
                {
                    OnPropertyChanged("CanSubmit");
                    OnPropertyChanged("IsCodeComplete");
                    OnPropertyChanged("Digit1");
                    OnPropertyChanged("Digit2");
                    OnPropertyChanged("Digit3");
                    OnPropertyChanged("Digit4");
                    OnPropertyChanged("Digit5");
                }
            }
        }

        public string Digit1 { get { return DigitAt(0); } }
        public string Digit2 { get { return DigitAt(1); } }
        public string Digit3 { get { return DigitAt(2); } }
        public string Digit4 { get { return DigitAt(3); } }
        public string Digit5 { get { return DigitAt(4); } }

        public bool IsCodeComplete
        {
            get { return !string.IsNullOrEmpty(_code) && _code.Length >= CodeLength; }
        }

        private string DigitAt(int index)
        {
            if (string.IsNullOrEmpty(_code) || index >= _code.Length) return string.Empty;
            return _code[index].ToString();
        }

        private static string SanitizeCode(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length && sb.Length < CodeLength; i++)
            {
                char c = value[i];
                if (c >= '0' && c <= '9') sb.Append(c);
            }
            return sb.ToString();
        }

        private void OnBack()
        {
            if (_nav == null) return;
            if (_nav.CanGoBack) _nav.GoBack();
            else _nav.NavigateTo(Route.PhoneNumber);
        }

        private void OnEditNumber()
        {
            // The phone number is editable on the previous page; bouncing
            // back through the navigation stack returns the user to the same
            // PhoneNumberPage they came from with their input intact.
            OnBack();
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

        public bool HasResendStatus
        {
            get { return !string.IsNullOrEmpty(_resendStatusText); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    OnPropertyChanged("CanSubmit");
                    OnPropertyChanged("CanResend");
                    var rc = ResendCommand as AsyncCommand;
                    if (rc != null) rc.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanSubmit
        {
            get { return !_isBusy && IsCodeComplete; }
        }

        public bool CanResend
        {
            get { return !_isBusy && _canResend; }
        }

        public string IntroText
        {
            get { return Strings.Get("SmsCodeIntro"); }
        }

        public string EditNumberText
        {
            get { return Strings.Get("SmsCodeEditNumber"); }
        }

        public string PhoneDisplay
        {
            get { return _phoneNumber; }
        }

        public async Task<VerifyOutcome> VerifyAsync(CancellationToken ct)
        {
            ErrorMessage = null;
            PasswordHint = null;

            if (_account == null)
            {
                ErrorMessage = Strings.Get("SmsCodeAccountUnavailable");
                return VerifyOutcome.Fail;
            }

            if (string.IsNullOrWhiteSpace(_code))
            {
                ErrorMessage = Strings.Get("SmsCodeMissing");
                return VerifyOutcome.Fail;
            }

            IsBusy = true;
            try
            {
                Result<AuthOutcome, AccountError> result;
                try
                {
                    result = await _account.VerifyCodeAsync(_code, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return VerifyOutcome.Fail;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.SmsCodePage").Error("VerifyAsync threw: " + ex);
                    ErrorMessage = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("ErrorsPhoneUnexpected"),
                        ex.GetType().Name);
                    return VerifyOutcome.Fail;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return VerifyOutcome.Fail;
                }

                var outcome = result.Value;
                if (outcome != null && outcome.TwoFaRequired)
                {
                    PasswordHint = outcome.PasswordHint;
                    return VerifyOutcome.TwoFaRequired;
                }
                if (outcome != null && outcome.SignUpRequired)
                {
                    return VerifyOutcome.SignUpRequired;
                }

                return VerifyOutcome.Success;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async Task<bool> ResendAsync(CancellationToken ct)
        {
            ErrorMessage = null;
            ResendStatusText = null;

            if (_account == null)
            {
                ErrorMessage = Strings.Get("SmsCodeAccountUnavailable");
                return false;
            }

            if (!CanResend)
            {
                return false;
            }

            IsBusy = true;
            try
            {
                var result = await _account.ResendPhoneCodeAsync(ct).ConfigureAwait(true);
                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return false;
                }

                Code = string.Empty;
                RefreshCodeStateFromAccount();
                ResendStatusText = Strings.Get("SmsCodeResendSuccess");
                return true;
            }
            catch (OperationCanceledException)
            {
                return false;
            }
            catch (Exception ex)
            {
                AppLog.For("App.SmsCodePage").Error("ResendAsync threw: " + ex);
                ErrorMessage = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Get("ErrorsPhoneUnexpected"),
                    ex.GetType().Name);
                return false;
            }
            finally
            {
                IsBusy = false;
                UpdateResendState();
            }
        }

        public void StopCountdown()
        {
            if (_resendTimer != null)
            {
                _resendTimer.Stop();
            }
        }

        private void RefreshCodeStateFromAccount()
        {
            try
            {
                var state = _account != null ? _account.CurrentState : null;
                if (state != null)
                {
                    _sentCodeType = state.SentCodeType;
                    _nextCodeType = state.NextCodeType;
                    _codeExpiresAtUtc = state.CodeExpiresAtUtc;
                }
            }
            catch
            {
                _sentCodeType = null;
                _nextCodeType = null;
                _codeExpiresAtUtc = null;
            }

            InstructionText = BuildInstructionText(_sentCodeType, _phoneNumber);
            UpdateResendState();
        }

        private void OnResendTimerTick(object sender, object e)
        {
            UpdateResendState();
        }

        private void UpdateResendState()
        {
            SentCodeType? effectiveNextType = GetEffectiveNextCodeType();
            if (!effectiveNextType.HasValue)
            {
                StopCountdown();
                SetCanResend(false);
                ResendText = Strings.Get("SmsCodeResendUnavailable");
                return;
            }

            int remaining = GetRemainingSeconds();
            string delivery = FormatDeliveryType(effectiveNextType.Value);

            if (remaining > 0)
            {
                SetCanResend(false);
                ResendText = string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Get("SmsCodeResendCountdown"),
                    delivery,
                    remaining);
                _resendTimer.Start();
                return;
            }

            StopCountdown();
            SetCanResend(true);
            ResendText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Get("SmsCodeResendNow"),
                delivery);
        }

        private SentCodeType? GetEffectiveNextCodeType()
        {
            if (_nextCodeType.HasValue)
            {
                return _nextCodeType.Value;
            }

            // Telegram may answer type=App with a timeout but without next_type.
            // In that case still expose the resend path after the timeout and
            // let auth.resendCode decide if SMS is available for this account.
            if (_sentCodeType == SentCodeType.App && _codeExpiresAtUtc.HasValue)
            {
                return SentCodeType.Sms;
            }

            return null;
        }

        private int GetRemainingSeconds()
        {
            if (!_codeExpiresAtUtc.HasValue)
            {
                return 0;
            }

            double remaining = (_codeExpiresAtUtc.Value - DateTime.UtcNow).TotalSeconds;
            if (remaining <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(remaining);
        }

        private void SetCanResend(bool value)
        {
            SetProperty(ref _canResend, value, "CanResend");
            var rc = ResendCommand as AsyncCommand;
            if (rc != null) rc.RaiseCanExecuteChanged();
        }

        private static string FormatError(AccountError error)
        {
            if (error == null) return Strings.Get("ErrorsPhoneUnexpected");

            switch (error.Kind)
            {
                case AccountErrorKind.PhoneCodeInvalid:
                    return Strings.Get("SmsCodeIncorrect");
                case AccountErrorKind.PhoneCodeExpired:
                    return Strings.Get("SmsCodeExpired");
                case AccountErrorKind.AuthRestart:
                    return Strings.Get("SmsCodeAuthRestart");
                case AccountErrorKind.NetworkError:
                    string detail = string.IsNullOrEmpty(error.Message)
                        ? Strings.Get("ErrorsPhoneNetworkNoConnection")
                        : error.Message;
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("ErrorsPhoneNetwork"),
                        detail);
                case AccountErrorKind.NotInExpectedState:
                    return Strings.Get("SmsCodeNotInState");
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }

        private static string BuildInstructionText(SentCodeType? type, string phoneNumber)
        {
            string key;
            if (type == SentCodeType.App)
            {
                key = "SmsCodeInstructionApp";
            }
            else if (type == SentCodeType.Sms)
            {
                key = "SmsCodeInstructionSms";
            }
            else if (type == SentCodeType.Call)
            {
                key = "SmsCodeInstructionCall";
            }
            else if (type == SentCodeType.FlashCall)
            {
                key = "SmsCodeInstructionFlashCall";
            }
            else
            {
                key = "SmsCodeInstructionGeneric";
            }

            return string.Format(CultureInfo.CurrentCulture, Strings.Get(key), phoneNumber ?? string.Empty);
        }

        private static string FormatDeliveryType(SentCodeType type)
        {
            if (type == SentCodeType.Sms)
            {
                return Strings.Get("SmsCodeDeliverySms");
            }

            if (type == SentCodeType.Call)
            {
                return Strings.Get("SmsCodeDeliveryCall");
            }

            if (type == SentCodeType.FlashCall)
            {
                return Strings.Get("SmsCodeDeliveryFlashCall");
            }

            return Strings.Get("SmsCodeDeliveryCode");
        }
    }
}
