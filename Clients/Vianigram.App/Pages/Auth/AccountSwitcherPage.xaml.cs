// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AccountSwitcherPage.xaml.cs — code-behind is intentionally minimal.
// VM is created via the AppViewModels factory in OnNavigatedTo.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Auth
{
    public sealed partial class AccountSwitcherPage : Page
    {
        private AccountSwitcherPageViewModel _vm;

        public AccountSwitcherPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateAccountSwitcherPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
            base.OnNavigatedFrom(e);
        }

        private void OnAccountItemClick(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            _vm.SwitchCommand.Execute(e.ClickedItem);
        }
    }
}
