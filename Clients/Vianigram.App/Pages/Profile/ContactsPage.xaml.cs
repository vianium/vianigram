// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ContactsPage.xaml.cs — code-behind is intentionally minimal.
// VM is created via the AppViewModels factory in OnNavigatedTo.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Profile
{
    public sealed partial class ContactsPage : Page
    {
        private ContactsPageViewModel _vm;

        public ContactsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateContactsPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
            base.OnNavigatedFrom(e);
        }

        private void OnContactItemClick(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            if (_vm.SelectContactCommand != null && _vm.SelectContactCommand.CanExecute(e.ClickedItem))
            {
                _vm.SelectContactCommand.Execute(e.ClickedItem);
            }
        }
    }
}
