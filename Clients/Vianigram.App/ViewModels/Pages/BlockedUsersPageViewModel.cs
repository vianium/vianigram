// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BlockedUsersPageViewModel.cs — blocked users page VM.
// Wires IContactsApi (GetBlockedListAsync, UnblockAsync). No navigation deps.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.Contacts.Domain;
using Vianigram.Contacts.Domain.ValueObjects;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Kernel.Result;

namespace Vianigram.App.ViewModels.Pages
{
    /// <summary>Row-level binding shape for one blocked user entry.</summary>
    public sealed class BlockedUserVm : ObservableObject
    {
        private string _displayName;
        private string _avatarLetter;
        private string _peerKey;
        private long _userId;

        public string DisplayName
        {
            get { return _displayName; }
            set { SetProperty(ref _displayName, value); }
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

        public long UserId
        {
            get { return _userId; }
            set { _userId = value; }
        }
    }

    public sealed class BlockedUsersPageViewModel : ObservableObject
    {
        private readonly IContactsApi _contacts;

        private bool _isLoading;
        private string _errorMessage;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public BlockedUsersPageViewModel()
            : this(null)
        {
        }

        public BlockedUsersPageViewModel(IContactsApi contacts)
        {
            _contacts = contacts;
            Users = new ObservableCollection<BlockedUserVm>();
            UnblockCommand = new AsyncCommand(p => UnblockAsync(p as BlockedUserVm), _ => true);
        }

        public ObservableCollection<BlockedUserVm> Users { get; private set; }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set { SetProperty(ref _isLoading, value); }
        }

        public bool IsEmpty
        {
            get { return Users.Count == 0; }
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

        public ICommand UnblockCommand { get; private set; }

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            var ignore = ReloadAsync();
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        private async Task ReloadAsync()
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
                Result<IList<long>, ContactsError> result;
                try
                {
                    result = await _contacts.GetBlockedListAsync(CancellationToken.None).ConfigureAwait(true);
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

                Users.Clear();
                IList<long> ids = result.Value;
                if (ids != null)
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        long id = ids[i];
                        Users.Add(new BlockedUserVm
                        {
                            UserId = id,
                            DisplayName = "User " + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            AvatarLetter = "U",
                            PeerKey = "user:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                        });
                    }
                }
                OnPropertyChanged("IsEmpty");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task UnblockAsync(BlockedUserVm user)
        {
            if (user == null) return;
            ErrorMessage = null;
            if (_contacts == null)
            {
                ErrorMessage = "Contacts service not available.";
                return;
            }

            try
            {
                Result<Unit, ContactsError> result;
                try
                {
                    result = await _contacts.UnblockAsync(user.UserId, CancellationToken.None).ConfigureAwait(true);
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

                if (Users.Contains(user))
                {
                    Users.Remove(user);
                    OnPropertyChanged("IsEmpty");
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = "Could not unblock: " + ex.GetType().Name;
            }
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
                case ContactsErrorKind.NotFound:
                    return "User not found.";
                case ContactsErrorKind.FloodWait:
                    int retry = error.RetryAfterSeconds.HasValue ? error.RetryAfterSeconds.Value : 0;
                    return "Too many attempts. Try again in " + retry + " s.";
                default:
                    return error.Message ?? error.Kind.ToString();
            }
        }
    }
}
