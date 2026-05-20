// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// TopicsPage.xaml.cs — code-behind is intentionally minimal.
//
// InitializeComponent + nav delegations only. The single ItemClick shim
// pushes the clicked TopicVm back through the VM's OpenTopicCommand,
// which then routes through INavigationService directly.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class TopicsPage : Page
    {
        private TopicsPageViewModel _vm;

        public TopicsPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateTopicsPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
        }

        private void OnTopicItemClick(object sender, ItemClickEventArgs e)
        {
            var vm = DataContext as TopicsPageViewModel;
            if (vm == null || e == null) return;
            vm.OpenTopicCommand.Execute(e.ClickedItem);
        }
    }
}
