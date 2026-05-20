// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ForwardPage.xaml.cs — code-behind only handles plumbing.
// Resolves the VM through AppViewModels and forwards lifecycle hooks.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Compose
{
    public sealed partial class ForwardPage : Page
    {
        private ForwardPageViewModel _vm;

        public ForwardPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateForwardPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }

        private void OnDialogClicked(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            var dialog = e.ClickedItem as DialogVm;
            if (dialog == null) return;

            if (_vm.ToggleSelectCommand != null && _vm.ToggleSelectCommand.CanExecute(dialog))
            {
                _vm.ToggleSelectCommand.Execute(dialog);
            }
        }
    }
}
