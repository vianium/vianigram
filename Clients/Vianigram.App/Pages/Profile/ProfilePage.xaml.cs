// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ProfilePage.xaml.cs — code-behind is intentionally minimal.
// VM is created via the AppViewModels factory in OnNavigatedTo.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Profile
{
    public sealed partial class ProfilePage : Page
    {
        private ProfilePageViewModel _vm;

        public ProfilePage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateProfilePageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
            base.OnNavigatedFrom(e);
        }

        private async void OnNotificationsToggled(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            var toggle = sender as ToggleSwitch;
            if (toggle == null) return;
            await _vm.UpdateNotificationsEnabledAsync(toggle.IsOn).ConfigureAwait(true);
        }
    }
}
