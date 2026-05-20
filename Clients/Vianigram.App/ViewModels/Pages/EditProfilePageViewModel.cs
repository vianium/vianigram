// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// EditProfilePageViewModel.cs — editable profile form VM.
// Wires IAccountApi (UpdateProfileAsync / GetSelfAsync). Cancel routes through INavigationService.

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
using Vianigram.Media.Ports.Inbound;
using AccountUnit = Vianigram.Account.Domain.ValueObjects.Unit;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class EditProfilePageViewModel : ObservableObject
    {
        private readonly IAccountApi _account;
        private readonly INavigationService _nav;

        private string _firstName;
        private string _lastName;
        private string _username;
        private string _bio;
        private bool _isSaving;
        private string _errorMessage;
        private string _statusText;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public EditProfilePageViewModel()
            : this(null, null, null)
        {
        }

        public EditProfilePageViewModel(IAccountApi account, IMediaApi media, INavigationService nav)
        {
            _account = account;
            _nav = nav;

            _firstName = string.Empty;
            _lastName = string.Empty;
            _username = string.Empty;
            _bio = string.Empty;
            _statusText = string.Empty;

            SaveCommand = new AsyncCommand(_ => SaveAsync(), _ => CanSave);
            CancelCommand = new RelayCommand(_ => OnCancel(), _ => true);
        }

        // ---- Editable fields (TwoWay) --------------------------------

        public string FirstName
        {
            get { return _firstName; }
            set
            {
                if (SetProperty(ref _firstName, value))
                {
                    OnPropertyChanged("CanSave");
                    ((AsyncCommand)SaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public string LastName
        {
            get { return _lastName; }
            set { SetProperty(ref _lastName, value); }
        }

        public string Username
        {
            get { return _username; }
            set { SetProperty(ref _username, value); }
        }

        public string Bio
        {
            get { return _bio; }
            set { SetProperty(ref _bio, value); }
        }

        // ---- State ----------------------------------------------------

        public bool IsSaving
        {
            get { return _isSaving; }
            private set
            {
                if (SetProperty(ref _isSaving, value))
                {
                    OnPropertyChanged("CanSave");
                    ((AsyncCommand)SaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanSave
        {
            get { return !_isSaving && !string.IsNullOrWhiteSpace(_firstName); }
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

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        // ---- Commands -------------------------------------------------

        public ICommand SaveCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        // ---- Navigation lifecycle ------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            StatusText = string.Empty;
            var ignore = HydrateAsync(CancellationToken.None);
        }

        private async Task HydrateAsync(CancellationToken ct)
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
                AppLog.For("App.EditProfilePage").Error("GetSelfAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatError(result.Error);
                return;
            }

            SelfProfile p = result.Value;
            if (p == null) return;

            FirstName = p.FirstName ?? string.Empty;
            LastName = p.LastName ?? string.Empty;
            Username = p.Username ?? string.Empty;
            Bio = p.Bio ?? string.Empty;
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Command handlers ----------------------------------------

        private async Task SaveAsync()
        {
            ErrorMessage = null;

            if (string.IsNullOrWhiteSpace(_firstName))
            {
                ErrorMessage = "First name is required.";
                return;
            }

            if (_account == null)
            {
                ErrorMessage = "Account service not available.";
                return;
            }

            IsSaving = true;
            StatusText = "Saving...";
            try
            {
                Result<AccountUnit, AccountError> result;
                try
                {
                    result = await _account.UpdateProfileAsync(
                        _firstName ?? string.Empty,
                        _lastName ?? string.Empty,
                        _username ?? string.Empty,
                        _bio ?? string.Empty,
                        CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.For("App.EditProfilePage").Error("UpdateProfileAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                StatusText = "Profile updated";
                if (_nav != null && _nav.CanGoBack) _nav.GoBack();
            }
            finally
            {
                IsSaving = false;
            }
        }

        private void OnCancel()
        {
            if (_nav != null && _nav.CanGoBack) _nav.GoBack();
        }

        private static string FormatError(AccountError error)
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

    }
}
