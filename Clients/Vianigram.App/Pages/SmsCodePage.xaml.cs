// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SmsCodePage.xaml.cs — code-behind handles digit-cell focus management
// and the visual feedback around an invalid code (red flash + haptic).
//
// The five digit cells live in the page (rather than the VM) because their
// behavior is presentation-only: focus traversal, paste-distribution,
// backspace-back, and a brief red shake when verification fails. The VM
// owns the wire-side state (Code, IsBusy, ErrorMessage, navigation).

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.App.Services;
using Vianigram.App.ViewModels;
using Vianigram.Kernel.Logging;
using Windows.Phone.Devices.Notification;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class SmsCodePage : Page
    {
        private SmsCodePageViewModel _vm;
        private TextBox[] _cells;
        private bool _isSyncingFromVm;
        private bool _isVerifying;
        private Brush _normalBorderBrush;
        private Brush _errorBorderBrush;

        public SmsCodePage()
        {
            EarlyLog.Write("Boot", "SmsCodePage ctor begin");
            try
            {
                InitializeComponent();
                EarlyLog.Write("Boot", "SmsCodePage InitializeComponent ok");
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "Boot",
                    "SmsCodePage InitializeComponent THREW " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }

            try
            {
                HeaderControl.AppName = Strings.Get("SmsCodeAppName");
                HeaderControl.PageTitle = Strings.Get("SmsCodePageTitle");
                _cells = new[] { Digit1, Digit2, Digit3, Digit4, Digit5 };
                EarlyLog.Write("Boot", "SmsCodePage header+cells wired ok");
            }
            catch (Exception ex)
            {
                EarlyLog.Write(
                    "Boot",
                    "SmsCodePage header/cells THREW " + ex.GetType().Name + ": " + ex.Message);
                throw;
            }

            // Cache the brushes once so we can flip cells red on a failed
            // verify and back to neutral when the user starts retyping.
            // The theme brushes live inside ThemeDictionaries in Tokens.xaml
            // (merged into Application.Resources) and the indexer throws
            // KeyNotFoundException — surfacing as COMException in WinRT —
            // when the lookup misses. Fall through to hardcoded fallbacks
            // so the page never crashes before the user can type.
            _normalBorderBrush = TryGetThemeBrush("VgFg3Brush")
                ?? new SolidColorBrush(Color.FromArgb(0x66, 0xFF, 0xFF, 0xFF));
            _errorBorderBrush = TryGetThemeBrush("VgDangerBrush")
                ?? new SolidColorBrush(Color.FromArgb(0xFF, 0xE5, 0x14, 0x00));

            EarlyLog.Write(
                "Boot",
                "SmsCodePage brushes resolved normal=" +
                (_normalBorderBrush != null ? "ok" : "null") +
                " error=" +
                (_errorBorderBrush != null ? "ok" : "null"));

            EarlyLog.Write("Boot", "SmsCodePage ctor end");
        }

        private static Brush TryGetThemeBrush(string key)
        {
            // 1. Try the application root dictionary (works when the resource
            //    sits at root — e.g. theme-independent accent/danger brushes).
            try
            {
                if (Application.Current != null
                    && Application.Current.Resources != null
                    && Application.Current.Resources.ContainsKey(key))
                {
                    return Application.Current.Resources[key] as Brush;
                }
            }
            catch
            {
            }

            // 2. Walk the ThemeDictionaries explicitly — fg/bg brushes live
            //    inside the Default/Light dictionary and don't bubble up to
            //    the root ContainsKey check.
            try
            {
                if (Application.Current != null
                    && Application.Current.Resources != null)
                {
                    var theme = Application.Current.RequestedTheme == ApplicationTheme.Light
                        ? "Light" : "Default";
                    object dict;
                    if (Application.Current.Resources.ThemeDictionaries != null
                        && Application.Current.Resources.ThemeDictionaries.TryGetValue(theme, out dict))
                    {
                        var rd = dict as ResourceDictionary;
                        if (rd != null && rd.ContainsKey(key))
                        {
                            return rd[key] as Brush;
                        }
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            string phone = e != null ? (e.Parameter as string) : null;
            _vm = AppViewModels.CreateSmsCodePageViewModel(phone);
            DataContext = _vm;
            _vm.PropertyChanged += OnVmPropertyChanged;

            SyncCellsFromCode(_vm.Code);
            ApplyBusyState(_vm.IsBusy);

            var first = FirstEmptyCell();
            if (first != null)
            {
                first.Loaded += FocusOnce;
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            if (_vm != null)
            {
                _vm.PropertyChanged -= OnVmPropertyChanged;
                _vm.StopCountdown();
            }
            base.OnNavigatedFrom(e);
        }

        private void FocusOnce(object sender, RoutedEventArgs e)
        {
            var tb = sender as TextBox;
            if (tb == null) return;
            tb.Loaded -= FocusOnce;
            // Pointer focus so the numeric SIP rises immediately on page
            // arrival — Programmatic focus would land on the cell but
            // leave the keyboard hidden, forcing the user to tap.
            try { tb.Focus(FocusState.Pointer); } catch { }
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null) return;
            if (e.PropertyName == "Code")
            {
                // The VM cleared (resend) or set the code from elsewhere —
                // mirror it into the cells so the UI stays in sync.
                if (_vm != null) SyncCellsFromCode(_vm.Code);
            }
            else if (e.PropertyName == "IsBusy")
            {
                if (_vm != null) ApplyBusyState(_vm.IsBusy);
            }
        }

        private void ApplyBusyState(bool busy)
        {
            // Lock the cells while a verify is in flight so a stray keystroke
            // doesn't stomp on the in-progress request.
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i].IsEnabled = !busy;
            }
        }

        private void OnDigitTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isSyncingFromVm) return;

            var box = sender as TextBox;
            if (box == null) return;

            // Any new keystroke clears a previous error highlight.
            ClearErrorHighlight();

            string raw = box.Text ?? string.Empty;
            string digits = ExtractDigits(raw);

            int index = IndexOfCell(box);
            if (index < 0) return;

            if (digits.Length == 0)
            {
                SetCellSilently(box, string.Empty);
                PushCodeToVm();
                return;
            }

            char first = digits[0];
            SetCellSilently(box, first.ToString());

            string overflow = digits.Length > 1 ? digits.Substring(1) : string.Empty;
            int writeIndex = index + 1;
            for (int i = 0; i < overflow.Length && writeIndex < _cells.Length; i++, writeIndex++)
            {
                SetCellSilently(_cells[writeIndex], overflow[i].ToString());
            }

            int nextIndex = Math.Min(index + Math.Max(1, digits.Length), _cells.Length - 1);
            // FocusState.Pointer (vs Programmatic) keeps the SIP / numeric
            // keypad open as we hop between cells. Programmatic focus on
            // WP 8.1 does NOT raise the SIP — that's why every cell after
            // the first one used to feel "dead" (the user had to tap it
            // to bring the keyboard back up).
            try { _cells[nextIndex].Focus(FocusState.Pointer); } catch { }

            PushCodeToVm();
            TryAutoVerify();
        }

        private void OnDigitKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key != VirtualKey.Back) return;

            var box = sender as TextBox;
            if (box == null) return;
            if (!string.IsNullOrEmpty(box.Text)) return;

            int index = IndexOfCell(box);
            if (index <= 0) return;

            ClearErrorHighlight();

            var prev = _cells[index - 1];
            SetCellSilently(prev, string.Empty);
            // Same SIP-keep-alive treatment as the forward-jump in
            // OnDigitTextChanged — Pointer focus keeps the numeric keypad
            // visible when Backspace pulls focus to the previous cell.
            try { prev.Focus(FocusState.Pointer); } catch { }
            PushCodeToVm();
            e.Handled = true;
        }

        private void OnEditNumberTapped(object sender, TappedRoutedEventArgs e)
        {
            if (_vm != null && _vm.EditNumberCommand != null && _vm.EditNumberCommand.CanExecute(null))
            {
                _vm.EditNumberCommand.Execute(null);
            }
        }

        private void OnResendTapped(object sender, TappedRoutedEventArgs e)
        {
            if (_vm != null && _vm.ResendCommand != null && _vm.ResendCommand.CanExecute(null))
            {
                _vm.ResendCommand.Execute(null);
            }
        }

        private void PushCodeToVm()
        {
            if (_vm == null) return;
            _vm.Code = ReadCodeFromCells();
        }

        private string ReadCodeFromCells()
        {
            var sb = new System.Text.StringBuilder(_cells.Length);
            for (int i = 0; i < _cells.Length; i++)
            {
                string t = _cells[i].Text;
                if (!string.IsNullOrEmpty(t)) sb.Append(t[0]);
                else break;
            }
            return sb.ToString();
        }

        private void SyncCellsFromCode(string code)
        {
            _isSyncingFromVm = true;
            try
            {
                for (int i = 0; i < _cells.Length; i++)
                {
                    string ch = (code != null && i < code.Length) ? code[i].ToString() : string.Empty;
                    if (_cells[i].Text != ch) _cells[i].Text = ch;
                }
            }
            finally
            {
                _isSyncingFromVm = false;
            }
        }

        private void SetCellSilently(TextBox cell, string value)
        {
            _isSyncingFromVm = true;
            try { cell.Text = value ?? string.Empty; }
            finally { _isSyncingFromVm = false; }
        }

        private TextBox FirstEmptyCell()
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (string.IsNullOrEmpty(_cells[i].Text)) return _cells[i];
            }
            return _cells[_cells.Length - 1];
        }

        private int IndexOfCell(TextBox box)
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (object.ReferenceEquals(_cells[i], box)) return i;
            }
            return -1;
        }

        private static string ExtractDigits(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var sb = new System.Text.StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c >= '0' && c <= '9') sb.Append(c);
            }
            return sb.ToString();
        }

        private async void TryAutoVerify()
        {
            if (_isVerifying || _vm == null) return;
            if (!_vm.IsCodeComplete) return;

            _isVerifying = true;
            try
            {
                var outcome = await _vm.VerifyAsync(CancellationToken.None).ConfigureAwait(true);
                if (outcome == VerifyOutcome.Success && Frame != null)
                {
                    App.OnUserLoggedIn();
                    App.NavigateToMainPage(Frame);
                }
                else if (outcome == VerifyOutcome.Fail)
                {
                    await SignalInvalidCodeAsync().ConfigureAwait(true);
                }
                else if (outcome == VerifyOutcome.TwoFaRequired && Frame != null)
                {
                    Frame.Navigate(
                        typeof(Vianigram.App.Pages.Auth.TwoFaPasswordPage),
                        _vm != null ? _vm.PasswordHint : null);
                }
                else if (outcome == VerifyOutcome.SignUpRequired && Frame != null)
                {
                    Frame.Navigate(typeof(Vianigram.App.Pages.Auth.SignUpPage));
                }
            }
            finally
            {
                _isVerifying = false;
            }
        }

        private async Task SignalInvalidCodeAsync()
        {
            // Visual: paint the underline red on every cell.
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i].BorderBrush = _errorBorderBrush;
            }

            // Haptic: a short buzz so the failure is felt, not just seen.
            try
            {
                var device = VibrationDevice.GetDefault();
                if (device != null) device.Vibrate(TimeSpan.FromMilliseconds(120));
            }
            catch
            {
                // Vibration capability might not be granted; the visual cue
                // already covers the user.
            }

            // Hold the red flash long enough to register, then clear.
            await Task.Delay(450).ConfigureAwait(true);

            _vm.Code = string.Empty;
            ClearErrorHighlight();

            var first = _cells[0];
            // Pointer focus keeps the SIP open after a wrong-code reset
            // so the user can immediately retype without tapping again.
            try { first.Focus(FocusState.Pointer); } catch { }
        }

        private void ClearErrorHighlight()
        {
            if (_normalBorderBrush == null) return;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].BorderBrush != _normalBorderBrush)
                    _cells[i].BorderBrush = _normalBorderBrush;
            }
        }
    }
}
