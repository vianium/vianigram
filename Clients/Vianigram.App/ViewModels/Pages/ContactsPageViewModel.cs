// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ContactsPageViewModel.cs — contacts page VM.
// Wires IContactsApi (sync / search / events). Subscribes to ContactsChanged
// in OnNavigatedTo and unsubscribes in OnNavigatedFrom (via Dispatch.OnUiAsync).

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Kernel.Result;

namespace Vianigram.App.ViewModels.Pages
{
    /// <summary>Row-level binding shape for one contact entry.</summary>
    public sealed class ContactVm : ObservableObject
    {
        private string _displayName;
        private string _phone;
        private bool _isOnline;
        private string _avatarLetter;
        private string _peerKey;
        private bool _isSelected;
        private long _avatarColorSeed;
        private long _userId;

        public string DisplayName
        {
            get { return _displayName; }
            set
            {
                if (SetProperty(ref _displayName, value))
                    OnPropertyChanged("GroupKey");
            }
        }

        public string Phone
        {
            get { return _phone; }
            set { SetProperty(ref _phone, value); }
        }

        public bool IsOnline
        {
            get { return _isOnline; }
            set
            {
                if (SetProperty(ref _isOnline, value))
                    OnPropertyChanged("StatusText");
            }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        public string PeerKey
        {
            get { return _peerKey; }
            set { SetProperty(ref _peerKey, value); }
        }

        public string StatusText
        {
            get { return _isOnline ? "online" : "offline"; }
        }

        public string GroupKey
        {
            get
            {
                if (string.IsNullOrEmpty(_displayName)) return "#";
                char c = _displayName[0];
                if (char.IsLetter(c)) return char.ToUpperInvariant(c).ToString();
                return "#";
            }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }

        public long AvatarColorSeed
        {
            get { return _avatarColorSeed; }
            set { SetProperty(ref _avatarColorSeed, value); }
        }

        public long UserId
        {
            get { return _userId; }
            set { _userId = value; }
        }
    }

    public sealed class ContactsPageViewModel : ObservableObject
    {
        private readonly IContactsApi _contacts;
        private readonly INavigationService _nav;

        private string _searchQuery;
        private bool _isLoading;
        private string _errorMessage;
        private bool _isSubscribed;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public ContactsPageViewModel()
            : this(null, null)
        {
        }

        public ContactsPageViewModel(IContactsApi contacts, INavigationService nav)
        {
            _contacts = contacts;
            _nav = nav;
            _searchQuery = string.Empty;
            Contacts = new ObservableCollection<ContactVm>();
            FilteredContacts = new ObservableCollection<ContactVm>();

            SelectContactCommand = new RelayCommand(OnSelectContact, _ => true);
            RefreshCommand = new AsyncCommand(_ => RefreshAsync(), _ => true);
        }

        // ---- Collections ---------------------------------------------

        public ObservableCollection<ContactVm> Contacts { get; private set; }

        public ObservableCollection<ContactVm> FilteredContacts { get; private set; }

        // ---- Search ---------------------------------------------------

        public string SearchQuery
        {
            get { return _searchQuery; }
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    OnPropertyChanged("HasSearch");
                    ApplyFilter();
                    if (!string.IsNullOrEmpty(value) && value.Length > 2)
                    {
                        var ignore = SearchAsync(value);
                    }
                }
            }
        }

        public bool HasSearch
        {
            get { return !string.IsNullOrEmpty(_searchQuery); }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
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

        public bool IsEmpty
        {
            get { return FilteredContacts.Count == 0; }
        }

        // ---- Commands -------------------------------------------------

        public ICommand SelectContactCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }

        // ---- Navigation lifecycle ------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            Subscribe();
            if (Contacts.Count == 0)
            {
                var ignore = RefreshAsync();
            }
            else
            {
                ApplyFilter();
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
            Unsubscribe();
        }

        // ---- Subscription --------------------------------------------

        private void Subscribe()
        {
            if (_contacts == null || _isSubscribed) return;
            _contacts.ContactsChanged += OnContactsChanged;
            _isSubscribed = true;
        }

        private void Unsubscribe()
        {
            if (_contacts == null || !_isSubscribed) return;
            _contacts.ContactsChanged -= OnContactsChanged;
            _isSubscribed = false;
        }

        private void OnContactsChanged(object sender, ContactsChangedEventArgs args)
        {
            // Marshal to UI thread before mutating ObservableCollection.
            var ignore = Dispatch.OnUiAsync(() =>
            {
                var refresh = RefreshAsync();
            });
        }

        // ---- Loaders --------------------------------------------------

        // Hydrate from the local cache via GetContactsAsync (no MTProto round-trip);
        // fall back to SyncContactsAsync when the cache returns empty.
        private async Task RefreshAsync()
        {
            ErrorMessage = null;
            if (_contacts == null)
            {
                ErrorMessage = "Contacts service not available.";
                return;
            }

            IsLoading = true;
            try
            {
                Result<IList<Contact>, ContactsError> cached;
                try
                {
                    cached = await _contacts.GetContactsAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (cached.IsFail)
                {
                    ErrorMessage = FormatError(cached.Error);
                    return;
                }

                if (cached.Value != null && cached.Value.Count > 0)
                {
                    Apply(cached.Value);
                    return;
                }

                // Cache empty — pull a fresh sync from the server.
                Result<IList<Contact>, ContactsError> result;
                try
                {
                    result = await _contacts.SyncContactsAsync(CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }
                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }
                Apply(result.Value);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SearchAsync(string query)
        {
            if (_contacts == null) return;
            try
            {
                var result = await _contacts.SearchAsync(query, 50, CancellationToken.None).ConfigureAwait(true);
                if (result.IsFail) return;
                Apply(result.Value);
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Search is best-effort — don't surface a transient.
            }
        }

        private void Apply(IList<Contact> source)
        {
            Contacts.Clear();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    var c = source[i];
                    if (c == null) continue;
                    Contacts.Add(Project(c));
                }
            }
            ApplyFilter();
        }

        private static ContactVm Project(Contact c)
        {
            string display = c.DisplayName ?? string.Empty;
            string letter = "?";
            if (display.Length > 0 && char.IsLetter(display[0]))
            {
                letter = char.ToUpperInvariant(display[0]).ToString();
            }
            long userId = c.UserId.Value;
            return new ContactVm
            {
                DisplayName = display,
                Phone = c.Phone ?? string.Empty,
                AvatarLetter = letter,
                PeerKey = "user:" + userId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                UserId = userId,
                AvatarColorSeed = userId
            };
        }

        private void ApplyFilter()
        {
            FilteredContacts.Clear();
            string q = (_searchQuery ?? string.Empty).Trim().ToLowerInvariant();

            for (int i = 0; i < Contacts.Count; i++)
            {
                var c = Contacts[i];
                if (c == null) continue;
                if (q.Length == 0 || MatchesQuery(c, q))
                {
                    FilteredContacts.Add(c);
                }
            }

            OnPropertyChanged("IsEmpty");
        }

        private static bool MatchesQuery(ContactVm c, string lowercaseQuery)
        {
            string name = c.DisplayName != null ? c.DisplayName.ToLowerInvariant() : string.Empty;
            string phone = c.Phone != null ? c.Phone.ToLowerInvariant() : string.Empty;
            return name.Contains(lowercaseQuery) || phone.Contains(lowercaseQuery);
        }

        private void OnSelectContact(object parameter)
        {
            var c = parameter as ContactVm;
            if (c == null || _nav == null) return;
            if (string.IsNullOrEmpty(c.PeerKey)) return;
            _nav.NavigateTo(Route.Chat, c.PeerKey);
        }

        private static string FormatError(ContactsError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ContactsErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case ContactsErrorKind.PermissionDenied:
                    return "Permission denied.";
                case ContactsErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }
}
