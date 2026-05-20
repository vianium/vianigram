// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SearchPage.xaml.cs — code-behind only handles plumbing.
// Resolves the VM through AppViewModels and forwards lifecycle hooks.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Settings
{
    public sealed partial class SearchPage : Page
    {
        private SearchPageViewModel _vm;

        public SearchPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateSearchPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }

        private void OnChatHitClicked(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            if (_vm.OpenChatCommand != null && _vm.OpenChatCommand.CanExecute(e.ClickedItem))
                _vm.OpenChatCommand.Execute(e.ClickedItem);
        }

        private void OnContactHitClicked(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            if (_vm.OpenContactCommand != null && _vm.OpenContactCommand.CanExecute(e.ClickedItem))
                _vm.OpenContactCommand.Execute(e.ClickedItem);
        }

        private void OnMessageHitClicked(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            if (_vm.OpenMessageCommand != null && _vm.OpenMessageCommand.CanExecute(e.ClickedItem))
                _vm.OpenMessageCommand.Execute(e.ClickedItem);
        }
    }
}
