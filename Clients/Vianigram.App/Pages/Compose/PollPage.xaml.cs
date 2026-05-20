// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PollPage.xaml.cs — code-behind only handles plumbing.
// Resolves the VM through AppViewModels and forwards lifecycle hooks.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Compose
{
    public sealed partial class PollPage : Page
    {
        private PollPageViewModel _vm;

        public PollPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreatePollPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }

        private void OnRemoveOptionClicked(object sender, RoutedEventArgs e)
        {
            var btn = sender as FrameworkElement;
            if (btn == null) return;
            var option = btn.DataContext as PollOptionVm;
            if (option == null || _vm == null) return;

            if (_vm.RemoveOptionCommand != null && _vm.RemoveOptionCommand.CanExecute(option))
            {
                _vm.RemoveOptionCommand.Execute(option);
            }
        }
    }
}
