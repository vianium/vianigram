// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IncomingCallPageViewModel.cs
//
// Drives IncomingCallPage. OnNavigatedTo projects the supplied CallId
// onto peer/video state via ICallsApi.GetSession; Accept routes through
// AcceptCallAsync + Nav.NavigateTo(Route.Call); Decline routes through
// DiscardAsync(Busy) + Nav.GoBack().

using System;
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

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class IncomingCallPageViewModel : BaseViewModel
    {
        private readonly ICallsApi _calls;
        private readonly INavigationService _nav;

        private CallId _callId;
        private string _peerName;
        private string _avatarLetter;
        private string _errorMessage;
        private bool _isVideo;
        private bool _isBusy;

        public IncomingCallPageViewModel() : this(null, null)
        {
        }

        public IncomingCallPageViewModel(ICallsApi calls, INavigationService nav)
        {
            _calls = calls;
            _nav = nav;

            _peerName = string.Empty;
            _avatarLetter = "?";
            _isVideo = false;

            AcceptCommand = new AsyncCommand(_ => AcceptAsync(), _ => !_isBusy);
            DeclineCommand = new AsyncCommand(_ => DeclineAsync(), _ => !_isBusy);
        }

        // ---- Bindable surface ---------------------------------------

        public string PeerName
        {
            get { return _peerName; }
            set { SetProperty(ref _peerName, value); }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        public bool IsVideo
        {
            get { return _isVideo; }
            set
            {
                if (SetProperty(ref _isVideo, value))
                    OnPropertyChanged("CallTypeText");
            }
        }

        public string CallTypeText
        {
            get { return _isVideo ? "Incoming video call" : "Incoming voice call"; }
        }

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

        // ---- Commands -------------------------------------------------

        public AsyncCommand AcceptCommand { get; private set; }
        public AsyncCommand DeclineCommand { get; private set; }

        // ---- Lifecycle -----------------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;

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
                CallSession session = null;
                try
                {
                    session = _calls.GetSession(_callId);
                }
                catch (Exception ex)
                {
                    AppLog.For("App.IncomingCallPage").Error("GetSession threw: " + ex);
                }

                if (session != null)
                {
                    IsVideo = session.IsVideo;
                }
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Command handlers ---------------------------------------

        private async Task AcceptAsync()
        {
            ErrorMessage = null;

            if (_calls == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (!_calls.IsCallingAvailable)
            {
                ErrorMessage = FormatCallingUnavailable(_calls.CallingUnavailableReason);
                return;
            }

            _isBusy = true;
            try
            {
                Result<CallSession, CallError> result;
                try
                {
                    result = await _calls.AcceptCallAsync(_callId, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.IncomingCallPage").Error("AcceptCallAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                if (_nav != null) _nav.NavigateTo(Route.Call, _callId);
            }
            finally
            {
                _isBusy = false;
            }
        }

        private async Task DeclineAsync()
        {
            ErrorMessage = null;

            if (_calls == null)
            {
                ErrorMessage = "Service not available";
                if (_nav != null) _nav.GoBack();
                return;
            }

            _isBusy = true;
            try
            {
                Result<Unit, CallError> result;
                try
                {
                    result = await _calls.DiscardAsync(_callId, DiscardReason.Busy, CancellationToken.None)
                        .ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.IncomingCallPage").Error("DiscardAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    // Navigate back regardless — user intent was "decline".
                }
            }
            finally
            {
                _isBusy = false;
                if (_nav != null) _nav.GoBack();
            }
        }

        private static string FormatError(CallError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case CallErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case CallErrorKind.CallNotFound:
                    return "Call no longer available.";
                case CallErrorKind.NotInExpectedState:
                    return "Call already ended.";
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
    }
}
