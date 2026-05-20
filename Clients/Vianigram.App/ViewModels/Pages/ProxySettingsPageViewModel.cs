// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ProxySettingsPageViewModel.cs
//
// MTProxy descriptor editor. Persistence flows through ISettingsApi
// (Vianigram.Settings) so the saved value survives app launches AND is
// picked up by the live MTProto transport via the IProxyRuntimeSink
// outbound port (canonical impl in
// Vianigram.Composition.Infrastructure.MtProxyRuntimeSink).
//
// UI states the VM exposes:
//   * Enabled toggle  — flips between disabled (direct dial) and the
//                       active descriptor. Saving while Enabled=true
//                       requires a valid host / port / secret.
//   * Host / Port     — proxy endpoint (the proxy server, NOT the
//                       Telegram DC; the DC is chosen by Telegram client
//                       state and embedded in the obfuscated handshake).
//   * SecretInput     — paste either a tg:// URL or a raw hex/url-safe
//                       base64 secret. The VM parses on every change and
//                       surfaces ParsedSecretSummary for confirmation.
//   * Test            — fires a TCP probe at host:port with a 5-second
//                       deadline. A green TestResult means the proxy
//                       socket accepted us; correctness of the secret
//                       is verified by the live MTProto reconnect.

using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.Kernel.Result;
using Vianigram.Settings.Domain;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Infrastructure;
using Vianigram.Settings.Ports.Inbound;
using Windows.Networking;
using Windows.Networking.Sockets;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class ProxySettingsPageViewModel : ObservableObject
    {
        private readonly INavigationService _nav;
        private readonly ISettingsApi _settings;

        // UI-bound state
        private bool _enabled;
        private string _host;
        private string _portText;
        private int _port;
        private string _secretInput;
        private string _parsedSecretSummary;
        private ProxySecretMode _parsedMode;
        private byte[] _parsedSecret;
        private string _parsedFakeTlsDomain;
        private string _label;
        private bool _isTesting;
        private string _testResult;
        private bool _isLoaded;

        public ProxySettingsPageViewModel()
            : this(null, null)
        {
        }

        public ProxySettingsPageViewModel(INavigationService nav, ISettingsApi settings)
        {
            _nav = nav;
            _settings = settings;

            _port = 443;
            _portText = "443";
            _host = string.Empty;
            _secretInput = string.Empty;
            _label = string.Empty;
            _parsedSecretSummary = string.Empty;
            _parsedMode = ProxySecretMode.Legacy;
            _parsedSecret = null;
            _parsedFakeTlsDomain = string.Empty;
            _testResult = string.Empty;

            SaveCommand   = new AsyncCommand(_ => SaveAsync(),   _ => CanSave);
            TestCommand   = new AsyncCommand(_ => TestAsync(),   _ => CanTest);
            ClearCommand  = new RelayCommand(_ => ClearProxy(),  _ => true);
            CancelCommand = new RelayCommand(_ => GoBack(),      _ => true);
        }

        public bool Enabled
        {
            get { return _enabled; }
            set
            {
                if (SetProperty(ref _enabled, value))
                {
                    OnPropertyChanged("CanSave");
                }
            }
        }

        public string Host
        {
            get { return _host; }
            set
            {
                if (SetProperty(ref _host, value ?? string.Empty))
                {
                    OnPropertyChanged("CanSave");
                    OnPropertyChanged("CanTest");
                }
            }
        }

        public string PortText
        {
            get { return _portText; }
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _portText, normalized))
                {
                    int parsed;
                    if (int.TryParse(normalized, out parsed) && parsed > 0 && parsed <= 65535)
                    {
                        _port = parsed;
                    }
                    else
                    {
                        _port = 0;
                    }
                    OnPropertyChanged("CanSave");
                    OnPropertyChanged("CanTest");
                }
            }
        }

        public string SecretInput
        {
            get { return _secretInput; }
            set
            {
                string normalized = value ?? string.Empty;
                if (SetProperty(ref _secretInput, normalized))
                {
                    ReparseSecret();
                    OnPropertyChanged("CanSave");
                }
            }
        }

        public string ParsedSecretSummary
        {
            get { return _parsedSecretSummary; }
            private set { SetProperty(ref _parsedSecretSummary, value ?? string.Empty); }
        }

        public string Label
        {
            get { return _label; }
            set { SetProperty(ref _label, value ?? string.Empty); }
        }

        public bool IsTesting
        {
            get { return _isTesting; }
            private set
            {
                if (SetProperty(ref _isTesting, value))
                {
                    OnPropertyChanged("CanTest");
                    OnPropertyChanged("CanSave");
                }
            }
        }

        public string TestResult
        {
            get { return _testResult; }
            private set { SetProperty(ref _testResult, value ?? string.Empty); }
        }

        public bool CanSave
        {
            get
            {
                if (_isTesting) return false;
                if (!_enabled) return true;  // Disabling is always allowed.
                if (string.IsNullOrEmpty(_host)) return false;
                if (_port <= 0 || _port > 65535) return false;
                if (_parsedSecret == null || _parsedSecret.Length != 16) return false;
                if (_parsedMode == ProxySecretMode.FakeTls && string.IsNullOrEmpty(_parsedFakeTlsDomain)) return false;
                return true;
            }
        }

        public bool CanTest
        {
            get
            {
                if (_isTesting) return false;
                if (string.IsNullOrEmpty(_host)) return false;
                if (_port <= 0 || _port > 65535) return false;
                return true;
            }
        }

        public AsyncCommand SaveCommand   { get; private set; }
        public AsyncCommand TestCommand   { get; private set; }
        public ICommand     ClearCommand  { get; private set; }
        public ICommand     CancelCommand { get; private set; }

        public void OnNavigatedTo(object parameter)
        {
            if (!_isLoaded)
            {
                _isLoaded = true;
                LoadAsync();
            }

            // If we arrived here via tg://proxy?... activation the App
            // hands the original URI through as the navigation parameter.
            // Pre-fill the secret input so the user only has to review
            // and tap Save. We don't auto-Enable the proxy — the user
            // must still confirm via the toggle to avoid silent take-over.
            string url = parameter as string;
            if (!string.IsNullOrEmpty(url) &&
                (url.StartsWith("tg://proxy", StringComparison.OrdinalIgnoreCase)
                 || url.IndexOf("t.me/proxy", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                SecretInput = url;
                TestResult = "Proxy details pre-filled from link. Review and tap Save to enable.";
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // -----------------------------------------------------------------
        // Load + Save
        // -----------------------------------------------------------------

        private async void LoadAsync()
        {
            if (_settings == null) return;
            try
            {
                Result<ProxyConfig, SettingsError> r =
                    await _settings.GetProxyAsync(CancellationToken.None).ConfigureAwait(true);
                if (r.IsOk && r.Value != null)
                {
                    ApplyLoaded(r.Value);
                }
            }
            catch (Exception)
            {
                // Best-effort; the defaults applied in the ctor remain.
            }
        }

        private void ApplyLoaded(ProxyConfig cfg)
        {
            Enabled = cfg.Enabled;
            Host = cfg.Host ?? string.Empty;
            PortText = cfg.Port > 0 ? cfg.Port.ToString() : string.Empty;
            Label = cfg.Label ?? string.Empty;

            // Reconstruct an editable secret representation so the user can
            // see what they had configured. Re-encode as hex for the input
            // field; the parser accepts hex round-trip cleanly.
            if (cfg.Secret != null && cfg.Secret.Length == 16)
            {
                _parsedSecret = cfg.Secret;
                _parsedMode = cfg.Mode;
                _parsedFakeTlsDomain = cfg.FakeTlsDomain ?? string.Empty;

                // Re-encode into a shareable hex form including the mode
                // prefix byte so a round-trip Save -> Load preserves the
                // exact wire descriptor.
                string display = EncodeSecretForDisplay(cfg.Mode, cfg.Secret, cfg.FakeTlsDomain);
                SecretInput = display;  // triggers ReparseSecret() — idempotent
            }
        }

        private async Task SaveAsync()
        {
            if (_settings == null)
            {
                TestResult = "Settings service unavailable.";
                return;
            }

            ProxyConfig config;
            try
            {
                if (_enabled)
                {
                    config = new ProxyConfig(
                        enabled: true,
                        host: _host,
                        port: _port,
                        secret: _parsedSecret,
                        mode: _parsedMode,
                        fakeTlsDomain: _parsedFakeTlsDomain,
                        label: _label ?? string.Empty);
                }
                else
                {
                    // Persist the disabled descriptor — keeps host/port
                    // available for re-enable, but clears the runtime.
                    if (_parsedSecret != null && _parsedSecret.Length == 16
                        && !string.IsNullOrEmpty(_host) && _port > 0 && _port <= 65535)
                    {
                        config = new ProxyConfig(
                            enabled: false,
                            host: _host,
                            port: _port,
                            secret: _parsedSecret,
                            mode: _parsedMode,
                            fakeTlsDomain: _parsedFakeTlsDomain,
                            label: _label ?? string.Empty);
                    }
                    else
                    {
                        config = ProxyConfig.Disabled;
                    }
                }
            }
            catch (Exception ex)
            {
                TestResult = "Invalid configuration: " + ex.Message;
                return;
            }

            try
            {
                Result<Unit, SettingsError> r =
                    await _settings.SetProxyAsync(config, CancellationToken.None).ConfigureAwait(true);
                if (r.IsFail)
                {
                    TestResult = "Save failed: " + (r.Error != null ? r.Error.ToString() : "unknown");
                    return;
                }
                TestResult = _enabled ? "Saved. Proxy enabled." : "Saved. Direct connection.";
                if (_nav != null && _nav.CanGoBack) _nav.GoBack();
            }
            catch (Exception ex)
            {
                TestResult = "Save failed: " + ex.GetType().Name;
            }
        }

        private void ClearProxy()
        {
            Enabled = false;
            Host = string.Empty;
            PortText = "443";
            SecretInput = string.Empty;
            Label = string.Empty;
            _parsedSecret = null;
            _parsedMode = ProxySecretMode.Legacy;
            _parsedFakeTlsDomain = string.Empty;
            ParsedSecretSummary = string.Empty;
            TestResult = string.Empty;
        }

        private void GoBack()
        {
            if (_nav != null && _nav.CanGoBack) _nav.GoBack();
        }

        // -----------------------------------------------------------------
        // Test connection — live MTProxy handshake probe via ISettingsApi.
        //
        // The settings layer's MtProxyProbe opens a fresh socket to
        // host:port, writes the 64-byte obfuscated init packet built
        // from the user-typed secret, and waits for any response
        // bytes within a 5-second window. The probe does NOT touch
        // the active MTProto channel — a connected user can test a
        // new proxy without dropping their session.
        //
        // Reachable means the proxy is alive and accepting traffic.
        // Wrong-secret detection still requires Telegram's DH
        // handshake, which only happens on Save (next channel open).
        // -----------------------------------------------------------------

        private async Task TestAsync()
        {
            if (_settings == null)
            {
                TestResult = "Settings service unavailable.";
                return;
            }
            if (string.IsNullOrEmpty(_host) || _port <= 0 || _port > 65535)
            {
                TestResult = "Enter a valid host and port.";
                return;
            }
            if (_parsedSecret == null || _parsedSecret.Length != 16)
            {
                TestResult = "Enter a valid secret first.";
                return;
            }

            ProxyConfig probeConfig;
            try
            {
                probeConfig = new ProxyConfig(
                    enabled: true,
                    host: _host,
                    port: _port,
                    secret: _parsedSecret,
                    mode: _parsedMode,
                    fakeTlsDomain: _parsedFakeTlsDomain ?? string.Empty,
                    label: _label ?? string.Empty);
            }
            catch (Exception ex)
            {
                TestResult = "Invalid configuration: " + ex.Message;
                return;
            }

            IsTesting = true;
            TestResult = "Probing MTProxy handshake...";
            try
            {
                ProxyProbeResult r = await _settings
                    .TestProxyAsync(probeConfig, CancellationToken.None)
                    .ConfigureAwait(true);
                TestResult = FormatProbeResult(r);
            }
            catch (Exception ex)
            {
                TestResult = "Probe failed: " + ex.GetType().Name;
            }
            finally
            {
                IsTesting = false;
            }
        }

        private static string FormatProbeResult(ProxyProbeResult r)
        {
            if (r == null) return "Probe failed.";
            string elapsed = "(" + r.ElapsedMs + " ms)";
            switch (r.Status)
            {
                case ProxyProbeStatus.Reachable:
                    return "Reachable. " + r.Detail + " " + elapsed +
                        " — save to verify the secret via Telegram DH.";
                case ProxyProbeStatus.Timeout:
                    return "No response within 5 s " + elapsed +
                        ". Check host/port and try again.";
                case ProxyProbeStatus.Rejected:
                    return "Proxy closed without responding " + elapsed +
                        ". The secret or proxy type may be wrong.";
                case ProxyProbeStatus.NetworkError:
                    return "Network error: " + r.Detail + " " + elapsed + ".";
                case ProxyProbeStatus.Misconfigured:
                    return "Configuration invalid: " + r.Detail + ".";
                default:
                    return r.Detail + " " + elapsed;
            }
        }

        // -----------------------------------------------------------------
        // Secret parsing
        // -----------------------------------------------------------------

        private void ReparseSecret()
        {
            if (string.IsNullOrEmpty(_secretInput))
            {
                _parsedSecret = null;
                _parsedMode = ProxySecretMode.Legacy;
                _parsedFakeTlsDomain = string.Empty;
                ParsedSecretSummary = string.Empty;
                return;
            }

            string trimmed = _secretInput.Trim();

            // Try URL form first — it carries host+port too.
            ProxyConfig urlForm;
            if (ProxyConfigParser.TryParse(trimmed, /* enabled */ false, out urlForm))
            {
                // Auto-fill host / port from the URL — the user can still
                // override after pasting. Use the property setters so the
                // CanSave / CanTest pipeline kicks in.
                if (string.IsNullOrEmpty(_host) || _host == urlForm.Host)
                {
                    Host = urlForm.Host;
                }
                if (_port == 0 || _port == urlForm.Port)
                {
                    PortText = urlForm.Port.ToString();
                }
                _parsedSecret = urlForm.Secret;
                _parsedMode = urlForm.Mode;
                _parsedFakeTlsDomain = urlForm.FakeTlsDomain ?? string.Empty;
                ParsedSecretSummary = DescribeSecret(_parsedMode, _parsedFakeTlsDomain);
                return;
            }

            // Try the bare secret form (hex or url-safe base64).
            byte[] payload;
            ProxySecretMode mode;
            string fakeSni;
            if (ProxyConfigParser.TryParseSecret(trimmed, out payload, out mode, out fakeSni))
            {
                _parsedSecret = payload;
                _parsedMode = mode;
                _parsedFakeTlsDomain = fakeSni ?? string.Empty;
                ParsedSecretSummary = DescribeSecret(_parsedMode, _parsedFakeTlsDomain);
                return;
            }

            // Malformed.
            _parsedSecret = null;
            _parsedMode = ProxySecretMode.Legacy;
            _parsedFakeTlsDomain = string.Empty;
            ParsedSecretSummary = "Unrecognized secret format.";
        }

        private static string DescribeSecret(ProxySecretMode mode, string sni)
        {
            switch (mode)
            {
                case ProxySecretMode.Legacy:
                    return "Legacy (16-byte raw)";
                case ProxySecretMode.Secure:
                    return "Secure (random-padding intermediate)";
                case ProxySecretMode.FakeTls:
                    return "Fake-TLS via " + (sni ?? "(missing SNI)");
                default:
                    return string.Empty;
            }
        }

        private static string EncodeSecretForDisplay(ProxySecretMode mode, byte[] secret, string sni)
        {
            // Re-encode the saved descriptor as hex with the mode prefix
            // byte so the round-trip through SecretInput is byte-stable.
            //   Legacy   → 32 hex chars
            //   Secure   → "dd" + 32 hex chars
            //   FakeTls  → "ee" + 32 hex chars + ASCII SNI bytes hex
            if (secret == null || secret.Length != 16) return string.Empty;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
            if (mode == ProxySecretMode.Secure) sb.Append("dd");
            else if (mode == ProxySecretMode.FakeTls) sb.Append("ee");

            for (int i = 0; i < secret.Length; i++)
            {
                sb.Append(NibbleHex((secret[i] >> 4) & 0x0F));
                sb.Append(NibbleHex(secret[i]        & 0x0F));
            }
            if (mode == ProxySecretMode.FakeTls && !string.IsNullOrEmpty(sni))
            {
                for (int i = 0; i < sni.Length; i++)
                {
                    byte b = (byte)sni[i];
                    sb.Append(NibbleHex((b >> 4) & 0x0F));
                    sb.Append(NibbleHex(b        & 0x0F));
                }
            }
            return sb.ToString();
        }

        private static char NibbleHex(int n)
        {
            return n < 10 ? (char)('0' + n) : (char)('a' + (n - 10));
        }
    }
}
