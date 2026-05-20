// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// KeyFingerprintPage.xaml.cs — code-behind is intentionally minimal.
//
// InitializeComponent + nav delegations only. Clipboard access is the
// only platform-specific shim — the VM exposes CopyHexRequested with
// the hex string and the page hands it to a DataPackage. The Clipboard
// type is resolved by reflection because the WP8.1 Phone profile does
// not surface it directly.

using System;
using System.Reflection;
using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Secret
{
    public sealed partial class KeyFingerprintPage : Page
    {
        private KeyFingerprintPageViewModel _vm;

        public KeyFingerprintPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateKeyFingerprintPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null)
            {
                _vm.CopyHexRequested = OnCopyHexRequested;
                _vm.OnNavigatedTo(e != null ? e.Parameter : null);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null)
            {
                _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
                _vm.CopyHexRequested = null;
            }
        }

        private void OnCopyHexRequested(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            try
            {
                Type clipboardType = Type.GetType(
                    "Windows.ApplicationModel.DataTransfer.Clipboard, Windows, ContentType=WindowsRuntime");
                if (clipboardType == null) return;

                MethodInfo setContent = null;
                foreach (MethodInfo m in clipboardType.GetTypeInfo().DeclaredMethods)
                {
                    if (m != null && m.Name == "SetContent")
                    {
                        setContent = m;
                        break;
                    }
                }
                if (setContent == null) return;

                var package = new DataPackage();
                package.SetText(hex);
                setContent.Invoke(null, new object[] { package });
            }
            catch
            {
                // Clipboard failure is non-fatal — VM already surfaced the
                // confirmation copy; swallow rather than crash the page.
            }
        }
    }
}
