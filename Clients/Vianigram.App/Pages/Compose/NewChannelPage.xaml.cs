// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// NewChannelPage.xaml.cs — code-behind only handles plumbing.
// Resolves the VM through AppViewModels and forwards lifecycle hooks.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Compose
{
    public sealed partial class NewChannelPage : Page
    {
        private NewChannelPageViewModel _vm;

        public NewChannelPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateNewChannelPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }

        private void OnUsernameTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_vm == null) return;
            var cmd = _vm.CheckUsernameCommand;
            if (cmd != null && cmd.CanExecute(null))
            {
                cmd.Execute(null);
            }
        }
    }
}
