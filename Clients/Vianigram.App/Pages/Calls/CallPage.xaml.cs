// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CallPage.xaml.cs — code-behind is intentionally minimal.
//
// InitializeComponent + nav delegations only. The CallPageViewModel owns
// peer info / state / commands and routes hangup through INavigationService.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Inbound;
using Windows.Media.Core;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages.Calls
{
    public sealed partial class CallPage : Page
    {
        private CallPageViewModel _vm;
        private ICallsApi _calls;
        private CallId _callId;
        private bool _playbackStarted;
        private bool _directNativeAudioLogged;

        public CallPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            object parameter = e != null ? e.Parameter : null;
            if (parameter is CallId)
            {
                _callId = (CallId)parameter;
            }
            else if (parameter is long)
            {
                _callId = new CallId((long)parameter);
            }

            if (_vm == null && App.Composition != null)
            {
                _vm = AppViewModels.CreateCallPageViewModel(App.Composition);
                DataContext = _vm;
            }
            if (_vm != null) _vm.OnNavigatedTo(parameter);

            if (_calls == null && App.Composition != null)
            {
                ICallsApi calls;
                if (App.Composition.TryResolve<ICallsApi>(out calls))
                {
                    _calls = calls;
                    _calls.StateChanged += OnCallStateChanged;
                }
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (_vm != null) _vm.OnNavigatedFrom(e != null ? e.Parameter : null);
            if (_calls != null)
            {
                _calls.StateChanged -= OnCallStateChanged;
            }
            StopRemotePlayback();
        }

        private void OnCallStateChanged(object sender, CallStateChangedEventArgs args)
        {
            if (args == null || args.CallId != _callId) return;
            if (args.State == CallSessionState.Active)
            {
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
                {
                    StartRemotePlayback();
                });
            }
            else if (args.State == CallSessionState.Discarded)
            {
                var ignore = Dispatcher.RunAsync(CoreDispatcherPriority.Normal, delegate
                {
                    StopRemotePlayback();
                });
            }
        }

        private void StartRemotePlayback()
        {
            if (_playbackStarted || _calls == null) return;
            object source = _calls.CreatePlaybackSource(_callId);
            MediaStreamSource stream = source as MediaStreamSource;
            if (stream == null)
            {
                if (!_directNativeAudioLogged)
                {
                    _directNativeAudioLogged = true;
                    AppLog.For("App.CallPage").Info(
                        "Remote audio is owned by the native VoIP runtime; MediaElement source is not required for callId=" + _callId);
                }
                _playbackStarted = true;
                return;
            }

            RemoteAudioPlayer.AutoPlay = true;
            RemoteAudioPlayer.SetMediaStreamSource(stream);
            RemoteAudioPlayer.Play();
            _playbackStarted = true;
            AppLog.For("App.CallPage").Info("Remote audio playback started for callId=" + _callId);
        }

        private void StopRemotePlayback()
        {
            try { RemoteAudioPlayer.Stop(); }
            catch { }
            _playbackStarted = false;
            _directNativeAudioLogged = false;
        }

        private void RemoteAudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            _playbackStarted = false;
            StartRemotePlayback();
        }

        private void RemoteAudioPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            AppLog.For("App.CallPage").Error("Remote audio playback failed: " +
                (e == null ? "(no detail)" : e.ErrorMessage));
            _playbackStarted = false;
        }
    }
}
