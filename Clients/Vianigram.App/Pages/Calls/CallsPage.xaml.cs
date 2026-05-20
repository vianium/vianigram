// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CallsPage.xaml.cs
//
// Code-behind stays as UI plumbing: VM creation, navigation lifecycle, and
// forwarding row taps/button taps to CallsPageViewModel.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Calls
{
    public sealed partial class CallsPage : Page
    {
        private CallsPageViewModel _vm;

        public CallsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateCallsPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }

        private void OnCallItemClick(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            _vm.OpenProfile(e.ClickedItem as CallLogRow);
        }

        private async void OnCallActionClicked(object sender, RoutedEventArgs e)
        {
            if (_vm == null) return;
            FrameworkElement fe = sender as FrameworkElement;
            if (fe == null) return;
            await _vm.StartCallAsync(fe.Tag as CallLogRow).ConfigureAwait(true);
        }
    }
}
