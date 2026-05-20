// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ForwardPageViewModel.cs — forward-message destination picker VM.
// LoadDialogsAsync hits IChatsApi.GetDialogsAsync; SendCommand routes through
// IMessagesApi.ForwardAsync. Nav.GoBack on success.

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
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Ports.Inbound;
using MessagesUnit = Vianigram.Messages.Domain.ValueObjects.Unit;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class DialogVm : ObservableObject
    {
        private bool _isSelected;

        public string PeerKey { get; set; }
        public string DisplayName { get; set; }
        public string AvatarLetter { get; set; }
        public long AvatarColorSeed { get; set; }
        public string SubtitleText { get; set; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }

    public sealed class ForwardPageViewModel : ObservableObject
    {
        private const int PageSize = 50;

        private readonly IChatsApi _chats;
        private readonly IMessagesApi _messages;
        private readonly INavigationService _nav;

        private string _sourcePreview;
        private string _sourceSenderName;
        private string _sourceTimeLabel;
        private string _sourcePeerKey;
        private long[] _sourceMessageIds;
        private string _searchQuery;
        private string _comment;
        private string _statusText;
        private string _errorMessage;
        private bool _isBusy;
        private bool _isLoading;

        private readonly List<DialogVm> _allDialogs;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public ForwardPageViewModel()
            : this(null, null, null)
        {
        }

        public ForwardPageViewModel(IChatsApi chats, IMessagesApi messages, INavigationService nav)
        {
            _chats = chats;
            _messages = messages;
            _nav = nav;

            _allDialogs = new List<DialogVm>();
            Dialogs = new ObservableCollection<DialogVm>();
            SelectedDialogs = new ObservableCollection<DialogVm>();

            ToggleSelectCommand = new RelayCommand(p => ToggleSelect(p as DialogVm), _ => true);
            SendCommand = new AsyncCommand(_ => SendAsync(), _ => CanSend);
        }

        // ---- Properties --------------------------------------------------

        public ObservableCollection<DialogVm> Dialogs { get; private set; }
        public ObservableCollection<DialogVm> SelectedDialogs { get; private set; }

        public ICommand ToggleSelectCommand { get; private set; }
        public AsyncCommand SendCommand { get; private set; }

        public string SourcePreview
        {
            get { return _sourcePreview; }
            set { SetProperty(ref _sourcePreview, value); }
        }

        public string SourceSenderName
        {
            get { return _sourceSenderName; }
            set { SetProperty(ref _sourceSenderName, value); }
        }

        public string SourceTimeLabel
        {
            get { return _sourceTimeLabel; }
            set { SetProperty(ref _sourceTimeLabel, value); }
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

        public string Comment
        {
            get { return _comment; }
            set { SetProperty(ref _comment, value); }
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

        public bool CanSend
        {
            get { return !_isBusy && SelectedDialogs != null && SelectedDialogs.Count > 0; }
        }

        // ---- Navigation lifecycle ---------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            // Optional payload: ForwardSourceArgs carrying srcPeerKey / msgIds.
            var src = parameter as ForwardSourceArgs;
            if (src != null)
            {
                _sourcePeerKey = src.SourcePeerKey;
                _sourceMessageIds = src.MessageIds ?? new long[0];
                SourcePreview = src.Preview;
                SourceSenderName = src.SenderName;
                SourceTimeLabel = src.TimeLabel;
            }

            var ignore = LoadDialogsAsync(CancellationToken.None);
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Loaders ----------------------------------------------------

        public void SetDialogs(IEnumerable<DialogVm> dialogs)
        {
            _allDialogs.Clear();
            if (dialogs != null)
            {
                foreach (var d in dialogs)
                {
                    if (d != null) _allDialogs.Add(d);
                }
            }
            ApplyFilter();
        }

        public async Task LoadDialogsAsync(CancellationToken ct)
        {
            ErrorMessage = null;
            if (_chats == null)
            {
                ErrorMessage = "Service not available";
                return;
            }

            IsLoading = true;
            try
            {
                Result<DialogPage, ChatError> result;
                try
                {
                    result = await _chats.GetDialogsAsync(PageSize, DialogCursor.Empty, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    AppLog.For("App.ForwardPage").Error("GetDialogsAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatError(result.Error);
                    return;
                }

                ApplyDialogs(result.Value);
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ApplyDialogs(DialogPage page)
        {
            _allDialogs.Clear();
            if (page != null && page.Items != null)
            {
                for (int i = 0; i < page.Items.Count; i++)
                {
                    var preview = page.Items[i];
                    if (preview == null) continue;
                    _allDialogs.Add(ToDialogVm(preview));
                }
            }
            ApplyFilter();
        }

        private static DialogVm ToDialogVm(DialogPreview p)
        {
            string title = string.IsNullOrEmpty(p.Title) ? "(untitled)" : p.Title;
            string letter = "?";
            if (title.Length > 0)
            {
                char ch = title[0];
                letter = char.IsLetter(ch) ? char.ToUpperInvariant(ch).ToString() : "#";
            }
            return new DialogVm
            {
                PeerKey = p.Peer != null ? p.Peer.ToString() : string.Empty,
                DisplayName = title,
                AvatarLetter = letter,
                AvatarColorSeed = 0,
                SubtitleText = p.LastMessageText ?? string.Empty
            };
        }

        private void ApplyFilter()
        {
            Dialogs.Clear();
            string q = (_searchQuery ?? string.Empty).Trim().ToLowerInvariant();

            for (int i = 0; i < _allDialogs.Count; i++)
            {
                var d = _allDialogs[i];
                if (d == null) continue;
                if (q.Length == 0)
                {
                    Dialogs.Add(d);
                    continue;
                }

                string name = d.DisplayName != null ? d.DisplayName.ToLowerInvariant() : string.Empty;
                if (name.IndexOf(q, StringComparison.Ordinal) >= 0)
                {
                    Dialogs.Add(d);
                }
            }
        }

        private void ToggleSelect(DialogVm dialog)
        {
            if (dialog == null) return;
            dialog.IsSelected = !dialog.IsSelected;

            if (dialog.IsSelected)
            {
                if (!SelectedDialogs.Contains(dialog)) SelectedDialogs.Add(dialog);
            }
            else
            {
                if (SelectedDialogs.Contains(dialog)) SelectedDialogs.Remove(dialog);
            }

            int count = SelectedDialogs.Count;
            StatusText = count == 0
                ? "Pick a chat to forward to"
                : (count == 1 ? "1 chat selected" : count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " chats selected");

            OnPropertyChanged("CanSend");
            var cmd = SendCommand;
            if (cmd != null) cmd.RaiseCanExecuteChanged();
        }

        private async Task SendAsync()
        {
            if (!CanSend) return;

            ErrorMessage = null;
            if (_messages == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (string.IsNullOrEmpty(_sourcePeerKey) || _sourceMessageIds == null || _sourceMessageIds.Length == 0)
            {
                ErrorMessage = "Nothing to forward.";
                return;
            }

            var destKeys = new List<string>(SelectedDialogs.Count);
            for (int i = 0; i < SelectedDialogs.Count; i++)
            {
                var d = SelectedDialogs[i];
                if (d == null || string.IsNullOrEmpty(d.PeerKey)) continue;
                destKeys.Add(d.PeerKey);
            }
            if (destKeys.Count == 0)
            {
                ErrorMessage = "No destinations selected.";
                return;
            }

            var msgIds = new List<long>(_sourceMessageIds.Length);
            for (int i = 0; i < _sourceMessageIds.Length; i++) msgIds.Add(_sourceMessageIds[i]);

            IsBusy = true;
            StatusText = "Forwarding...";

            try
            {
                Result<MessagesUnit, MessageError> result;
                try
                {
                    result = await _messages.ForwardAsync(destKeys, _sourcePeerKey, msgIds, _comment ?? string.Empty, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.For("App.ForwardPage").Error("ForwardAsync threw: " + ex);
                    ErrorMessage = "Forward failed: " + ex.GetType().Name;
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatMessageError(result.Error);
                    return;
                }

                StatusText = "Forwarded";
                if (_nav != null) _nav.GoBack();
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static string FormatMessageError(MessageError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Code)
            {
                case MessageErrorCode.NetworkFailed:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case MessageErrorCode.FloodWait:
                    return error.Message ?? "Too many attempts.";
                case MessageErrorCode.NotFound:
                    return "Source message not found.";
                case MessageErrorCode.Unauthorized:
                    return "Cannot forward: forbidden.";
                default:
                    return error.Message ?? error.Code.ToString();
            }
        }

        private static string FormatError(ChatError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ChatErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case ChatErrorKind.AccessDenied:
                    return "Access denied.";
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }
    }

    /// <summary>Nav payload describing the message(s) being forwarded.</summary>
    public sealed class ForwardSourceArgs
    {
        public ForwardSourceArgs(string sourcePeerKey, long[] messageIds,
                                 string preview, string senderName, string timeLabel)
        {
            SourcePeerKey = sourcePeerKey;
            MessageIds = messageIds ?? new long[0];
            Preview = preview;
            SenderName = senderName;
            TimeLabel = timeLabel;
        }

        public string SourcePeerKey { get; private set; }
        public long[] MessageIds { get; private set; }
        public string Preview { get; private set; }
        public string SenderName { get; private set; }
        public string TimeLabel { get; private set; }
    }
}
