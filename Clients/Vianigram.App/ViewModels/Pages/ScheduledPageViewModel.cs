// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// ScheduledPageViewModel.cs — pending scheduled-messages VM.
// Load / SendNow / Delete route through IMessagesApi (GetScheduled,
// SendScheduledNow, DeleteScheduled). Edit navigates to the chat in
// compose-edit mode.

using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Vianigram.App.Navigation;
using Vianigram.App.Services;
using Vianigram.Kernel.Result;
using Vianigram.Messages.Domain;
using Vianigram.Messages.Domain.Entities;
using Vianigram.Messages.Domain.ValueObjects;
using Vianigram.Messages.Ports.Inbound;
using Windows.UI.Xaml;
using MessagesUnit = Vianigram.Messages.Domain.ValueObjects.Unit;

namespace Vianigram.App.ViewModels.Pages
{
    public sealed class ScheduledPageViewModel : ObservableObject
    {
        private readonly IMessagesApi _messages;
        private readonly INavigationService _nav;

        private string _peerKey;
        private string _errorMessage;
        private bool _isLoading;

        /// <summary>Design-time fallback constructor — degraded mode.</summary>
        public ScheduledPageViewModel()
            : this(null, null)
        {
        }

        public ScheduledPageViewModel(IMessagesApi messages, INavigationService nav)
        {
            _messages = messages;
            _nav = nav;
            Messages = new ObservableCollection<ScheduledMessageVm>();

            SendNowCommand = new AsyncCommand(SendNowAsync, _ => true);
            EditCommand = new RelayCommand(OnEdit);
            DeleteCommand = new AsyncCommand(DeleteAsync, _ => true);
        }

        public ObservableCollection<ScheduledMessageVm> Messages { get; private set; }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged("LoadingVisibility");
                    OnPropertyChanged("IsEmpty");
                    OnPropertyChanged("EmptyVisibility");
                }
            }
        }

        public bool IsEmpty
        {
            get { return !_isLoading && Messages.Count == 0; }
        }

        public Visibility LoadingVisibility
        {
            get { return _isLoading ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility EmptyVisibility
        {
            get { return IsEmpty ? Visibility.Visible : Visibility.Collapsed; }
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

        public AsyncCommand SendNowCommand { get; private set; }
        public ICommand EditCommand { get; private set; }
        public AsyncCommand DeleteCommand { get; private set; }

        // ---- Navigation lifecycle ---------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            _peerKey = parameter as string;
            ErrorMessage = null;
            var ignore = LoadAsync(_peerKey, CancellationToken.None);
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        public async Task LoadAsync(string peerKey, CancellationToken ct)
        {
            ErrorMessage = null;
            if (_messages == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (string.IsNullOrEmpty(peerKey))
            {
                OnPropertyChanged("IsEmpty");
                OnPropertyChanged("EmptyVisibility");
                return;
            }

            IsLoading = true;
            try
            {
                Result<MessagePage, MessageError> result;
                try
                {
                    result = await _messages.GetScheduledAsync(peerKey, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    AppLog.For("App.ScheduledPage").Error("GetScheduledAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatMessageError(result.Error);
                    return;
                }

                Messages.Clear();
                MessagePage page = result.Value;
                if (page != null && page.Messages != null)
                {
                    for (int i = 0; i < page.Messages.Count; i++)
                    {
                        Message m = page.Messages[i];
                        if (m == null) continue;
                        Messages.Add(Project(m));
                    }
                }

                OnPropertyChanged("IsEmpty");
                OnPropertyChanged("EmptyVisibility");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private static ScheduledMessageVm Project(Message m)
        {
            var text = m.Content as MessageContentText;
            string preview = text != null ? text.Body : string.Empty;
            return new ScheduledMessageVm
            {
                MessageId = m.Id != null ? m.Id.ServerId : 0L,
                Preview = preview ?? string.Empty,
                ScheduledAtText = m.Date.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
            };
        }

        private async Task SendNowAsync(object parameter)
        {
            var msg = parameter as ScheduledMessageVm;
            if (msg == null) return;
            ErrorMessage = null;
            if (_messages == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (string.IsNullOrEmpty(_peerKey)) return;

            Result<MessagesUnit, MessageError> result;
            try
            {
                result = await _messages.SendScheduledNowAsync(_peerKey, msg.MessageId, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.ScheduledPage").Error("SendScheduledNowAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatMessageError(result.Error);
                return;
            }

            Messages.Remove(msg);
            OnPropertyChanged("IsEmpty");
            OnPropertyChanged("EmptyVisibility");
        }

        private void OnEdit(object parameter)
        {
            var msg = parameter as ScheduledMessageVm;
            if (msg == null) return;
            if (_nav == null) return;
            try
            {
                // Chat opens in compose-edit mode — destination is the same peer.
                var arg = new ScheduledEditNavArgs(_peerKey, msg);
                _nav.NavigateTo(Route.Chat, arg);
            }
            catch (Exception ex)
            {
                AppLog.For("App.ScheduledPage").Error("OnEdit threw: " + ex);
            }
        }

        private async Task DeleteAsync(object parameter)
        {
            var msg = parameter as ScheduledMessageVm;
            if (msg == null) return;
            ErrorMessage = null;
            if (_messages == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            if (string.IsNullOrEmpty(_peerKey)) return;

            Result<MessagesUnit, MessageError> result;
            try
            {
                result = await _messages.DeleteScheduledAsync(_peerKey, msg.MessageId, CancellationToken.None).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppLog.For("App.ScheduledPage").Error("DeleteScheduledAsync threw: " + ex);
                ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                return;
            }

            if (result.IsFail)
            {
                ErrorMessage = FormatMessageError(result.Error);
                return;
            }

            Messages.Remove(msg);
            OnPropertyChanged("IsEmpty");
            OnPropertyChanged("EmptyVisibility");
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
                    return "Message not found.";
                case MessageErrorCode.Unauthorized:
                    return "Not signed in.";
                default:
                    return error.Message ?? error.Code.ToString();
            }
        }
    }

    public sealed class ScheduledMessageVm
    {
        public long MessageId { get; set; }
        public string Preview { get; set; }
        public string ScheduledAtText { get; set; }
    }

    /// <summary>Nav payload carried into ChatPage when editing a scheduled message.</summary>
    public sealed class ScheduledEditNavArgs
    {
        public ScheduledEditNavArgs(string peerKey, ScheduledMessageVm draft)
        {
            PeerKey = peerKey;
            Draft = draft;
        }

        public string PeerKey { get; private set; }
        public ScheduledMessageVm Draft { get; private set; }
    }
}
