// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// QrLoginPageViewModel.cs — auth page VM (QR login).
//
// Drives QrLoginPage. Owns the full token-refresh + poll loop. Per
// Telegram's QR-login protocol the unauthenticated client polls for
// status by RE-ISSUING auth.exportLoginToken every few seconds — never
// auth.importLoginToken (that's for the already-authorized device, or
// for the unauthenticated client to switch DCs after MigrateTo). Both
// IAccountApi.RequestQrTokenAsync and IAccountApi.PollQrLoginAsync
// resolve to the same wire call here, and ApplyPollResult dispatches on
// the QrLoginStatus they return.
//
//   1) RequestQrTokenAsync issues the initial token; the VM exposes its
//      tg://login URI as QrText for QrCodeCanvas to render.
//   2) A DispatcherTimer fires every second to drive both the visible
//      countdown and the polling cadence (one wire poll per
//      <PollIntervalSeconds>). Polling and refresh are serialized via
//      _isLoadingQr / _pendingRefresh flags so a slow RPC never lets
//      two requests race.
//   3) When the token is <= RefreshLeadSeconds from server-side expiry
//      the VM proactively re-issues exportLoginToken, replacing the
//      visible QR with a fresh one before the old code would have
//      stopped working.
//   4) Status transitions (handled in ApplyPollResult):
//        - Pending          → countdown ticking, "Waiting for scan… 23s".
//        - Expired          → schedule immediate refresh.
//        - TwoFaRequired    → navigate to TwoFaPasswordPage with PasswordHint;
//                             stop the loop (the 2FA page completes auth).
//        - SignUpRequired   → stop polling and guide the user to phone sign-up.
//        - Accepted         → fire SignInSucceeded so the page can boot the
//                             chat-list main flow; stop the loop.
//   5) After RetryStreakLimit consecutive RPC failures the VM surfaces a
//      retry button and pauses the timer; tapping retry drains the streak
//      and starts a fresh refresh.
//
// All wire-side persistence (auth_key, homeDcId, userId) happens inside
// PollQrLoginHandler — the VM is purely UI: no auth_key knowledge, no DC
// awareness. That mirrors the phone-flow split (SmsCodePage VM doesn't
// touch auth_key either).

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using Windows.UI.Xaml;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class QrLoginPageViewModel : ObservableObject
    {
        // Cadence chosen empirically. 4s is the cadence Telegram desktop
        // settled on after their early "poll on every keystroke" design;
        // ~7-8 polls per token cycle is enough to react quickly to a scan
        // without hammering the server.
        private const int PollIntervalSeconds = 4;

        // Ask for a fresh token a few seconds before the current one
        // expires so the user never sees a stale QR. Telegram tokens last
        // ~30 seconds; 5s of lead gives a generous margin even on slow
        // networks.
        private const int RefreshLeadSeconds = 5;

        // After this many consecutive RPC failures we stop the loop and
        // show a retry button. The user is much better served by a clear
        // "we lost the network" surface than by silent infinite retries
        // on a loaded battery.
        private const int RetryStreakLimit = 3;

        private readonly IAccountApi _account;
        private readonly INavigationService _nav;
        private readonly DispatcherTimer _timer;

        private string _qrText;
        private string _statusText;
        private bool _isBusy;
        private bool _hasError;
        private bool _showRetry;
        private bool _isLoadingQr;
        private bool _pendingRefresh;
        private int _consecutiveFailures;
        private int _navigateGuard;     // 0 = active, 1 = pivoted away (Accepted/2FA)
        private int _prewarmStarted;    // 0 = not yet, 1 = scheduled/running

        private QrLoginToken _currentToken;
        private CancellationTokenSource _cts;
        private CancellationTokenSource _prewarmCts;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public QrLoginPageViewModel()
            : this(null, null)
        {
        }

        public QrLoginPageViewModel(IAccountApi account, INavigationService nav)
        {
            _account = account;
            _nav = nav;
            _statusText = Strings.Get("QrLoginStatusConnecting");

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += OnTimerTick;

            RefreshCommand = new AsyncCommand(RefreshAsync, _ => CanRefresh);
            RetryCommand = new AsyncCommand(_ => OnRetryAsync(), _ => _showRetry);
            UsePhoneCommand = new RelayCommand(OnUsePhone);
        }

        /// <summary>Raised after a successful QR login (Accepted) so the
        /// page can run App.OnUserLoggedIn() and navigate to ChatListPage.</summary>
        public event EventHandler SignInSucceeded;

        public string QrText
        {
            get { return _qrText; }
            private set { SetProperty(ref _qrText, value); }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public bool HasError
        {
            get { return _hasError; }
            private set { SetProperty(ref _hasError, value); }
        }

        public bool ShowRetry
        {
            get { return _showRetry; }
            private set
            {
                if (SetProperty(ref _showRetry, value))
                {
                    var rc = RetryCommand as AsyncCommand;
                    if (rc != null) rc.RaiseCanExecuteChanged();
                }
            }
        }

        public bool CanRefresh
        {
            get { return !_isLoadingQr; }
        }

        public AsyncCommand RefreshCommand { get; private set; }
        public ICommand RetryCommand { get; private set; }
        public ICommand UsePhoneCommand { get; private set; }

        public void OnNavigatedTo(object parameter)
        {
            EarlyLog.Write("App.QrLogin", "OnNavigatedTo");
            ResetForFreshSession();
            // First fetch is fire-and-forget — RefreshAsync owns the timer
            // start.
            var ignored = RefreshAsync(null);
        }

        private async void StartPrewarmAfterFirstRender()
        {
            if (_account == null) return;
            if (Interlocked.Exchange(ref _prewarmStarted, 1) != 0) return;

            try
            {
                await Task.Delay(750).ConfigureAwait(true);
            }
            catch
            {
                return;
            }

            if (Volatile.Read(ref _navigateGuard) != 0) return;
            FireAndForgetPrewarm();
        }

        private async void FireAndForgetPrewarm()
        {
            CancellationTokenSource cts;
            try
            {
                // Cancel — never Dispose — the previous prewarm CTS.
                // See StopAndDisposeTokenSource for the rationale.
                if (_prewarmCts != null) { try { _prewarmCts.Cancel(); } catch { } }
                cts = new CancellationTokenSource();
                _prewarmCts = cts;
            }
            catch
            {
                return;
            }

            try
            {
                await _account.PrewarmQrLoginDcsAsync(cts.Token).ConfigureAwait(true);
                EarlyLog.Write("App.QrLogin", "Prewarm completed");
            }
            catch (OperationCanceledException)
            {
                // Page navigated away — fine.
            }
            catch (Exception ex)
            {
                AppLog.For("App.QrLogin").Warn("Prewarm threw: " + ex);
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
            EarlyLog.Write("App.QrLogin", "OnNavigatedFrom — stopping timer");
            StopAndDisposeTokenSource();
            CancelPrewarm();
            _timer.Stop();
        }

        private void CancelPrewarm()
        {
            // Cancel only — see StopAndDisposeTokenSource for the
            // disposal-safety reasoning. The prewarm task may still be
            // mid-handshake when we navigate away, and calling Dispose
            // on its CTS would surface as ObjectDisposedException
            // inside the keygen.
            var cts = _prewarmCts;
            _prewarmCts = null;
            if (cts == null) return;
            try { cts.Cancel(); } catch { }
        }

        // -------- Refresh (auth.exportLoginToken) --------------------------

        private async Task RefreshAsync(object parameter)
        {
            if (Volatile.Read(ref _navigateGuard) != 0) return;

            // Serialize: if a refresh is already in flight, queue a single
            // pending refresh so we re-run once it completes. The
            // _isLoadingQr / _pendingRefresh pair prevents the manual
            // "refresh" tap + the auto-expiry refresh from racing.
            if (_isLoadingQr)
            {
                _pendingRefresh = true;
                EarlyLog.Write("App.QrLogin", "Refresh queued (already loading)");
                return;
            }

            _isLoadingQr = true;
            _timer.Stop();
            IsBusy = true;
            HasError = false;
            ShowRetry = false;
            StatusText = Strings.Get("QrLoginStatusGenerating");
            RaiseCommandStates();

            try
            {
                if (_account == null)
                {
                    StatusText = Strings.Get("QrLoginStatusAccountUnavailable");
                    HasError = true;
                    return;
                }

                Result<QrLoginPoll, AccountError> result;
                CancellationToken ct = EnsureFreshCt();
                try
                {
                    result = await _account.RequestQrTokenAsync(ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    EarlyLog.Write("App.QrLogin", "Refresh cancelled");
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.QrLogin").Error("RefreshAsync threw: " + ex);
                    StatusText = string.Format(
                        CultureInfo.InvariantCulture,
                        Strings.Get("QrLoginStatusUnexpected"),
                        ex.GetType().Name);
                    HasError = true;
                    BumpFailureStreak();
                    return;
                }

                if (result.IsFail)
                {
                    StatusText = FormatError(result.Error);
                    HasError = true;
                    EarlyLog.Write(
                        "App.QrLogin",
                        "RequestQrToken FAILED kind=" + result.Error.Kind +
                        " msg=" + (result.Error.Message ?? string.Empty));

                    // DcMigrationRequired: the underlying transport already
                    // tried to follow the migration; if it surfaced here the
                    // first attempt failed. Treat as a generic failure and
                    // let the streak counter take it from there.
                    BumpFailureStreak();
                    return;
                }

                // Refresh is allowed to surface ANY status: the act of
                // re-issuing exportLoginToken can itself catch the moment
                // the user authorises on another device (the server flips
                // from auth.loginToken to auth.loginTokenSuccess, and the
                // handler finalizes the auth in-place). Route through the
                // shared dispatcher used by the periodic poll path so a
                // refresh that surfaces Accepted / TwoFa / SignUp is
                // handled the same way.
                ApplyPollResult(result.Value);
            }
            finally
            {
                IsBusy = false;
                _isLoadingQr = false;
                RaiseCommandStates();

                // If the page is still alive and we have a token, restart
                // the per-second tick that drives polling and the visible
                // countdown.
                if (Volatile.Read(ref _navigateGuard) == 0 && _currentToken != null)
                {
                    _timer.Start();
                }

                if (_pendingRefresh)
                {
                    _pendingRefresh = false;
                    EarlyLog.Write("App.QrLogin", "Replaying queued refresh");
                    var ignored = RefreshAsync(null);
                }
            }
        }

        // -------- Timer tick: drives countdown + polling cadence -----------

        private DateTimeOffset _lastPollUtc = DateTimeOffset.MinValue;

        private void OnTimerTick(object sender, object e)
        {
            if (Volatile.Read(ref _navigateGuard) != 0)
            {
                _timer.Stop();
                return;
            }

            if (_currentToken == null)
            {
                return;
            }

            int secondsLeft = GetSecondsUntilExpiry();

            if (secondsLeft <= RefreshLeadSeconds)
            {
                // Pre-emptive refresh — token is about to expire on the
                // server. Stop ticking and fire a refresh; the refresh
                // restarts the timer once it has a fresh token.
                EarlyLog.Write(
                    "App.QrLogin",
                    "Pre-empt refresh: secondsLeft=" + secondsLeft);
                _timer.Stop();
                var ignored = RefreshAsync(null);
                return;
            }

            // Visible countdown.
            UpdateWaitingStatus();

            // Throttle polling to the configured cadence.
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if ((now - _lastPollUtc).TotalSeconds < PollIntervalSeconds)
            {
                return;
            }

            _lastPollUtc = now;
            var pollIgnored = PollOnceAsync();
        }

        // -------- Poll (auth.exportLoginToken) -----------------------------

        // Threshold above which a poll call is "definitely doing more
        // than just an exportLoginToken round-trip" — typically a
        // post-MigrateTo importLoginToken with a fresh DH handshake on
        // the new DC. Updates StatusText to "Linking your device..." so
        // the user sees forward progress instead of a frozen UI.
        private const int LinkingStatusThresholdMs = 2000;

        private async Task PollOnceAsync()
        {
            if (Volatile.Read(ref _navigateGuard) != 0) return;
            if (_currentToken == null) return;

            QrLoginToken snapshot = _currentToken;
            CancellationToken ct = _cts != null ? _cts.Token : CancellationToken.None;

            // Race the poll against a 2 s timer; if the poll hasn't
            // returned by then we're almost certainly mid-migrate
            // handshake and want to show the user a "linking" hint.
            Task<Result<QrLoginPoll, AccountError>> pollTask = _account.PollQrLoginAsync(snapshot, ct);
            Task watchdog = Task.Delay(LinkingStatusThresholdMs, ct);
            Task firstDone = await Task.WhenAny(pollTask, watchdog).ConfigureAwait(true);
            if (!object.ReferenceEquals(firstDone, pollTask))
            {
                EarlyLog.Write("App.QrLogin", "Poll exceeded " + LinkingStatusThresholdMs + "ms — showing linking status");
                StatusText = Strings.Get("QrLoginStatusLinking");
            }

            Result<QrLoginPoll, AccountError> result;
            try
            {
                result = await pollTask.ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLog.For("App.QrLogin").Error("PollOnceAsync threw: " + ex);
                BumpFailureStreak();
                return;
            }

            if (result.IsFail)
            {
                EarlyLog.Write(
                    "App.QrLogin",
                    "Poll FAIL kind=" + result.Error.Kind +
                    " msg=" + (result.Error.Message ?? string.Empty));

                // SessionExpired here is a different signal — the server
                // rejected the token; refresh it.
                if (result.Error.Kind == AccountErrorKind.SessionExpired)
                {
                    StatusText = Strings.Get("QrLoginStatusExpired");
                    _currentToken = null;
                    QrText = null;
                    _timer.Stop();
                    var ignored = RefreshAsync(null);
                    return;
                }

                BumpFailureStreak();
                return;
            }

            ApplyPollResult(result.Value);
        }

        /// <summary>
        /// Shared result dispatcher for both refresh and poll paths. The
        /// server can return any status from either entry point — both
        /// resolve to the same wire call (auth.exportLoginToken) — so we
        /// route through one switch instead of duplicating it. Caller is
        /// responsible for the post-call timer / streak housekeeping.
        /// </summary>
        private void ApplyPollResult(QrLoginPoll poll)
        {
            if (poll == null) return;

            EarlyLog.Write("App.QrLogin", "ApplyPollResult kind=" + poll.Status);
            _consecutiveFailures = 0;
            ShowRetry = false;
            HasError = false;

            switch (poll.Status)
            {
                case QrLoginStatus.Pending:
                    if (poll.Token != null)
                    {
                        // The wire returns a fresh token on every
                        // exportLoginToken call, but per Telegram's
                        // protocol all outstanding tokens issued during
                        // the same login session stay valid until their
                        // own expiry — the user's earlier scan of the
                        // visible QR is still recognised on a later
                        // poll. So we keep the rendered QR stable
                        // across polls and only swap to the freshly
                        // issued token when the rendered one is within
                        // RefreshLeadSeconds of expiry. Without this
                        // the QR visibly flickers every PollIntervalSeconds
                        // (~4s) while the user is mid-scan.
                        bool needsRender = _currentToken == null ||
                            (_currentToken.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds <= RefreshLeadSeconds;
                        if (needsRender)
                        {
                            _currentToken = poll.Token;
                            QrText = poll.Token.TgUri.ToString();
                            EarlyLog.Write(
                                "App.QrLogin",
                                "Token rendered expiresAt=" + poll.Token.ExpiresAt.ToString("o") +
                                " secs_until_expiry=" + GetSecondsUntilExpiry());

                            // Prioritize first paint. Starting the speculative
                            // DC#1 DH prewarm before the QR is visible makes
                            // fresh installs spend two expensive handshakes in
                            // parallel, delaying the code the user needs to
                            // scan. Once the token is rendered, prewarm can
                            // run in the background and still help the later
                            // MigrateTo/importLoginToken path.
                            StartPrewarmAfterFirstRender();
                        }
                        // Else: silently discard the new token. The
                        // currently rendered one is still valid and
                        // the user may already be scanning it.
                    }
                    UpdateWaitingStatus();
                    return;

                case QrLoginStatus.Expired:
                    EarlyLog.Write("App.QrLogin", "Token expired — refreshing");
                    StatusText = Strings.Get("QrLoginStatusExpired");
                    _currentToken = null;
                    QrText = null;
                    _timer.Stop();
                    var refreshIgnored = RefreshAsync(null);
                    return;

                case QrLoginStatus.TwoFaRequired:
                    EarlyLog.Write(
                        "App.QrLogin",
                        "2FA required hint='" +
                        (poll.PasswordHint ?? string.Empty) + "'");
                    PivotAwayFromQr();
                    StatusText = Strings.Get("QrLoginStatusTwoFa");
                    if (_nav != null)
                    {
                        _nav.NavigateTo(Route.TwoFaPassword, poll.PasswordHint);
                    }
                    return;

                case QrLoginStatus.SignUpRequired:
                    EarlyLog.Write("App.QrLogin", "Sign-up required; QR login only authorizes existing accounts");
                    PivotAwayFromQr();
                    QrText = null;
                    HasError = true;
                    ShowRetry = false;
                    StatusText = Strings.Get("QrLoginStatusSignUpRequired");
                    return;

                case QrLoginStatus.Accepted:
                    EarlyLog.Write(
                        "App.QrLogin",
                        "Accepted — userId=" + poll.UserId + "; firing SignInSucceeded");
                    PivotAwayFromQr();
                    StatusText = Strings.Get("QrLoginStatusAccepted");
                    var h = SignInSucceeded;
                    if (h != null) h(this, EventArgs.Empty);
                    return;
            }
        }

        // -------- UI state helpers -----------------------------------------

        private void UpdateWaitingStatus()
        {
            int secs = GetSecondsUntilExpiry();
            if (secs <= 0)
            {
                StatusText = Strings.Get("QrLoginStatusRefreshing");
                return;
            }
            StatusText = string.Format(
                CultureInfo.CurrentCulture,
                Strings.Get("QrLoginStatusWaiting"),
                secs);
        }

        private int GetSecondsUntilExpiry()
        {
            if (_currentToken == null) return 0;
            double secs = (_currentToken.ExpiresAt - DateTimeOffset.UtcNow).TotalSeconds;
            if (secs <= 0) return 0;
            return (int)Math.Ceiling(secs);
        }

        private void BumpFailureStreak()
        {
            _consecutiveFailures++;
            EarlyLog.Write(
                "App.QrLogin",
                "Failure streak=" + _consecutiveFailures + "/" + RetryStreakLimit);
            if (_consecutiveFailures >= RetryStreakLimit)
            {
                _timer.Stop();
                ShowRetry = true;
                HasError = true;
                StatusText = Strings.Get("QrLoginStatusRetry");
                EarlyLog.Write("App.QrLogin", "Retry-streak limit reached, pausing loop");
            }
        }

        private async Task OnRetryAsync()
        {
            EarlyLog.Write("App.QrLogin", "Retry tapped — resetting and refetching");
            _consecutiveFailures = 0;
            ShowRetry = false;
            HasError = false;
            await RefreshAsync(null).ConfigureAwait(true);
        }

        private void OnUsePhone(object parameter)
        {
            EarlyLog.Write("App.QrLogin", "UsePhone tapped — full state cleanup");
            ResetForFreshSession();
            if (_nav == null) return;
            if (_nav.CanGoBack) _nav.GoBack();
            else _nav.NavigateTo(Route.PhoneNumber);
        }

        private void ResetForFreshSession()
        {
            // P2: full state cleanup. Stop the timer, drop the cached
            // token, clear surfaced status / error / retry flags, and
            // re-open the navigate guard so OnNavigatedTo can drive a new
            // session if the page is reused.
            _timer.Stop();
            StopAndDisposeTokenSource();
            _currentToken = null;
            QrText = null;
            _consecutiveFailures = 0;
            _isLoadingQr = false;
            _pendingRefresh = false;
            _lastPollUtc = DateTimeOffset.MinValue;
            Volatile.Write(ref _prewarmStarted, 0);
            HasError = false;
            ShowRetry = false;
            Volatile.Write(ref _navigateGuard, 0);
        }

        private void PivotAwayFromQr()
        {
            // Set the guard FIRST so any inflight tick / poll bails out.
            Volatile.Write(ref _navigateGuard, 1);
            _timer.Stop();
            StopAndDisposeTokenSource();
        }

        private CancellationToken EnsureFreshCt()
        {
            StopAndDisposeTokenSource();
            _cts = new CancellationTokenSource();
            return _cts.Token;
        }

        private void StopAndDisposeTokenSource()
        {
            // Cancel only — do NOT dispose. Pending tasks (e.g. an
            // in-flight rpc whose Task.Delay(ct) is racing the wire
            // response) still hold this token; calling Dispose() while
            // they're running causes ObjectDisposedException to surface
            // through the rpc retry loop and cascades into a fatal
            // native fault on Windows Phone. Cancellation is enough to
            // unwind in-flight work — the GC collects the CTS once the
            // last reference drops.
            var cts = _cts;
            _cts = null;
            if (cts == null) return;
            try { cts.Cancel(); }
            catch { }
        }

        private void RaiseCommandStates()
        {
            OnPropertyChanged("CanRefresh");
            if (RefreshCommand != null) RefreshCommand.RaiseCanExecuteChanged();
            var rc = RetryCommand as AsyncCommand;
            if (rc != null) rc.RaiseCanExecuteChanged();
        }

        private static string FormatError(AccountError error)
        {
            if (error == null) return Strings.Get("QrLoginErrorUnknown");
            switch (error.Kind)
            {
                case AccountErrorKind.NetworkError:
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        Strings.Get("QrLoginErrorNetwork"),
                        string.IsNullOrEmpty(error.Message)
                            ? Strings.Get("QrLoginErrorNetworkNoConnection")
                            : error.Message);
                case AccountErrorKind.PhoneNumberFlood:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return string.Format(
                        CultureInfo.InvariantCulture,
                        Strings.Get("QrLoginErrorFlood"),
                        retry);
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }
}
