// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// WelcomePageViewModel.cs
//
// Drives WelcomePage. Pure command surface — no async I/O, no error state.
//
//   ContinueCommand → PhoneNumber route (the user opted to sign in by phone)
//   ScanQrCommand   → QrLogin route (sign in by scanning a QR from another
//                     authorized device)
//   ProxyCommand    → MTProxy settings before the first auth attempt
//   LanguageCommand → language picker

using System.Windows.Input;
using Vianigram.App.Navigation;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class WelcomePageViewModel : ObservableObject
    {
        private readonly INavigationService _nav;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public WelcomePageViewModel()
            : this(null)
        {
        }

        public WelcomePageViewModel(INavigationService nav)
        {
            _nav = nav;
            ContinueCommand = new RelayCommand(_ => OnContinue());
            ScanQrCommand = new RelayCommand(_ => OnScanQr());
            ProxyCommand = new RelayCommand(_ => OnProxy());
            LanguageCommand = new RelayCommand(_ => OnLanguage());
        }

        public ICommand ContinueCommand { get; private set; }
        public ICommand ScanQrCommand { get; private set; }
        public ICommand ProxyCommand { get; private set; }
        public ICommand LanguageCommand { get; private set; }

        private void OnContinue()
        {
            if (_nav != null) _nav.NavigateTo(Route.PhoneNumber);
        }

        private void OnScanQr()
        {
            if (_nav != null) _nav.NavigateTo(Route.QrLogin);
        }

        private void OnProxy()
        {
            if (_nav != null) _nav.NavigateTo(Route.ProxySettings);
        }

        private void OnLanguage()
        {
            if (_nav != null) _nav.NavigateTo(Route.LanguagePicker);
        }
    }
}
