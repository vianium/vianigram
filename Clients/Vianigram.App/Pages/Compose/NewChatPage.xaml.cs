// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// NewChatPage.xaml.cs — code-behind only handles plumbing.
// Resolves the VM through AppViewModels and forwards lifecycle hooks.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Compose
{
    public sealed partial class NewChatPage : Page
    {
        private NewChatPageViewModel _vm;

        public NewChatPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateNewChatPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }

        private void OnContactClicked(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            var contact = e.ClickedItem as ContactVm;
            if (contact == null) return;

            if (_vm.ToggleSelectCommand != null && _vm.ToggleSelectCommand.CanExecute(contact))
            {
                _vm.ToggleSelectCommand.Execute(contact);
            }
        }
    }
}
