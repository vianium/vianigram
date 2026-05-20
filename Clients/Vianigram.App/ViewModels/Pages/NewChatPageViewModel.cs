// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// NewChatPageViewModel.cs — group / secret / channel creation flow.
// Loads contacts via IContactsApi.SyncContactsAsync; Group/Channel creation route
// through IChatsApi.CreateGroupAsync / CreateChannelAsync; Secret routes through
// ISecretChatsApi.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.Entities;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Kernel.Result;
using Vianigram.SecretChats.Domain;
using Vianigram.SecretChats.Domain.Entities;
using Vianigram.SecretChats.Ports.Inbound;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class NewChatPageViewModel : ObservableObject
    {
        private readonly IContactsApi _contacts;
        private readonly IChatsApi _chats;
        private readonly ISecretChatsApi _secret;
        private readonly INavigationService _nav;

        private string _step;            // "contacts" | "meta"
        private string _chatType;        // "Group" | "Secret" | "Channel"
        private string _searchQuery;
        private string _groupTitle;
        private string _statusText;
        private string _errorMessage;
        private bool _isBusy;
        private bool _isLoading;

        private List<ContactVm> _allContacts;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public NewChatPageViewModel()
            : this(null, null, null, null)
        {
        }

        public NewChatPageViewModel(IContactsApi contacts, IChatsApi chats, ISecretChatsApi secret, INavigationService nav)
        {
            _contacts = contacts;
            _chats = chats;
            _secret = secret;
            _nav = nav;
            _step = "contacts";
            _chatType = "Group";
            _allContacts = new List<ContactVm>();

            Contacts = new ObservableCollection<ContactVm>();
            Selected = new ObservableCollection<ContactVm>();

            NextCommand = new RelayCommand(_ => GoToMetaStep(), _ => CanGoNext);
            BackCommand = new RelayCommand(_ => GoBackToContacts(), _ => true);
            ToggleSelectCommand = new RelayCommand(p => ToggleSelect(p as ContactVm), _ => true);
            CreateCommand = new AsyncCommand(_ => CreateAsync(), _ => CanCreate);
        }

        // ---- Properties --------------------------------------------------

        public ObservableCollection<ContactVm> Contacts { get; private set; }
        public ObservableCollection<ContactVm> Selected { get; private set; }

        public ICommand NextCommand { get; private set; }
        public ICommand BackCommand { get; private set; }
        public ICommand ToggleSelectCommand { get; private set; }
        public AsyncCommand CreateCommand { get; private set; }

        public string Step
        {
            get { return _step; }
            private set
            {
                if (SetProperty(ref _step, value))
                {
                    OnPropertyChanged("IsContactsStep");
                    OnPropertyChanged("IsMetaStep");
                }
            }
        }

        public bool IsContactsStep
        {
            get { return string.Equals(_step, "contacts", StringComparison.Ordinal); }
        }

        public bool IsMetaStep
        {
            get { return string.Equals(_step, "meta", StringComparison.Ordinal); }
        }

        public string ChatType
        {
            get { return _chatType; }
            set
            {
                if (SetProperty(ref _chatType, value))
                {
                    OnPropertyChanged("IsGroup");
                    OnPropertyChanged("IsSecret");
                    OnPropertyChanged("IsChannel");
                }
            }
        }

        public bool IsGroup
        {
            get { return string.Equals(_chatType, "Group", StringComparison.Ordinal); }
            set { if (value) ChatType = "Group"; }
        }

        public bool IsSecret
        {
            get { return string.Equals(_chatType, "Secret", StringComparison.Ordinal); }
            set { if (value) ChatType = "Secret"; }
        }

        public bool IsChannel
        {
            get { return string.Equals(_chatType, "Channel", StringComparison.Ordinal); }
            set { if (value) ChatType = "Channel"; }
        }

        public string SearchQuery
        {
            get { return _searchQuery; }
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplyFilter();
                }
            }
        }

        public string GroupTitle
        {
            get { return _groupTitle; }
            set
            {
                if (SetProperty(ref _groupTitle, value))
                {
                    OnPropertyChanged("CanCreate");
                    var cmd = CreateCommand;
                    if (cmd != null) cmd.RaiseCanExecuteChanged();
                }
            }
        }

        public string StatusText
        {
            get { return _statusText; }
            private set { SetProperty(ref _statusText, value); }
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

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { SetProperty(ref _isBusy, value); }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
        }

        public bool CanGoNext
        {
            get { return Selected != null && Selected.Count > 0; }
        }

        public bool CanCreate
        {
            get
            {
                if (_isBusy) return false;
                if (Selected == null || Selected.Count == 0) return false;
                bool needsTitle = !string.Equals(_chatType, "Secret", StringComparison.Ordinal);
                if (needsTitle && string.IsNullOrWhiteSpace(_groupTitle)) return false;
                return true;
            }
        }

        // ---- Navigation lifecycle ---------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            if (_contacts != null) _contacts.ContactsChanged += OnContactsChanged;
            var ignore = LoadContactsAsync(CancellationToken.None);
        }

        public void OnNavigatedFrom(object parameter)
        {
            if (_contacts != null) _contacts.ContactsChanged -= OnContactsChanged;
        }

        private void OnContactsChanged(object sender, ContactsChangedEventArgs args)
        {
            // Marshal to UI before mutating the contact list.
            var ignore = Dispatch.OnUiAsync(() =>
            {
                var loadIgnore = LoadContactsAsync(CancellationToken.None);
            });
        }

        // ---- Loaders ----------------------------------------------------

        public async Task LoadContactsAsync(CancellationToken ct)
        {
            ErrorMessage = null;
            if (_contacts == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            IsLoading = true;
            try
            {
                Result<IList<Contact>, ContactsError> result;
                try
                {
                    result = await _contacts.SyncContactsAsync(ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.NewChatPage").Error("SyncContactsAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatContacts(result.Error);
                    return;
                }

                ApplyContacts(result.Value);
            }
            finally
            {
                IsLoading = false;
                UpdateStatusText();
            }
        }

        public void SetContacts(IEnumerable<ContactVm> contacts)
        {
            _allContacts.Clear();
            if (contacts != null)
            {
                foreach (var c in contacts)
                {
                    if (c != null) _allContacts.Add(c);
                }
            }
            ApplyFilter();
        }

        private void ApplyContacts(IList<Contact> contacts)
        {
            _allContacts.Clear();
            if (contacts != null)
            {
                for (int i = 0; i < contacts.Count; i++)
                {
                    var c = contacts[i];
                    if (c == null) continue;
                    _allContacts.Add(ToContactVm(c));
                }
            }
            ApplyFilter();
        }

        private static ContactVm ToContactVm(Contact c)
        {
            string name = c.DisplayName ?? string.Empty;
            string letter = "?";
            if (name.Length > 0)
            {
                char ch = name[0];
                letter = char.IsLetter(ch) ? char.ToUpperInvariant(ch).ToString() : "#";
            }
            return new ContactVm
            {
                DisplayName = name,
                Phone = c.Phone ?? string.Empty,
                AvatarLetter = letter,
                AvatarColorSeed = c.UserId.Value,
                UserId = c.UserId.Value,
                PeerKey = "user:" + c.UserId.Value
            };
        }

        // ---- Filtering / selection --------------------------------------

        private void GoToMetaStep()
        {
            if (!CanGoNext) return;
            Step = "meta";
            ErrorMessage = null;
        }

        private void GoBackToContacts()
        {
            Step = "contacts";
            ErrorMessage = null;
        }

        private void ToggleSelect(ContactVm contact)
        {
            if (contact == null) return;
            contact.IsSelected = !contact.IsSelected;

            if (contact.IsSelected)
            {
                if (!Selected.Contains(contact)) Selected.Add(contact);
            }
            else
            {
                if (Selected.Contains(contact)) Selected.Remove(contact);
            }

            UpdateStatusText();
            OnPropertyChanged("CanGoNext");
            OnPropertyChanged("CanCreate");
            var cmd = CreateCommand;
            if (cmd != null) cmd.RaiseCanExecuteChanged();
        }

        private void UpdateStatusText()
        {
            int count = Selected != null ? Selected.Count : 0;
            if (count == 0)
            {
                StatusText = "Select contacts to add";
            }
            else if (count == 1)
            {
                StatusText = "1 contact selected";
            }
            else
            {
                StatusText = count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                             + " contacts selected";
            }
        }

        private void ApplyFilter()
        {
            Contacts.Clear();
            string q = (_searchQuery ?? string.Empty).Trim().ToLowerInvariant();

            for (int i = 0; i < _allContacts.Count; i++)
            {
                var c = _allContacts[i];
                if (c == null) continue;
                if (q.Length == 0)
                {
                    Contacts.Add(c);
                    continue;
                }

                string name = c.DisplayName != null ? c.DisplayName.ToLowerInvariant() : string.Empty;
                if (name.IndexOf(q, StringComparison.Ordinal) >= 0)
                {
                    Contacts.Add(c);
                }
            }
        }

        // ---- Create flow -------------------------------------------------

        private async Task CreateAsync()
        {
            if (!CanCreate) return;

            IsBusy = true;
            ErrorMessage = null;
            StatusText = "Creating...";

            try
            {
                if (string.Equals(_chatType, "Secret", StringComparison.Ordinal))
                {
                    await CreateSecretAsync().ConfigureAwait(true);
                }
                else if (string.Equals(_chatType, "Channel", StringComparison.Ordinal))
                {
                    await CreateChannelAsync().ConfigureAwait(true);
                }
                else
                {
                    await CreateGroupAsync().ConfigureAwait(true);
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task CreateGroupAsync()
        {
            if (_chats == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            var userIds = new List<long>(Selected.Count);
            for (int i = 0; i < Selected.Count; i++)
            {
                var c = Selected[i];
                if (c == null) continue;
                if (c.UserId != 0) userIds.Add(c.UserId);
            }

            Result<Dialog, ChatError> result;
            try
            {
                result = await _chats.CreateGroupAsync(_groupTitle ?? string.Empty, userIds, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.NewChatPage").Error("CreateGroupAsync threw: " + ex);
                ErrorMessage = "Create failed: " + ex.GetType().Name;
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatChat(result.Error);
                return;
            }

            StatusText = "Group created";
            if (_nav != null && result.Value != null && result.Value.Peer != null)
            {
                _nav.NavigateTo(Route.Chat, result.Value.Peer.ToString());
            }
        }

        private async Task CreateChannelAsync()
        {
            if (_chats == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            // The compose flow does not collect a description / public flag here;
            // those live on NewChannelPage. From this entry point we always create
            // a private channel with no username and rely on later edits.
            Result<Dialog, ChatError> result;
            try
            {
                result = await _chats.CreateChannelAsync(
                    _groupTitle ?? string.Empty,
                    string.Empty,
                    false,
                    string.Empty,
                    CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.NewChatPage").Error("CreateChannelAsync threw: " + ex);
                ErrorMessage = "Create failed: " + ex.GetType().Name;
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatChat(result.Error);
                return;
            }

            StatusText = "Channel created";
            if (_nav != null && result.Value != null && result.Value.Peer != null)
            {
                _nav.NavigateTo(Route.Chat, result.Value.Peer.ToString());
            }
        }

        private static string FormatChat(ChatError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ChatErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case ChatErrorKind.AccessDenied:
                    return "Access denied.";
                case ChatErrorKind.PeerNotFound:
                    return "Peer not found.";
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }

        private async Task CreateSecretAsync()
        {
            if (_secret == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (Selected.Count == 0) return;

            long userId = Selected[0].UserId;

            Result<SecretSession, SecretChatError> result;
            try
            {
                result = await _secret.RequestAsync(userId, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                AppLog.For("App.NewChatPage").Error("RequestAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatSecret(result.Error);
                return;
            }

            StatusText = "Secret chat requested";
            if (_nav != null && result.Value != null)
            {
                _nav.NavigateTo(Route.SecretChat, result.Value.ChatId);
            }
        }

        private static string FormatContacts(ContactsError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ContactsErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case ContactsErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }

        private static string FormatSecret(SecretChatError error)
        {
            if (error == null) return "Unknown error.";
            return error.Message ?? error.Kind.ToString();
        }
    }
}
