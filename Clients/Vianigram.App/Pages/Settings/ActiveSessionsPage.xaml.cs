// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ActiveSessionsPage.xaml.cs — code-behind only handles plumbing.
// Resolves the VM through AppViewModels and forwards lifecycle hooks.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Settings
{
    public sealed partial class ActiveSessionsPage : Page
    {
        private ActiveSessionsPageViewModel _vm;

        public ActiveSessionsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateActiveSessionsPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }
    }
}
