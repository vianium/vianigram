// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

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
using Vianigram.Kernel.Result;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class TwoFaPasswordPageViewModel : ObservableObject
    {
        private readonly IAccountApi _account;
        private readonly INavigationService _nav;

        private string _password;
        private string _passwordHint;
        private string _errorMessage;
        private bool _isBusy;

        public TwoFaPasswordPageViewModel()
            : this(null, null)
        {
        }

        public TwoFaPasswordPageViewModel(IAccountApi account, INavigationService nav)
        {
            _account = account;
            _nav = nav;
            BackCommand = new RelayCommand(_ => OnBack(), _ => !IsBusy);
            SubmitCommand = new AsyncCommand(_ => SubmitAsync(CancellationToken.None), _ => CanSubmit);
            ForgotPasswordCommand = new RelayCommand(_ => OnForgotPassword(), _ => !IsBusy);
        }

        public event EventHandler SubmitSucceeded;

        public ICommand BackCommand { get; private set; }
        public AsyncCommand SubmitCommand { get; private set; }
        public ICommand ForgotPasswordCommand { get; private set; }

        public string Password
        {
            get { return _password; }
            set
            {
                if (SetProperty(ref _password, value ?? string.Empty))
                {
                    ErrorMessage = null;
                    RaiseCommandStates();
                }
            }
        }

        public string PasswordHint
        {
            get { return _passwordHint; }
            private set
            {
                if (SetProperty(ref _passwordHint, value))
                {
                    OnPropertyChanged("HasPasswordHint");
                    OnPropertyChanged("PasswordHintText");
                }
            }
        }

        public bool HasPasswordHint
        {
            get { return !string.IsNullOrWhiteSpace(_passwordHint); }
        }

        public string PasswordHintText
        {
            get
            {
                if (!HasPasswordHint) return string.Empty;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Get("TwoFaPasswordHintFormat"),
                    _passwordHint);
            }
        }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                {
                    OnPropertyChanged("HasError");
                }
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrWhiteSpace(_errorMessage); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                {
                    RaiseCommandStates();
                }
            }
        }

        public bool CanSubmit
        {
            get { return !IsBusy && !string.IsNullOrWhiteSpace(_password); }
        }

        public void OnNavigatedTo(object parameter)
        {
            PasswordHint = parameter as string;
            Password = string.Empty;
            ErrorMessage = null;
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        public async Task SubmitAsync(CancellationToken ct)
        {
            if (!CanSubmit) return;

            ErrorMessage = null;
            if (_account == null)
            {
                ErrorMessage = Strings.Get("TwoFaPasswordAccountUnavailable");
                return;
            }

            IsBusy = true;
            try
            {
                Result<Unit, AccountError> result;
                try
                {
                    result = await _account.SubmitTwoFaPasswordAsync(_password, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ErrorMessage = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("TwoFaPasswordUnexpected"),
                        ex.GetType().Name);
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                var h = SubmitSucceeded;
                if (h != null) h(this, EventArgs.Empty);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void OnBack()
        {
            if (_nav != null && _nav.CanGoBack) _nav.GoBack();
        }

        private void OnForgotPassword()
        {
            ErrorMessage = Strings.Get("TwoFaPasswordForgotUnavailable");
        }

        private void RaiseCommandStates()
        {
            OnPropertyChanged("CanSubmit");
            if (SubmitCommand != null) SubmitCommand.RaiseCanExecuteChanged();

            var back = BackCommand as RelayCommand;
            if (back != null) back.RaiseCanExecuteChanged();

            var forgot = ForgotPasswordCommand as RelayCommand;
            if (forgot != null) forgot.RaiseCanExecuteChanged();
        }

        private static string FormatError(AccountError error)
        {
            if (error == null) return Strings.Get("TwoFaPasswordUnknown");
            switch (error.Kind)
            {
                case AccountErrorKind.SrpPasswordInvalid:
                    return Strings.Get("TwoFaPasswordInvalid");
                case AccountErrorKind.NetworkError:
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("TwoFaPasswordNetwork"),
                        string.IsNullOrEmpty(error.Message)
                            ? Strings.Get("TwoFaPasswordNoConnection")
                            : error.Message);
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }
}
