// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.ComponentModel;
using Vianigram.App.Pages;
using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Auth
{
    public sealed partial class TwoFaPasswordPage : Page
    {
        private TwoFaPasswordPageViewModel _vm;

        public TwoFaPasswordPage()
        {
            InitializeComponent();
            HeaderControl.AppName = Strings.Get("TwoFaPasswordAppName");
            HeaderControl.PageTitle = Strings.Get("TwoFaPasswordPageTitle");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateTwoFaPasswordPageViewModel(App.Composition);
                _vm.SubmitSucceeded += OnSubmitSucceeded;
                _vm.PropertyChanged += OnVmPropertyChanged;
                DataContext = _vm;
            }

            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
            PasswordInput.Password = string.Empty;
            PasswordInput.Loaded += FocusPasswordOnce;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
            PasswordInput.Loaded -= FocusPasswordOnce;
            base.OnNavigatedFrom(e);
        }

        private void FocusPasswordOnce(object sender, RoutedEventArgs e)
        {
            PasswordInput.Loaded -= FocusPasswordOnce;
            try { PasswordInput.Focus(FocusState.Programmatic); } catch { }
        }

        private void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_vm != null) _vm.Password = PasswordInput.Password;
        }

        private void OnPasswordKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Enter) return;
            if (_vm != null && _vm.SubmitCommand != null && _vm.SubmitCommand.CanExecute(null))
            {
                _vm.SubmitCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null || e.PropertyName != "IsBusy" || _vm == null) return;
            PasswordInput.IsEnabled = !_vm.IsBusy;
        }

        private void OnSubmitSucceeded(object sender, EventArgs e)
        {
            App.OnUserLoggedIn();
            if (Frame != null)
            {
                App.NavigateToMainPage(Frame);
            }
        }
    }
}
