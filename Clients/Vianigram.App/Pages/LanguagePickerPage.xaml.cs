// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LanguagePickerPage.xaml.cs — code-behind only handles plumbing.
//
// Resolves the VM through AppViewModels, seeds the AppHeader text from
// Resources.resw, and forwards the ListView ItemClick to the VM command.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Kernel.Logging;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class LanguagePickerPage : Page
    {
        private LanguagePickerPageViewModel _vm;

        public LanguagePickerPage()
        {
            EarlyLog.Write("Boot", "LanguagePickerPage ctor begin");
            InitializeComponent();
            HeaderControl.AppName = Strings.Get("LanguagePickerAppName");
            HeaderControl.PageTitle = Strings.Get("LanguagePickerTitle");
            EarlyLog.Write("Boot", "LanguagePickerPage ctor end");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateLanguagePickerPageViewModel();
                DataContext = _vm;
            }
        }

        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            if (_vm == null || e == null) return;
            _vm.SelectLanguageCommand.Execute(e.ClickedItem);
        }
    }
}
