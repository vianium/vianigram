// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.ObjectModel;
using System.Windows.Input;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class AccountSummaryVm : ObservableObject
    {
        private string _displayName;
        private string _phoneNumber;
        private bool _isActive;
        private string _avatarLetter;

        public string DisplayName
        {
            get { return _displayName; }
            set { SetProperty(ref _displayName, value); }
        }

        public string PhoneNumber
        {
            get { return _phoneNumber; }
            set { SetProperty(ref _phoneNumber, value); }
        }

        public bool IsActive
        {
            get { return _isActive; }
            set { SetProperty(ref _isActive, value); }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }
    }

    public sealed class AccountSwitcherPageViewModel : ObservableObject
    {
        private readonly INavigationService _nav;
        private readonly IAccountApi _account;

        public AccountSwitcherPageViewModel()
            : this(null, null)
        {
        }

        public AccountSwitcherPageViewModel(INavigationService nav, IAccountApi account)
        {
            _nav = nav;
            _account = account;
            Accounts = new ObservableCollection<AccountSummaryVm>();
            SwitchCommand = new RelayCommand(OnSwitch, _ => true);
            AddAccountCommand = new RelayCommand(OnAddAccount, _ => true);
            RemoveCommand = new RelayCommand(OnRemove, _ => true);
        }

        public ObservableCollection<AccountSummaryVm> Accounts { get; private set; }

        public ICommand SwitchCommand { get; private set; }
        public ICommand AddAccountCommand { get; private set; }
        public ICommand RemoveCommand { get; private set; }

        public void OnNavigatedTo(object parameter)
        {
            ReloadAccounts();
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        private void ReloadAccounts()
        {
            Accounts.Clear();
            AccountStateSnapshot state = _account == null ? null : _account.CurrentState;
            if (state == null || state.StateKind != AuthState.AuthStateKind.Authorized)
            {
                Accounts.Add(new AccountSummaryVm
                {
                    DisplayName = "No active account",
                    PhoneNumber = string.Empty,
                    IsActive = true,
                    AvatarLetter = "?"
                });
                return;
            }

            string phone = state.Phone ?? string.Empty;
            string display = string.IsNullOrEmpty(phone)
                ? "User " + (state.UserId.HasValue ? state.UserId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "")
                : phone;
            Accounts.Add(new AccountSummaryVm
            {
                DisplayName = display,
                PhoneNumber = phone,
                IsActive = true,
                AvatarLetter = AvatarFrom(display)
            });
        }

        private static string AvatarFrom(string display)
        {
            if (string.IsNullOrEmpty(display)) return "?";
            return display.Substring(0, 1).ToUpperInvariant();
        }

        private void OnSwitch(object parameter)
        {
            var account = parameter as AccountSummaryVm;
            if (account == null) return;

            for (int i = 0; i < Accounts.Count; i++)
            {
                Accounts[i].IsActive = object.ReferenceEquals(Accounts[i], account);
            }

            if (_nav != null) _nav.NavigateTo(Route.ChatList);
        }

        private void OnAddAccount(object parameter)
        {
            if (_nav != null) _nav.NavigateTo(Route.PhoneNumber);
        }

        private void OnRemove(object parameter)
        {
            var account = parameter as AccountSummaryVm;
            if (account == null) return;
            if (Accounts.Contains(account)) Accounts.Remove(account);
        }
    }
}
