// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.Account.Application.Commands;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Kernel.Result;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class SignUpPageViewModel : ObservableObject
    {
        private readonly IAccountApi _account;
        private readonly INavigationService _nav;
        private string _firstName;
        private string _lastName;
        private string _errorMessage;
        private bool _isBusy;

        public SignUpPageViewModel()
            : this(null, null)
        {
        }

        public SignUpPageViewModel(IAccountApi account, INavigationService nav)
        {
            _account = account;
            _nav = nav;
            SubmitCommand = new AsyncCommand(_ => SubmitAsync(CancellationToken.None), _ => CanSubmit);
            BackCommand = new RelayCommand(_ => OnBack(), _ => !IsBusy);
            _firstName = string.Empty;
            _lastName = string.Empty;
        }

        public event EventHandler SubmitSucceeded;

        public ICommand BackCommand { get; private set; }
        public AsyncCommand SubmitCommand { get; private set; }

        public string FirstName
        {
            get { return _firstName; }
            set
            {
                if (SetProperty(ref _firstName, value ?? string.Empty))
                {
                    ErrorMessage = null;
                    RaiseCommandStates();
                }
            }
        }

        public string LastName
        {
            get { return _lastName; }
            set
            {
                if (SetProperty(ref _lastName, value ?? string.Empty))
                {
                    ErrorMessage = null;
                }
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
            get { return !IsBusy && !string.IsNullOrWhiteSpace(_firstName); }
        }

        public async Task SubmitAsync(CancellationToken ct)
        {
            if (!CanSubmit) return;

            ErrorMessage = null;
            if (_account == null)
            {
                ErrorMessage = Strings.Get("SignUpAccountUnavailable");
                return;
            }

            IsBusy = true;
            try
            {
                Result<AuthOutcome, AccountError> result;
                try
                {
                    result = await _account.SignUpAsync(_firstName, _lastName, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    ErrorMessage = string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("SignUpUnexpected"),
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

        private void RaiseCommandStates()
        {
            OnPropertyChanged("CanSubmit");
            if (SubmitCommand != null) SubmitCommand.RaiseCanExecuteChanged();

            var back = BackCommand as RelayCommand;
            if (back != null) back.RaiseCanExecuteChanged();
        }

        private static string FormatError(AccountError error)
        {
            if (error == null) return Strings.Get("SignUpUnknown");
            switch (error.Kind)
            {
                case AccountErrorKind.PhoneCodeInvalid:
                    return Strings.Get("SmsCodeIncorrect");
                case AccountErrorKind.PhoneCodeExpired:
                    return Strings.Get("SmsCodeExpired");
                case AccountErrorKind.NetworkError:
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        Strings.Get("SignUpNetwork"),
                        string.IsNullOrEmpty(error.Message)
                            ? Strings.Get("TwoFaPasswordNoConnection")
                            : error.Message);
                default:
                    return string.IsNullOrEmpty(error.Message)
                        ? error.Kind.ToString()
                        : error.Message;
            }
        }
    }
}
