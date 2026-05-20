// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Kernel.Logging;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class CountryPickerPage : Page
    {
        private CountryPickerPageViewModel _vm;

        public CountryPickerPage()
        {
            EarlyLog.Write("Boot", "CountryPickerPage ctor begin");
            InitializeComponent();
            HeaderControl.AppName = Strings.Get("CountryPickerAppName");
            HeaderControl.PageTitle = Strings.Get("CountryPickerTitle");
            CountrySearchBox.PlaceholderText = Strings.Get("CountryPickerSearchPlaceholder");
            EarlyLog.Write("Boot", "CountryPickerPage ctor end");
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateCountryPickerPageViewModel();
                DataContext = _vm;
            }

            await _vm.LoadAsync();
        }

        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            _vm.SelectCountryCommand.Execute(e.ClickedItem);
        }
    }
}
