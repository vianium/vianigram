// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PasscodePageViewModel.cs — auth page VM (passcode set / unlock / change).
// Wires IPrivacyApi for Enable / Verify / Change. Mode comes via nav param
// ("set" / "unlock" / "change"). On accept we GoBack via INavigationService.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.Kernel.Result;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.ValueObjects;
using Vianigram.Privacy.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class PasscodePageViewModel : ObservableObject
    {
        private readonly IPrivacyApi _privacy;
        private readonly INavigationService _nav;

        private string _pin1;
        private string _pin2;
        private string _pin3;
        private string _pin4;
        private string _errorMessage;
        private bool _isBusy;
        private string _mode;          // "set" / "unlock" / "change"
        private string _firstEntry;    // for "set" two-step or "change" old/new step
        private string _changeOldPin;  // captured during the first prompt of change-mode

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public PasscodePageViewModel()
            : this(null, null)
        {
        }

        public PasscodePageViewModel(IPrivacyApi privacy, INavigationService nav)
        {
            _privacy = privacy;
            _nav = nav;
            _mode = "unlock";
            ConfirmCommand = new AsyncCommand(_ => ConfirmAsync(), _ => CanConfirm);
            ForgotCommand = new RelayCommand(_ => OnForgot(), _ => true);
        }

        public string Pin1
        {
            get { return _pin1; }
            set
            {
                if (SetProperty(ref _pin1, SanitizeDigit(value)))
                    RaiseConfirmCanExecuteChanged();
            }
        }

        public string Pin2
        {
            get { return _pin2; }
            set
            {
                if (SetProperty(ref _pin2, SanitizeDigit(value)))
                    RaiseConfirmCanExecuteChanged();
            }
        }

        public string Pin3
        {
            get { return _pin3; }
            set
            {
                if (SetProperty(ref _pin3, SanitizeDigit(value)))
                    RaiseConfirmCanExecuteChanged();
            }
        }

        public string Pin4
        {
            get { return _pin4; }
            set
            {
                if (SetProperty(ref _pin4, SanitizeDigit(value)))
                    RaiseConfirmCanExecuteChanged();
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

        /// <summary>True for "set"-mode (two-step confirm flow).</summary>
        public bool IsConfirmMode
        {
            get { return string.Equals(_mode, "set", StringComparison.OrdinalIgnoreCase); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    RaiseConfirmCanExecuteChanged();
            }
        }

        public bool CanConfirm
        {
            get
            {
                if (_isBusy) return false;
                return !string.IsNullOrEmpty(_pin1)
                    && !string.IsNullOrEmpty(_pin2)
                    && !string.IsNullOrEmpty(_pin3)
                    && !string.IsNullOrEmpty(_pin4);
            }
        }

        public AsyncCommand ConfirmCommand { get; private set; }
        public ICommand ForgotCommand { get; private set; }

        public void OnNavigatedTo(object parameter)
        {
            var raw = parameter as string;
            _mode = string.IsNullOrEmpty(raw) ? "unlock" : raw.ToLowerInvariant();
            _firstEntry = null;
            _changeOldPin = null;
            ErrorMessage = null;
            ResetPin();
            OnPropertyChanged("IsConfirmMode");
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        private async Task ConfirmAsync()
        {
            if (!CanConfirm) return;
            ErrorMessage = null;

            if (_privacy == null)
            {
                ErrorMessage = "Privacy service not available.";
                return;
            }

            string code = (_pin1 ?? "") + (_pin2 ?? "") + (_pin3 ?? "") + (_pin4 ?? "");
            string mode = _mode ?? "unlock";

            IsBusy = true;
            try
            {
                Result<Unit, PrivacyError> result;
                if (string.Equals(mode, "set", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(_firstEntry))
                    {
                        _firstEntry = code;
                        ResetPin();
                        return;
                    }
                    if (!string.Equals(_firstEntry, code, StringComparison.Ordinal))
                    {
                        _firstEntry = null;
                        ErrorMessage = "PINs do not match. Try again.";
                        ResetPin();
                        return;
                    }
                    string pin = _firstEntry;
                    _firstEntry = null;
                    try
                    {
                        result = await _privacy.EnablePasscodeAsync(pin, CancellationToken.None).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                        return;
                    }
                    if (result.IsFail)
                    {
                        ErrorMessage = FormatError(result.Error);
                        ResetPin();
                        return;
                    }
                    NavigateBackAccept();
                }
                else if (string.Equals(mode, "change", StringComparison.OrdinalIgnoreCase))
                {
                    // Two-step: first prompt captures old pin, second prompt new pin.
                    if (string.IsNullOrEmpty(_changeOldPin))
                    {
                        _changeOldPin = code;
                        ResetPin();
                        return;
                    }
                    string oldPin = _changeOldPin;
                    string newPin = code;
                    _changeOldPin = null;
                    try
                    {
                        result = await _privacy.ChangePasscodeAsync(oldPin, newPin, CancellationToken.None).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                        return;
                    }
                    if (result.IsFail)
                    {
                        ErrorMessage = FormatError(result.Error);
                        ResetPin();
                        return;
                    }
                    NavigateBackAccept();
                }
                else
                {
                    // Unlock — IPrivacyApi.VerifyPasscodeAsync returns Ok(bool).
                    Result<bool, PrivacyError> verify;
                    try
                    {
                        verify = await _privacy.VerifyPasscodeAsync(code, CancellationToken.None).ConfigureAwait(true);
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                        return;
                    }
                    if (verify.IsFail)
                    {
                        ErrorMessage = FormatError(verify.Error);
                        ResetPin();
                        return;
                    }
                    if (!verify.Value)
                    {
                        ErrorMessage = "Wrong passcode. Try again.";
                        ResetPin();
                        return;
                    }
                    NavigateBackAccept();
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void NavigateBackAccept()
        {
            if (_nav == null) return;
            if (_nav.CanGoBack) _nav.GoBack();
        }

        private void OnForgot()
        {
            // A real reset flow is planned; for now hop to Settings.
            if (_nav != null) _nav.NavigateTo(Route.Settings);
        }

        private void ResetPin()
        {
            _pin1 = null;
            _pin2 = null;
            _pin3 = null;
            _pin4 = null;
            OnPropertyChanged("Pin1");
            OnPropertyChanged("Pin2");
            OnPropertyChanged("Pin3");
            OnPropertyChanged("Pin4");
            RaiseConfirmCanExecuteChanged();
        }

        private void RaiseConfirmCanExecuteChanged()
        {
            OnPropertyChanged("CanConfirm");
            ConfirmCommand.RaiseCanExecuteChanged();
        }

        private static string SanitizeDigit(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return null;
            for (int i = raw.Length - 1; i >= 0; i--)
            {
                char c = raw[i];
                if (c >= '0' && c <= '9') return c.ToString();
            }
            return null;
        }

        private static string FormatError(PrivacyError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case PrivacyErrorKind.PasscodeWrong:
                    return "Wrong passcode. Try again.";
                case PrivacyErrorKind.PasscodeMismatch:
                    return "PINs do not match.";
                case PrivacyErrorKind.InvalidValue:
                    return string.IsNullOrEmpty(error.Message) ? "Invalid PIN." : error.Message;
                case PrivacyErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case PrivacyErrorKind.StorageError:
                    return "Storage error.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }
}
