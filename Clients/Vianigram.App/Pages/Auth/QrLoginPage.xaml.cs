// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// QrLoginPage.xaml.cs
//
// Code-behind glue for QR login. The VM owns all wire-side state and
// navigation logic; the page handles two presentation-only concerns:
//   1) running the fade-in storyboard whenever a fresh QrText comes in
//      (smoothes the visual handoff between the old and new code), and
//   2) translating the SignInSucceeded VM event into the host-level
//      App.OnUserLoggedIn() / App.NavigateToMainPage(Frame) sequence
//      that the SmsCode + 2FA pages already use.

using System;
using System.ComponentModel;
using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Kernel.Logging;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Auth
{
    public sealed partial class QrLoginPage : Page
    {
        private QrLoginPageViewModel _vm;

        public QrLoginPage()
        {
            InitializeComponent();
            HeaderControl.AppName = Strings.Get("QrLoginAppName");
            HeaderControl.PageTitle = Strings.Get("QrLoginPageTitle");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateQrLoginPageViewModel(App.Composition);
                _vm.PropertyChanged += OnVmPropertyChanged;
                _vm.SignInSucceeded += OnSignInSucceeded;
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(e != null ? e.Parameter : null);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
            base.OnNavigatedFrom(e);
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null || _vm == null) return;
            if (e.PropertyName != "QrText") return;
            // Re-render — fade the freshly painted QR in. Storyboard runs
            // for a quarter second so the swap doesn't feel jarring.
            try
            {
                if (!string.IsNullOrEmpty(_vm.QrText) && QrFadeIn != null)
                {
                    QrFadeIn.Stop();
                    QrFadeIn.Begin();
                }
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "App.QrLogin",
                    "QrFadeIn storyboard threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private void OnSignInSucceeded(object sender, EventArgs e)
        {
            EarlyLog.Write("App.QrLogin", "SignInSucceeded — navigating to chat list");
            App.OnUserLoggedIn();
            if (Frame != null)
            {
                App.NavigateToMainPage(Frame);
            }
        }
    }
}
