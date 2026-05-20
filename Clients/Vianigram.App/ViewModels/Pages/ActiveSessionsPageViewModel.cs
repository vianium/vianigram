// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ActiveSessionsPageViewModel.cs — devices / sessions VM.
// Loads ActiveSession list from IPrivacyApi; Terminate / TerminateAll route
// through the same port. Subscribes to SessionTerminated to refresh live.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Services;
using Vianigram.Kernel.Result;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.ValueObjects;
using Vianigram.Privacy.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class ActiveSessionsPageViewModel : ObservableObject
    {
        private readonly IPrivacyApi _privacy;

        private SessionVm _currentSession;
        private string _errorMessage;
        private bool _isLoading;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public ActiveSessionsPageViewModel()
            : this(null)
        {
        }

        public ActiveSessionsPageViewModel(IPrivacyApi privacy)
        {
            _privacy = privacy;
            OtherSessions = new ObservableCollection<SessionVm>();

            TerminateCommand = new AsyncCommand(p => TerminateAsync(p as SessionVm), p => CanTerminate(p as SessionVm));
            TerminateAllCommand = new AsyncCommand(_ => TerminateAllAsync(),
                                                   _ => OtherSessions.Count > 0);
        }

        public SessionVm CurrentSession
        {
            get { return _currentSession; }
            private set { SetProperty(ref _currentSession, value); }
        }

        public ObservableCollection<SessionVm> OtherSessions { get; private set; }

        public AsyncCommand TerminateCommand { get; private set; }
        public AsyncCommand TerminateAllCommand { get; private set; }

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

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
        }

        public bool HasOtherSessions
        {
            get { return OtherSessions.Count > 0; }
        }

        // ---- Navigation lifecycle ---------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            if (_privacy != null) _privacy.SessionTerminated += OnSessionTerminated;
            var ignore = LoadAsync(CancellationToken.None);
        }

        public void OnNavigatedFrom(object parameter)
        {
            if (_privacy != null) _privacy.SessionTerminated -= OnSessionTerminated;
        }

        private void OnSessionTerminated(object sender, SessionTerminatedEventArgs args)
        {
            // Event arrives on the publisher's thread — marshal to UI.
            var ignore = Dispatch.OnUiAsync(() =>
            {
                var loadIgnore = LoadAsync(CancellationToken.None);
            });
        }

        // ---- Loaders ----------------------------------------------------

        public async Task LoadAsync(CancellationToken ct)
        {
            ErrorMessage = null;
            if (_privacy == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            IsLoading = true;
            try
            {
                Result<IList<ActiveSession>, PrivacyError> result;
                try
                {
                    result = await _privacy.GetSessionsAsync(ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.ActiveSessionsPage").Error("GetSessionsAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                ApplySessions(result.Value);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplySessions(IList<ActiveSession> sessions)
        {
            OtherSessions.Clear();
            CurrentSession = null;

            if (sessions != null)
            {
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (s == null) continue;
                    var row = SessionVm.From(s);
                    if (s.IsCurrent) CurrentSession = row;
                    else OtherSessions.Add(row);
                }
            }

            OnPropertyChanged("HasOtherSessions");
            TerminateAllCommand.RaiseCanExecuteChanged();
        }

        private static bool CanTerminate(SessionVm session)
        {
            return session != null && !session.IsCurrent;
        }

        private async Task TerminateAsync(SessionVm session)
        {
            ErrorMessage = null;
            if (_privacy == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (session == null || session.IsCurrent) return;

            try
            {
                Result<Unit, PrivacyError> result;
                try
                {
                    result = await _privacy.TerminateSessionAsync(session.Hash, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.ActiveSessionsPage").Error("TerminateSessionAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                if (OtherSessions.Remove(session))
                {
                    OnPropertyChanged("HasOtherSessions");
                    TerminateAllCommand.RaiseCanExecuteChanged();
                }
            }
            catch (Exception ex)
            {
                AppLog.For("App.ActiveSessionsPage").Error("TerminateAsync threw: " + ex);
            }
        }

        private async Task TerminateAllAsync()
        {
            ErrorMessage = null;
            if (_privacy == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            try
            {
                Result<Unit, PrivacyError> result;
                try
                {
                    result = await _privacy.TerminateAllOtherSessionsAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.ActiveSessionsPage").Error("TerminateAllOtherSessionsAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                OtherSessions.Clear();
                OnPropertyChanged("HasOtherSessions");
                TerminateAllCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                AppLog.For("App.ActiveSessionsPage").Error("TerminateAllAsync threw: " + ex);
            }
        }

        private static string FormatError(PrivacyError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case PrivacyErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case PrivacyErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                case PrivacyErrorKind.NotAuthenticated:
                    return "Not authenticated.";
                case PrivacyErrorKind.ResetForbidden:
                    return "Cannot reset sessions yet — try again later.";
                case PrivacyErrorKind.CurrentSessionTermination:
                    return "Cannot terminate the current session.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }

    public sealed class SessionVm
    {
        public long Hash { get; set; }
        public string DeviceName { get; set; }
        public string AppVersion { get; set; }
        public string Ip { get; set; }
        public string Location { get; set; }
        public string LastActiveText { get; set; }
        public bool IsCurrent { get; set; }

        public string IpAndLocation
        {
            get
            {
                var ip = Ip ?? string.Empty;
                var loc = Location ?? string.Empty;
                if (ip.Length == 0) return loc;
                if (loc.Length == 0) return ip;
                return ip + "  •  " + loc;
            }
        }

        public static SessionVm From(ActiveSession s)
        {
            if (s == null) return null;
            string deviceName = string.IsNullOrEmpty(s.DeviceModel) ? s.Platform : s.DeviceModel;
            if (string.IsNullOrEmpty(deviceName)) deviceName = s.IsCurrent ? "This device" : "Unknown device";
            string appLine = (s.AppName ?? string.Empty);
            if (!string.IsNullOrEmpty(s.AppVersion))
                appLine = (appLine.Length == 0 ? string.Empty : appLine + " ") + s.AppVersion;
            string locLine = string.IsNullOrEmpty(s.Region) ? (s.Country ?? string.Empty) : s.Region;
            string lastActive = s.IsCurrent ? "Online" : FormatLastActive(s.DateActive);
            return new SessionVm
            {
                Hash = s.Hash,
                DeviceName = deviceName,
                AppVersion = appLine,
                Ip = s.Ip ?? string.Empty,
                Location = locLine,
                LastActiveText = lastActive,
                IsCurrent = s.IsCurrent
            };
        }

        private static string FormatLastActive(DateTime utc)
        {
            if (utc == default(DateTime)) return string.Empty;
            var local = utc.ToLocalTime();
            var now = DateTime.Now;
            if (local.Date == now.Date) return local.ToString("HH:mm");
            if ((now - local).TotalDays < 7) return local.ToString("ddd");
            return local.ToString("yyyy-MM-dd");
        }
    }
}
