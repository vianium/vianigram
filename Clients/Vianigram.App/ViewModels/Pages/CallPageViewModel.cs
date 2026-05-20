// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CallPageViewModel.cs
//
// Drives CallPage. OnNavigatedTo projects the supplied CallId onto the
// active session via ICallsApi.GetSession; subscribes to StateChanged so
// IsConnected / StatusText track the kernel-bus aggregate. HangUp routes
// through ICallsApi.DiscardAsync and then GoBack via INavigationService.

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.App.ViewModels;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.Entities;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Inbound;
using Vianigram.Kernel.Result;
using Windows.UI.Xaml;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class CallPageViewModel : BaseViewModel
    {
        private readonly ICallsApi _calls;
        private readonly INavigationService _nav;
        private readonly DispatcherTimer _durationTimer;

        private CallId _callId;
        private string _peerName;
        private string _avatarLetter;
        private string _statusText;
        private string _durationText;
        private string _errorMessage;
        private string _protocolWarning;
        private bool _isMuted;
        private bool _isSpeakerOn;
        private bool _isConnected;
        private bool _isVideo;
        private int _durationSeconds;

        // Design-time / degraded-mode ctor.
        public CallPageViewModel() : this(null, null)
        {
        }

        public CallPageViewModel(ICallsApi calls, INavigationService nav)
        {
            _calls = calls;
            _nav = nav;

            _peerName = string.Empty;
            _avatarLetter = "?";
            _statusText = "Calling...";
            _durationText = "00:00";

            _durationTimer = new DispatcherTimer();
            _durationTimer.Interval = TimeSpan.FromSeconds(1);
            _durationTimer.Tick += OnDurationTick;

            ToggleMuteCommand = new AsyncCommand(_ => ToggleMuteAsync(), _ => true);
            ToggleSpeakerCommand = new AsyncCommand(_ => ToggleSpeakerAsync(), _ => true);
            HangUpCommand = new AsyncCommand(_ => HangUpAsync(), _ => true);
            FlipCameraCommand = new AsyncCommand(_ => FlipCameraAsync(), _ => _isVideo);
        }

        // ---- Bindable surface ---------------------------------------

        public string PeerName
        {
            get { return _peerName; }
            set
            {
                if (SetProperty(ref _peerName, value))
                    OnPropertyChanged("AvatarColorSeed");
            }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
        }

        public string DurationText
        {
            get { return _durationText; }
            private set { SetProperty(ref _durationText, value); }
        }

        public bool IsMuted
        {
            get { return _isMuted; }
            set
            {
                if (SetProperty(ref _isMuted, value))
                {
                    OnPropertyChanged("MuteButtonLabel");
                    OnPropertyChanged("IsNotMuted");
                }
            }
        }

        // Inverse of IsMuted, exposed because WP8.1 XAML's BoolToVisibility
        // converter doesn't have a NegateBool variant in this project. The
        // CallPage uses two stacked TextBlocks (mic-on vs mic-muted) and
        // toggles their Visibility — IsMuted shows the muted glyph,
        // IsNotMuted shows the unmuted one. Same pattern applies to the
        // Speaker button below.
        public bool IsNotMuted
        {
            get { return !_isMuted; }
        }

        public string MuteButtonLabel
        {
            get { return _isMuted ? "Unmute" : "Mute"; }
        }

        public bool IsSpeakerOn
        {
            get { return _isSpeakerOn; }
            set
            {
                if (SetProperty(ref _isSpeakerOn, value))
                {
                    OnPropertyChanged("SpeakerButtonLabel");
                    OnPropertyChanged("IsSpeakerOff");
                }
            }
        }

        public bool IsSpeakerOff
        {
            get { return !_isSpeakerOn; }
        }

        public string SpeakerButtonLabel
        {
            get { return _isSpeakerOn ? "Speaker on" : "Speaker"; }
        }

        public bool IsConnected
        {
            get { return _isConnected; }
            set
            {
                if (!SetProperty(ref _isConnected, value)) return;
                if (value)
                {
                    StatusText = string.Empty;
                    _durationSeconds = 0;
                    DurationText = FormatDuration(0);
                    _durationTimer.Start();
                }
                else
                {
                    _durationTimer.Stop();
                }
                OnPropertyChanged("ShowDuration");
                OnPropertyChanged("ShowStatus");
            }
        }

        public bool IsVideo
        {
            get { return _isVideo; }
            set
            {
                if (SetProperty(ref _isVideo, value))
                {
                    var ac = FlipCameraCommand as AsyncCommand;
                    if (ac != null) ac.RaiseCanExecuteChanged();
                    OnPropertyChanged("CallTypeHeader");
                    OnPropertyChanged("ShowFlipCamera");
                }
            }
        }

        /// <summary>
        /// Header strap shown above the avatar — e.g. "VIANIGRAM AUDIO CALL".
        /// Tracking-spaced uppercase wordmark style consistent with the rest
        /// of the app (PhoneNumberPage, ExpiredPage, ChatListPage).
        /// </summary>
        public string CallTypeHeader
        {
            get { return _isVideo ? "VIANIGRAM VIDEO CALL" : "VIANIGRAM AUDIO CALL"; }
        }

        /// <summary>
        /// Avatar tint seed — same algorithm DialogRow uses on the chat
        /// list, so the call screen's avatar matches the colour the user
        /// sees in their dialogs (MS=green, DC=orange, …).
        /// </summary>
        public long AvatarColorSeed
        {
            get
            {
                string source = !string.IsNullOrEmpty(_peerName) ? _peerName : (_avatarLetter ?? string.Empty);
                long seed = 17;
                for (int i = 0; i < source.Length; i++) seed = (seed * 31) + source[i];
                return seed;
            }
        }

        /// <summary>True only for video calls — controls the Flip-camera
        /// button's visibility (audio calls hide it, matching the
        /// reference design where only mute / end / speaker show).</summary>
        public bool ShowFlipCamera { get { return _isVideo; } }

        public string ErrorMessage
        {
            get { return _errorMessage; }
            private set
            {
                if (SetProperty(ref _errorMessage, value))
                    OnPropertyChanged("HasError");
            }
        }

        public bool HasError
        {
            get { return !string.IsNullOrEmpty(_errorMessage); }
        }

        /// <summary>
        /// User-facing diagnostic banner shown after the call fails. When the
        /// underlying failure detail matches one of the tgcalls 2.x markers
        /// emitted by <c>voip_engine.cpp</c> (the peer is a modern Telegram
        /// client running tgcalls 2.x WebRTC, advertising classic protocol but
        /// never responding to classic INIT packets) we replace the generic
        /// "media plane failed" wording with a clear explanation. Otherwise we
        /// surface a passthrough "Call failed: ..." string.
        /// </summary>
        public string ProtocolWarning
        {
            get { return _protocolWarning; }
            private set
            {
                if (SetProperty(ref _protocolWarning, value))
                {
                    OnPropertyChanged("HasProtocolWarning");
                }
            }
        }

        public bool HasProtocolWarning
        {
            get { return !string.IsNullOrEmpty(_protocolWarning); }
        }

        /// <summary>Bound by CallPage to flip Visibility of the duration label.</summary>
        public bool ShowDuration { get { return _isConnected; } }

        /// <summary>Bound by CallPage to flip Visibility of the state label.</summary>
        public bool ShowStatus { get { return !_isConnected; } }

        // ---- Commands -------------------------------------------------

        public ICommand ToggleMuteCommand { get; private set; }
        public ICommand ToggleSpeakerCommand { get; private set; }
        public ICommand HangUpCommand { get; private set; }
        public ICommand FlipCameraCommand { get; private set; }

        // ---- Lifecycle -----------------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            ProtocolWarning = null;

            if (parameter is CallId)
            {
                _callId = (CallId)parameter;
            }
            else if (parameter is long)
            {
                _callId = new CallId((long)parameter);
            }

            if (_calls != null)
            {
                _calls.StateChanged += OnStateChanged;
                bool callsAvailable = _calls.IsCallingAvailable;

                if (!callsAvailable)
                {
                    IsConnected = false;
                    StatusText = "Call unavailable";
                    ErrorMessage = FormatCallingUnavailable(_calls.CallingUnavailableReason);
                }

                // Project the current aggregate snapshot, if known.
                CallSession session = null;
                try
                {
                    session = _calls.GetSession(_callId);
                }
                catch (Exception ex)
                {
                    AppLog.For("App.CallPage").Error("GetSession threw: " + ex);
                }

                if (session != null)
                {
                    IsVideo = session.IsVideo;
                    if (callsAvailable) ApplyState(session.State);
                }
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
            if (_calls != null)
            {
                _calls.StateChanged -= OnStateChanged;
            }
            _durationTimer.Stop();
        }

        public void SetState(string state)
        {
            // Helper for pages that want to drive the textual state without
            // touching IsConnected — used by the smoke / design surface.
            if (state == null) state = string.Empty;
            StatusText = state;
            OnPropertyChanged("ShowDuration");
            OnPropertyChanged("ShowStatus");
        }

        // ---- Subscription handlers ----------------------------------

        private void OnStateChanged(object sender, CallStateChangedEventArgs args)
        {
            if (args == null) return;
            // Filter to our session — multicast event covers all calls.
            if (args.CallId != _callId) return;

            CallSession session = null;
            if (_calls != null)
            {
                try { session = _calls.GetSession(_callId); }
                catch (Exception ex)
                {
                    AppLog.For("App.CallPage").Error("GetSession during state change threw: " + ex);
                }
            }

            var ignore = Dispatch.OnUiAsync(() => ApplyState(args.State, session));
        }

        private void ApplyState(CallSessionState state)
        {
            ApplyState(state, null);
        }

        private void ApplyState(CallSessionState state, CallSession session)
        {
            switch (state)
            {
                case CallSessionState.Active:
                    IsConnected = true;
                    StatusText = string.Empty;
                    break;
                case CallSessionState.Discarded:
                    IsConnected = false;
                    if (session != null
                        && session.DiscardReason == DiscardReason.Disconnect
                        && session.Duration.Seconds == 0)
                    {
                        StatusText = "Failed";
                        ErrorMessage = "Call failed: media could not connect.";
                        // The native voip_engine emits diagnostic detail strings
                        // describing why the classic VoIP control handshake never
                        // completed. If the peer is a modern Telegram client
                        // running tgcalls 2.x WebRTC the detail will contain one
                        // of our marker substrings — surface a clear protocol
                        // banner instead of the generic media-failure wording.
                        ProtocolWarning = ComputeProtocolWarning(_errorMessage);
                    }
                    else
                    {
                        StatusText = "Call ended";
                    }
                    break;
                case CallSessionState.Ringing:
                    IsConnected = false;
                    StatusText = "Ringing...";
                    break;
                case CallSessionState.Waiting:
                    IsConnected = false;
                    StatusText = "Calling...";
                    break;
                case CallSessionState.Confirming:
                    IsConnected = false;
                    StatusText = "Securing...";
                    break;
                case CallSessionState.MediaConnecting:
                    IsConnected = false;
                    StatusText = "Connecting...";
                    break;
                case CallSessionState.Pending:
                    IsConnected = false;
                    StatusText = "Connecting...";
                    break;
                case CallSessionState.Receiving:
                    IsConnected = false;
                    StatusText = "Incoming call...";
                    break;
                case CallSessionState.Requesting:
                default:
                    IsConnected = false;
                    StatusText = "Calling...";
                    break;
            }
        }

        // ---- Command handlers ---------------------------------------

        private async Task ToggleMuteAsync()
        {
            ErrorMessage = null;
            if (_calls == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            bool desired = !IsMuted;
            Result<Unit, CallError> result;
            try
            {
                result = await _calls.SetMutedAsync(_callId, desired, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.CallPage").Error("SetMutedAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatError(result.Error);
                return;
            }
            IsMuted = desired;
        }

        private async Task ToggleSpeakerAsync()
        {
            ErrorMessage = null;
            if (_calls == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            bool desired = !IsSpeakerOn;
            Result<Unit, CallError> result;
            try
            {
                result = await _calls.SetSpeakerAsync(_callId, desired, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.CallPage").Error("SetSpeakerAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatError(result.Error);
                return;
            }
            IsSpeakerOn = desired;
        }

        private async Task HangUpAsync()
        {
            ErrorMessage = null;
            _durationTimer.Stop();

            if (_calls == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            try
            {
                Result<Unit, CallError> result;
                try
                {
                    result = await _calls.DiscardAsync(_callId, DiscardReason.Hangup, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.CallPage").Error("DiscardAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    // Still navigate back — the local UX intent was "end".
                }
            }
            finally
            {
                if (_nav != null) _nav.GoBack();
            }
        }

        private async Task FlipCameraAsync()
        {
            ErrorMessage = null;
            if (_calls == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            Result<Unit, CallError> result;
            try
            {
                result = await _calls.FlipCameraAsync(_callId, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.CallPage").Error("FlipCameraAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatError(result.Error);
            }
        }

        // ---- Helpers --------------------------------------------------

        private void OnDurationTick(object sender, object e)
        {
            _durationSeconds++;
            DurationText = FormatDuration(_durationSeconds);
        }

        private static string FormatDuration(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            if (hours > 0)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2:D2}",
                    hours, minutes, seconds);
            }
            return string.Format(CultureInfo.InvariantCulture, "{0:D2}:{1:D2}",
                minutes, seconds);
        }

        private static string FormatError(CallError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case CallErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case CallErrorKind.Busy:
                    return "Peer is busy.";
                case CallErrorKind.AlreadyInCall:
                    return "Already in another call.";
                case CallErrorKind.CallNotFound:
                    return "Call not found.";
                case CallErrorKind.FingerprintMismatch:
                    return "Security check failed.";
                case CallErrorKind.MediaPlaneFailed:
                    return FormatCallingUnavailable(error.Message);
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }

        private static string FormatCallingUnavailable(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "Calls are not available in this build.";
            return "Calls are not available in this build: " + reason + ".";
        }

        /// <summary>
        /// Maps a native VoIP failure detail string (typically the
        /// <c>MediaPlaneFailed</c> message bubbled up from <c>voip_engine.cpp</c>)
        /// to a user-facing protocol-warning banner. Marker substrings are the
        /// diagnostic phrases emitted by the engine when the peer never
        /// answered our classic INIT (signaling-packets-received &gt; 0 +
        /// peer-supplied WebRTC endpoints + no classic handshake completed).
        /// </summary>
        private static string ComputeProtocolWarning(string detail)
        {
            if (!string.IsNullOrEmpty(detail))
            {
                if (detail.IndexOf("signaling packet", StringComparison.OrdinalIgnoreCase) >= 0
                    || detail.IndexOf("peer also supplied WebRTC endpoints", StringComparison.OrdinalIgnoreCase) >= 0
                    || detail.IndexOf("VoIP control handshake", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Voice calls between Vianigram clients use the classic Telegram VoIP protocol. " +
                           "The other party is using a modern Telegram client (tgcalls 2.x); support for " +
                           "that protocol is in development.";
                }
            }
            return "Call failed: " + (string.IsNullOrEmpty(detail) ? "unknown error." : detail);
        }
    }
}
