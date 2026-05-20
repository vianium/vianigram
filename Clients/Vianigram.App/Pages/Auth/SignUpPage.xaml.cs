// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.ComponentModel;
using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Auth
{
    public sealed partial class SignUpPage : Page
    {
        private SignUpPageViewModel _vm;

        public SignUpPage()
        {
            InitializeComponent();
            HeaderControl.AppName = Strings.Get("SignUpAppName");
            HeaderControl.PageTitle = Strings.Get("SignUpPageTitle");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateSignUpPageViewModel(App.Composition);
                _vm.SubmitSucceeded += OnSubmitSucceeded;
                _vm.PropertyChanged += OnVmPropertyChanged;
                DataContext = _vm;
            }

            FirstNameInput.Loaded += FocusFirstNameOnce;
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            FirstNameInput.Loaded -= FocusFirstNameOnce;
            if (_vm != null)
            {
                _vm.SubmitSucceeded -= OnSubmitSucceeded;
                _vm.PropertyChanged -= OnVmPropertyChanged;
            }
            base.OnNavigatedFrom(e);
        }

        private void FocusFirstNameOnce(object sender, RoutedEventArgs e)
        {
            FirstNameInput.Loaded -= FocusFirstNameOnce;
            try { FirstNameInput.Focus(FocusState.Pointer); } catch { }
        }

        private void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
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
            FirstNameInput.IsEnabled = !_vm.IsBusy;
            LastNameInput.IsEnabled = !_vm.IsBusy;
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
