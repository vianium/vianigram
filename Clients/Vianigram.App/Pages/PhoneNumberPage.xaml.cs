// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// PhoneNumberPage.xaml.cs — code-behind only handles plumbing.
//
// Resolves PhoneNumberPageViewModel through the AppViewModels factory and
// seeds the AppHeader from Resources.resw. All click
// handling and navigation lives in the VM via ICommand.

using System;
using System.ComponentModel;
using Vianigram.App.Services;
using Vianigram.App.ViewModels;
using Vianigram.Kernel.Logging;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class PhoneNumberPage : Page
    {
        private PhoneNumberPageViewModel _vm;
        private bool _isFormattingPhoneInput;

        public PhoneNumberPage()
        {
            EarlyLog.Write("Boot", "PhoneNumberPage ctor begin");
            InitializeComponent();

            HeaderControl.AppName = Strings.Get("PhoneNumberAppName");
            HeaderControl.PageTitle = Strings.Get("PhoneNumberTitle");

            EarlyLog.Write("Boot", "PhoneNumberPage ctor end");
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreatePhoneNumberPageViewModel();
                DataContext = _vm;
            }

            _vm.PropertyChanged += OnVmPropertyChanged;
            ApplyBusyState(_vm.IsBusy);

            await _vm.RefreshCountrySelectionAsync();
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
            base.OnNavigatedFrom(e);
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null) return;
            if (e.PropertyName == "IsBusy" && _vm != null) ApplyBusyState(_vm.IsBusy);
        }

        private void ApplyBusyState(bool busy)
        {
            // Lock the input affordances while send_code is in flight. Cold
            // MTProto handshakes can take 30–90s, so leaving the field hot
            // would let the user retype mid-call and get a confusing state.
            if (PhoneInput != null) PhoneInput.IsEnabled = !busy;
            if (CountryButton != null) CountryButton.IsEnabled = !busy;
        }

        private void PhoneInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isFormattingPhoneInput || _vm == null || PhoneInput == null)
            {
                return;
            }

            string original = PhoneInput.Text ?? string.Empty;
            int digitsBeforeCaret = CountDigitsBeforeIndex(original, PhoneInput.SelectionStart);
            string formatted = _vm.FormatNationalNumber(original);

            if (string.Equals(original, formatted, StringComparison.Ordinal))
            {
                return;
            }

            _isFormattingPhoneInput = true;
            try
            {
                PhoneInput.Text = formatted;
                PhoneInput.SelectionStart = FindCaretIndexForDigitCount(formatted, digitsBeforeCaret);
                PhoneInput.SelectionLength = 0;
                _vm.NationalNumber = formatted;
            }
            finally
            {
                _isFormattingPhoneInput = false;
            }
        }

        private static int CountDigitsBeforeIndex(string value, int index)
        {
            if (string.IsNullOrEmpty(value) || index <= 0)
            {
                return 0;
            }

            int limit = Math.Min(index, value.Length);
            int count = 0;
            for (int i = 0; i < limit; i++)
            {
                if (char.IsDigit(value[i]))
                {
                    count++;
                }
            }

            return count;
        }

        private static int FindCaretIndexForDigitCount(string value, int digitCount)
        {
            if (string.IsNullOrEmpty(value) || digitCount <= 0)
            {
                return 0;
            }

            int seen = 0;
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsDigit(value[i]))
                {
                    continue;
                }

                seen++;
                if (seen >= digitCount)
                {
                    return i + 1;
                }
            }

            return value.Length;
        }
    }
}
