// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// SearchPageViewModel.cs — global search VM.
// Query setter kicks off ISearchApi.SearchGlobalAsync; ResultsArrived lands
// in the buckets via Dispatch.OnUiAsync. Open commands navigate via INavigationService.

using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Kernel.Result;
using Vianigram.Search.Domain;
using Vianigram.Search.Domain.Entities;
using Vianigram.Search.Domain.ValueObjects;
using Vianigram.Search.Ports.Inbound;
using Windows.UI.Xaml;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class SearchPageViewModel : ObservableObject
    {
        private readonly ISearchApi _search;
        private readonly INavigationService _nav;

        private string _query;
        private bool _isSearching;
        private string _errorMessage;
        private SearchSession _activeSession;
        private CancellationTokenSource _querySource;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public SearchPageViewModel()
            : this(null, null)
        {
        }

        public SearchPageViewModel(ISearchApi search, INavigationService nav)
        {
            _search = search;
            _nav = nav;
            ChatHits = new ObservableCollection<ChatHitVm>();
            ContactHits = new ObservableCollection<ContactHitVm>();
            MessageHits = new ObservableCollection<MessageHitVm>();

            OpenChatCommand = new RelayCommand(OnOpenChat);
            OpenContactCommand = new RelayCommand(OnOpenContact);
            OpenMessageCommand = new RelayCommand(OnOpenMessage);
        }

        public string Query
        {
            get { return _query; }
            set
            {
                if (SetProperty(ref _query, value))
                {
                    var ignore = StartSearchAsync(value);
                }
            }
        }

        public bool IsSearching
        {
            get { return _isSearching; }
            private set
            {
                if (SetProperty(ref _isSearching, value))
                {
                    OnPropertyChanged("HasNoResults");
                    OnPropertyChanged("NoResultsVisibility");
                    OnPropertyChanged("SearchingVisibility");
                }
            }
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

        public ObservableCollection<ChatHitVm> ChatHits { get; private set; }
        public ObservableCollection<ContactHitVm> ContactHits { get; private set; }
        public ObservableCollection<MessageHitVm> MessageHits { get; private set; }

        public bool HasChatHits { get { return ChatHits.Count > 0; } }
        public bool HasContactHits { get { return ContactHits.Count > 0; } }
        public bool HasMessageHits { get { return MessageHits.Count > 0; } }

        public Visibility ChatHitsVisibility { get { return HasChatHits ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility ContactHitsVisibility { get { return HasContactHits ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility MessageHitsVisibility { get { return HasMessageHits ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility NoResultsVisibility { get { return HasNoResults ? Visibility.Visible : Visibility.Collapsed; } }
        public Visibility SearchingVisibility { get { return _isSearching ? Visibility.Visible : Visibility.Collapsed; } }

        public bool HasNoResults
        {
            get
            {
                if (_isSearching) return false;
                if (string.IsNullOrEmpty(_query)) return false;
                return ChatHits.Count == 0 && ContactHits.Count == 0 && MessageHits.Count == 0;
            }
        }

        public ICommand OpenChatCommand { get; private set; }
        public ICommand OpenContactCommand { get; private set; }
        public ICommand OpenMessageCommand { get; private set; }

        // ---- Navigation lifecycle ---------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            if (_search != null) _search.ResultsArrived += OnResultsArrived;
        }

        public void OnNavigatedFrom(object parameter)
        {
            if (_search != null) _search.ResultsArrived -= OnResultsArrived;
            CancelInflight();
        }

        // ---- Search pipeline --------------------------------------------

        private async Task StartSearchAsync(string query)
        {
            ErrorMessage = null;
            CancelInflight();
            ClearBuckets();

            if (string.IsNullOrWhiteSpace(query))
            {
                _activeSession = null;
                IsSearching = false;
                RefreshBucketBindings();
                return;
            }

            if (_search == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            var cts = new CancellationTokenSource();
            _querySource = cts;
            IsSearching = true;
            try
            {
                Result<SearchSession, SearchError> result;
                try
                {
                    result = await _search.SearchGlobalAsync(query, SearchFilter.All, cts.Token).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.SearchPage").Error("SearchGlobalAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                _activeSession = result.Value;
                ProjectSession(_activeSession);
            }
            finally
            {
                if (_querySource == cts)
                {
                    IsSearching = false;
                    _querySource = null;
                }
                RefreshBucketBindings();
            }
        }

        private void OnResultsArrived(object sender, SearchResultsEventArgs args)
        {
            // Marshal back to UI before mutating the buckets.
            var ignore = Dispatch.OnUiAsync(() =>
            {
                if (args == null) return;
                if (_activeSession == null || args.SessionId != _activeSession.SessionId) return;
                ProjectHits(args.Page);
                RefreshBucketBindings();
            });
        }

        private void ProjectSession(SearchSession session)
        {
            if (session == null) return;
            ProjectHits(session.Results);
        }

        private void ProjectHits(System.Collections.Generic.IList<SearchHit> hits)
        {
            if (hits == null) return;
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                if (hit == null) continue;
                switch (hit.ResultType)
                {
                    case SearchHitKind.Chat:
                    case SearchHitKind.Channel:
                        ChatHits.Add(BuildChatHit(hit));
                        break;
                    case SearchHitKind.User:
                        ContactHits.Add(BuildContactHit(hit));
                        break;
                    case SearchHitKind.Message:
                    case SearchHitKind.Document:
                        MessageHits.Add(BuildMessageHit(hit));
                        break;
                    default:
                        break;
                }
            }
        }

        private static ChatHitVm BuildChatHit(SearchHit hit)
        {
            string id = hit.Payload != null ? hit.Payload.ToString() : string.Empty;
            return new ChatHitVm
            {
                EntityId = id,
                DisplayName = id,
                Preview = string.Empty,
                AvatarLetter = AvatarLetterFrom(id)
            };
        }

        private static ContactHitVm BuildContactHit(SearchHit hit)
        {
            string id = hit.Payload != null ? hit.Payload.ToString() : string.Empty;
            return new ContactHitVm
            {
                EntityId = id,
                DisplayName = id,
                AvatarLetter = AvatarLetterFrom(id)
            };
        }

        private static MessageHitVm BuildMessageHit(SearchHit hit)
        {
            string id = hit.Payload != null ? hit.Payload.ToString() : string.Empty;
            return new MessageHitVm
            {
                EntityId = id,
                DisplayName = id,
                Preview = string.Empty,
                AvatarLetter = AvatarLetterFrom(id)
            };
        }

        private static string AvatarLetterFrom(string id)
        {
            if (string.IsNullOrEmpty(id)) return "?";
            char c = id[0];
            return char.IsLetter(c) ? char.ToUpperInvariant(c).ToString() : "#";
        }

        private void ClearBuckets()
        {
            ChatHits.Clear();
            ContactHits.Clear();
            MessageHits.Clear();
        }

        private void RefreshBucketBindings()
        {
            OnPropertyChanged("HasChatHits");
            OnPropertyChanged("HasContactHits");
            OnPropertyChanged("HasMessageHits");
            OnPropertyChanged("HasNoResults");
            OnPropertyChanged("ChatHitsVisibility");
            OnPropertyChanged("ContactHitsVisibility");
            OnPropertyChanged("MessageHitsVisibility");
            OnPropertyChanged("NoResultsVisibility");
        }

        private void CancelInflight()
        {
            var cts = _querySource;
            _querySource = null;
            if (cts != null)
            {
                try { cts.Cancel(); }
                catch { }
            }

            // Optimistically cancel the in-flight session via the port.
            var session = _activeSession;
            if (session != null && _search != null)
            {
                var ignore = CancelSessionAsync(session);
            }
        }

        private async Task CancelSessionAsync(SearchSession session)
        {
            try
            {
                await _search.CancelAsync(session, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                AppLog.For("App.SearchPage").Error("CancelAsync threw: " + ex);
            }
        }

        // ---- Open handlers ----------------------------------------------

        private void OnOpenChat(object parameter)
        {
            var hit = parameter as ChatHitVm;
            if (hit == null || _nav == null) return;
            try { _nav.NavigateTo(Route.Chat, hit.EntityId); }
            catch (Exception ex) { AppLog.For("App.SearchPage").Error("OnOpenChat threw: " + ex); }
        }

        private void OnOpenContact(object parameter)
        {
            var hit = parameter as ContactHitVm;
            if (hit == null || _nav == null) return;
            try { _nav.NavigateTo(Route.Profile, hit.EntityId); }
            catch (Exception ex) { AppLog.For("App.SearchPage").Error("OnOpenContact threw: " + ex); }
        }

        private void OnOpenMessage(object parameter)
        {
            var hit = parameter as MessageHitVm;
            if (hit == null || _nav == null) return;
            try { _nav.NavigateTo(Route.Chat, new MessageScrollNavArgs(hit.EntityId, hit.EntityId)); }
            catch (Exception ex) { AppLog.For("App.SearchPage").Error("OnOpenMessage threw: " + ex); }
        }

        private static string FormatError(SearchError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case SearchErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case SearchErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                case SearchErrorKind.Cancelled:
                    return null;
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }

    public sealed class ChatHitVm
    {
        public string EntityId { get; set; }
        public string DisplayName { get; set; }
        public string Preview { get; set; }
        public string AvatarLetter { get; set; }
    }

    public sealed class ContactHitVm
    {
        public string EntityId { get; set; }
        public string DisplayName { get; set; }
        public string AvatarLetter { get; set; }
    }

    public sealed class MessageHitVm
    {
        public string EntityId { get; set; }
        public string DisplayName { get; set; }
        public string Preview { get; set; }
        public string AvatarLetter { get; set; }
    }

    /// <summary>Nav payload for opening a chat scrolled to a specific message.</summary>
    public sealed class MessageScrollNavArgs
    {
        public MessageScrollNavArgs(string peerKey, string scrollToMsgId)
        {
            PeerKey = peerKey;
            ScrollToMsgId = scrollToMsgId;
        }

        public string PeerKey { get; private set; }
        public string ScrollToMsgId { get; private set; }
    }
}
