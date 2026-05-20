// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CallsPageViewModel.cs
//
// Projects the Calls bounded-context recent sessions into the Metro calls
// history surface. The live in-call controls stay in CallPageViewModel.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Vianigram.Composition.Infrastructure;
using Vianigram.Kernel.Result;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Media;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class CallsPageViewModel : BaseViewModel
    {
        private readonly ICallsApi _calls;
        private readonly INavigationService _nav;
        private readonly IPeerCache _peerCache;
        private readonly bool _designMode;

        private string _errorMessage;
        private bool _isLoading;
        private bool _subscribed;

        public CallsPageViewModel()
            : this(null, null, null, true)
        {
        }

        public CallsPageViewModel(ICallsApi calls, INavigationService nav, IPeerCache peerCache)
            : this(calls, nav, peerCache, false)
        {
        }

        private CallsPageViewModel(ICallsApi calls, INavigationService nav, IPeerCache peerCache, bool designMode)
        {
            _calls = calls;
            _nav = nav;
            _peerCache = peerCache;
            _designMode = designMode;

            AllCalls = new ObservableCollection<CallLogRow>();
            MissedCalls = new ObservableCollection<CallLogRow>();

            NewCallCommand = new RelayCommand(_ => NavigateTo(Route.Contacts));
            SearchCommand = new RelayCommand(_ => NavigateTo(Route.Search));

            if (_designMode)
                ApplyRows(CreateDesignRows());
        }

        public ObservableCollection<CallLogRow> AllCalls { get; private set; }
        public ObservableCollection<CallLogRow> MissedCalls { get; private set; }

        public ICommand NewCallCommand { get; private set; }
        public ICommand SearchCommand { get; private set; }

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

        public bool ShowAllEmpty
        {
            get { return !IsLoading && AllCalls.Count == 0; }
        }

        public bool ShowMissedEmpty
        {
            get { return !IsLoading && MissedCalls.Count == 0; }
        }

        public void OnNavigatedTo(object parameter)
        {
            Subscribe();
            var ignore = LoadAsync(CancellationToken.None);
        }

        public void OnNavigatedFrom(object parameter)
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_calls == null || _subscribed) return;
            _calls.StateChanged += OnCallStateChanged;
            _calls.IncomingCall += OnIncomingCall;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (_calls == null || !_subscribed) return;
            _calls.StateChanged -= OnCallStateChanged;
            _calls.IncomingCall -= OnIncomingCall;
            _subscribed = false;
        }

        private void OnCallStateChanged(object sender, CallStateChangedEventArgs e)
        {
            var ignore = Dispatch.OnUiAsync(() =>
            {
                var reload = LoadAsync(CancellationToken.None);
            });
        }

        private void OnIncomingCall(object sender, CallReceivedEventArgs e)
        {
            var ignore = Dispatch.OnUiAsync(() =>
            {
                var reload = LoadAsync(CancellationToken.None);
            });
        }

        public async Task LoadAsync(CancellationToken ct)
        {
            if (_designMode) return;

            ErrorMessage = null;
            if (_calls == null)
            {
                ErrorMessage = Strings.Get("CallsServiceUnavailable");
                return;
            }

            IsLoading = true;
            RaiseEmptyStateChanged();
            try
            {
                Result<IList<CallSession>, CallError> result =
                    await _calls.ListRecentAsync(ct).ConfigureAwait(true);
                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                IList<CallSession> sessions = result.Value ?? new CallSession[0];
                List<CallLogRow> rows = new List<CallLogRow>(sessions.Count);
                for (int i = 0; i < sessions.Count; i++)
                {
                    CallLogRow row = ToRow(sessions[i]);
                    if (row != null) rows.Add(row);
                }
                rows.Sort(CompareRowsDescending);
                ApplyRows(rows);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.CallsPage").Error("LoadAsync threw: " + ex);
                ErrorMessage = Strings.Get("CallsUnexpectedError") + ex.GetType().Name + ".";
            }
            finally
            {
                IsLoading = false;
                RaiseEmptyStateChanged();
            }
        }

        public void OpenProfile(CallLogRow row)
        {
            if (row == null || _nav == null || row.UserId <= 0) return;
            _nav.NavigateTo(Route.Profile, new ProfilePageNavArgs
            {
                PeerKey = row.PeerKey,
                DisplayName = row.PeerName,
                StatusText = Strings.Get("ProfileStatusRecently"),
                UserId = row.UserId,
                IsSelf = false
            });
        }

        public async Task StartCallAsync(CallLogRow row)
        {
            if (row == null || row.UserId <= 0) return;
            if (_calls == null)
            {
                ErrorMessage = Strings.Get("CallsServiceUnavailable");
                return;
            }
            if (!_calls.IsCallingAvailable)
            {
                ErrorMessage = FormatCallingUnavailable(_calls.CallingUnavailableReason);
                return;
            }

            ErrorMessage = null;
            try
            {
                Result<CallSession, CallError> result =
                    await _calls.RequestCallAsync(row.UserId, row.IsVideo, CancellationToken.None)
                        .ConfigureAwait(true);
                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }
                if (_nav != null && result.Value != null)
                    _nav.NavigateTo(Route.Call, result.Value.CallId);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.CallsPage").Error("StartCallAsync threw: " + ex);
                ErrorMessage = Strings.Get("CallsUnexpectedError") + ex.GetType().Name + ".";
            }
        }

        private void NavigateTo(Route route)
        {
            if (_nav != null) _nav.NavigateTo(route);
        }

        private void ApplyRows(IList<CallLogRow> rows)
        {
            AllCalls.Clear();
            MissedCalls.Clear();
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    CallLogRow row = rows[i];
                    if (row == null) continue;
                    AllCalls.Add(row);
                    if (row.IsMissed) MissedCalls.Add(row);
                }
            }
            RaiseEmptyStateChanged();
        }

        private void RaiseEmptyStateChanged()
        {
            OnPropertyChanged("ShowAllEmpty");
            OnPropertyChanged("ShowMissedEmpty");
        }

        private CallLogRow ToRow(CallSession session)
        {
            if (session == null) return null;
            long userId = session.PeerUserId;
            string name = ResolveUserName(userId);
            bool missed = session.State == CallSessionState.Discarded &&
                (session.DiscardReason == DiscardReason.Missed ||
                 (!session.IsInitiator && session.Duration.Seconds == 0));

            return CallLogRow.Create(
                userId,
                name,
                session.IsInitiator,
                session.IsVideo,
                missed,
                session.LastActivityAt == default(DateTime) ? session.CreatedAt : session.LastActivityAt,
                session.Duration.Seconds);
        }

        private string ResolveUserName(long userId)
        {
            if (_peerCache != null && userId > 0)
            {
                string cached = _peerCache.GetUserDisplayName(userId);
                if (!string.IsNullOrEmpty(cached)) return cached;
            }
            return userId > 0
                ? string.Format(CultureInfo.InvariantCulture, "user {0}", userId)
                : Strings.Get("CallsUnknownPeer");
        }

        private static int CompareRowsDescending(CallLogRow a, CallLogRow b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            return b.SortDateUtc.CompareTo(a.SortDateUtc);
        }

        private static string FormatError(CallError error)
        {
            if (error == null) return Strings.Get("CallsUnknownError");
            switch (error.Kind)
            {
                case CallErrorKind.NetworkError:
                    return Strings.Get("CallsNetworkError");
                case CallErrorKind.AlreadyInCall:
                    return Strings.Get("CallsAlreadyInCall");
                case CallErrorKind.CallNotFound:
                    return Strings.Get("CallsNotFound");
                case CallErrorKind.FingerprintMismatch:
                    return "Call security check failed.";
                case CallErrorKind.MediaPlaneFailed:
                    return FormatCallingUnavailable(error.Message);
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }

        private static string FormatCallingUnavailable(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return Strings.Get("CallsServiceUnavailable");
            return reason;
        }

        private static IList<CallLogRow> CreateDesignRows()
        {
            DateTime now = DateTime.UtcNow;
            return new[]
            {
                CallLogRow.Create(1001, "Mira Sato", true, true, false, now.AddMinutes(-9), 1122),
                CallLogRow.Create(1002, "Theo Park", false, false, false, now.AddHours(-1), 248),
                CallLogRow.Create(1003, "Anya Volkov", false, false, true, now.AddDays(-2).AddHours(-1), 0),
                CallLogRow.Create(1004, "Holt Mendez", true, false, false, now.AddDays(-2), 42),
                CallLogRow.Create(1005, "mom", false, true, false, now.AddDays(-4), 3133),
                CallLogRow.Create(1006, "Dr. Okafor", true, true, false, now.AddDays(-7), 724)
            };
        }
    }

    public sealed class CallLogRow
    {
        private static readonly SolidColorBrush AccentBrush = new SolidColorBrush(Color.FromArgb(255, 27, 161, 226));
        private static readonly SolidColorBrush SuccessBrush = new SolidColorBrush(Color.FromArgb(255, 96, 169, 23));
        private static readonly SolidColorBrush DangerBrush = new SolidColorBrush(Color.FromArgb(255, 229, 20, 0));

        public long UserId { get; set; }
        public string PeerKey { get; set; }
        public string PeerName { get; set; }
        public string Initials { get; set; }
        public long AvatarColorSeed { get; set; }
        public bool IsOutgoing { get; set; }
        public bool IsVideo { get; set; }
        public bool IsMissed { get; set; }
        public string DirectionGlyph { get; set; }
        public Brush DirectionBrush { get; set; }
        public string MetaText { get; set; }
        public DateTime SortDateUtc { get; set; }

        public Visibility VideoIconVisibility
        {
            get { return IsVideo ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility PhoneIconVisibility
        {
            get { return IsVideo ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility NormalNameVisibility
        {
            get { return IsMissed ? Visibility.Collapsed : Visibility.Visible; }
        }

        public Visibility MissedNameVisibility
        {
            get { return IsMissed ? Visibility.Visible : Visibility.Collapsed; }
        }

        public static CallLogRow Create(long userId, string name, bool outgoing, bool video, bool missed, DateTime atUtc, int durationSeconds)
        {
            string safeName = string.IsNullOrWhiteSpace(name) ? Strings.Get("CallsUnknownPeer") : name.Trim();
            string direction = missed
                ? Strings.Get("CallsDirectionMissed")
                : (outgoing ? Strings.Get("CallsDirectionOutgoing") : Strings.Get("CallsDirectionIncoming"));
            string when = FormatDate(atUtc);
            string duration = durationSeconds > 0 ? FormatDuration(durationSeconds) : string.Empty;
            string meta = string.IsNullOrEmpty(duration)
                ? string.Format(CultureInfo.CurrentCulture, "{0} · {1}", direction, when)
                : string.Format(CultureInfo.CurrentCulture, "{0} · {1} · {2}", direction, when, duration);

            return new CallLogRow
            {
                UserId = userId,
                PeerKey = userId > 0 ? "user:" + userId.ToString(CultureInfo.InvariantCulture) : string.Empty,
                PeerName = safeName,
                Initials = CreateInitials(safeName),
                AvatarColorSeed = CreateColorSeed(userId, safeName),
                IsOutgoing = outgoing,
                IsVideo = video,
                IsMissed = missed,
                DirectionGlyph = missed ? "↘" : (outgoing ? "↗" : "↙"),
                DirectionBrush = missed ? DangerBrush : (outgoing ? SuccessBrush : AccentBrush),
                MetaText = meta,
                SortDateUtc = atUtc
            };
        }

        private static string FormatDate(DateTime utc)
        {
            if (utc == default(DateTime)) return string.Empty;
            DateTime local = utc.ToLocalTime();
            DateTime today = DateTime.Now.Date;
            string time = local.ToString("H:mm", CultureInfo.CurrentCulture);
            if (local.Date == today)
                return Strings.Get("CallsDateToday") + ", " + time;
            if (local.Date == today.AddDays(-1))
                return Strings.Get("CallsDateYesterday") + ", " + time;
            return local.ToString("ddd, H:mm", CultureInfo.CurrentCulture).ToLowerInvariant();
        }

        private static string FormatDuration(int totalSeconds)
        {
            int hours = totalSeconds / 3600;
            int minutes = (totalSeconds % 3600) / 60;
            int seconds = totalSeconds % 60;
            if (hours > 0)
                return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2:D2}", hours, minutes, seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}", minutes, seconds);
        }

        private static string CreateInitials(string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return "?";
            string[] parts = title.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "?";
            if (parts.Length == 1)
            {
                string single = parts[0];
                if (single.Length <= 1) return single.ToUpperInvariant();
                return single.Substring(0, 2).ToUpperInvariant();
            }
            return (parts[0].Substring(0, 1) + parts[parts.Length - 1].Substring(0, 1)).ToUpperInvariant();
        }

        private static long CreateColorSeed(long userId, string name)
        {
            string source = userId > 0 ? userId.ToString(CultureInfo.InvariantCulture) : (name ?? string.Empty);
            long seed = 17;
            for (int i = 0; i < source.Length; i++)
                seed = (seed * 31) + source[i];
            return seed;
        }
    }
}
