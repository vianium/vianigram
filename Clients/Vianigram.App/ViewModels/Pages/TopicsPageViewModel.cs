// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// TopicsPageViewModel.cs
//
// Drives TopicsPage. OnNavigatedTo loads forum-topic rows for the
// supplied channel peer via IChatsApi.GetForumTopicsAsync; CreateTopic
// goes through IChatsApi.CreateForumTopicAsync. OpenTopic routes through
// INavigationService.

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
using Vianigram.Chats.Domain;
using Vianigram.Chats.Domain.ValueObjects;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Kernel.Result;

namespace Vianigram.App.ViewModels.Pages
{
    /// <summary>
    /// Single row in the topics list. Public + sealed so .NET Native can
    /// reflect for binding under the Release configuration.
    /// </summary>
    public sealed class TopicVm : ObservableObject
    {
        private string _title;
        private string _iconEmoji;
        private string _lastMessagePreview;
        private int _unreadCount;
        private long _topicId;

        public string Title
        {
            get { return _title; }
            set { SetProperty(ref _title, value); }
        }

        public string IconEmoji
        {
            get { return _iconEmoji; }
            set { SetProperty(ref _iconEmoji, value); }
        }

        public string LastMessagePreview
        {
            get { return _lastMessagePreview; }
            set { SetProperty(ref _lastMessagePreview, value); }
        }

        public int UnreadCount
        {
            get { return _unreadCount; }
            set { SetProperty(ref _unreadCount, value); }
        }

        public long TopicId
        {
            get { return _topicId; }
            set { SetProperty(ref _topicId, value); }
        }
    }

    public sealed class TopicsPageViewModel : BaseViewModel
    {
        private readonly IChatsApi _chats;
        private readonly INavigationService _nav;

        private string _channelPeerKey;
        private string _forumTitle;
        private string _avatarLetter;
        private bool _isBusy;
        private string _errorMessage;

        public TopicsPageViewModel() : this(null, null)
        {
        }

        public TopicsPageViewModel(IChatsApi chats, INavigationService nav)
        {
            _chats = chats;
            _nav = nav;

            _forumTitle = "Topics";
            _avatarLetter = "#";

            Topics = new ObservableCollection<TopicVm>();

            OpenTopicCommand = new RelayCommand(p => OnOpenTopic(p), _ => true);
            CreateTopicCommand = new AsyncCommand(_ => CreateTopicAsync(), _ => !_isBusy);
        }

        // ---- Bindable surface ---------------------------------------

        public string ForumTitle
        {
            get { return _forumTitle; }
            set { SetProperty(ref _forumTitle, value); }
        }

        public string AvatarLetter
        {
            get { return _avatarLetter; }
            set { SetProperty(ref _avatarLetter, value); }
        }

        public ObservableCollection<TopicVm> Topics { get; private set; }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (SetProperty(ref _isBusy, value))
                    OnPropertyChanged("IsEmpty");
            }
        }

        public bool IsEmpty
        {
            get { return !_isBusy && Topics.Count == 0; }
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

        public string ChannelPeerKey
        {
            get { return _channelPeerKey; }
        }

        // ---- Commands -------------------------------------------------

        public ICommand OpenTopicCommand { get; private set; }
        public ICommand CreateTopicCommand { get; private set; }

        // ---- Lifecycle -----------------------------------------------

        public void OnNavigatedTo(object parameter)
        {
            ErrorMessage = null;
            _channelPeerKey = parameter as string;
            var ignore = LoadTopicsAsync(CancellationToken.None);
        }

        private async Task LoadTopicsAsync(CancellationToken ct)
        {
            if (_chats == null)
            {
                ErrorMessage = "Service not available";
                OnPropertyChanged("IsEmpty");
                return;
            }
            PeerId channel = ParseChannelPeerKey(_channelPeerKey);
            if (channel == null)
            {
                OnPropertyChanged("IsEmpty");
                return;
            }

            IsBusy = true;
            try
            {
                Result<IList<ForumTopic>, ChatError> result;
                try
                {
                    result = await _chats.GetForumTopicsAsync(channel, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    AppLog.For("App.TopicsPage").Error("GetForumTopicsAsync threw: " + ex);
                    ErrorMessage = "Unexpected error: " + ex.GetType().Name + ".";
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatChatError(result.Error);
                    return;
                }

                Topics.Clear();
                IList<ForumTopic> topics = result.Value;
                if (topics != null)
                {
                    for (int i = 0; i < topics.Count; i++)
                    {
                        ForumTopic t = topics[i];
                        if (t == null) continue;
                        Topics.Add(new TopicVm
                        {
                            TopicId = t.TopicId,
                            Title = t.Title ?? string.Empty,
                            IconEmoji = t.IconEmoji ?? string.Empty,
                            UnreadCount = t.UnreadCount,
                            LastMessagePreview = string.Empty
                        });
                    }
                }
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged("IsEmpty");
            }
        }

        public void OnNavigatedFrom(object parameter)
        {
        }

        // ---- Command handlers ---------------------------------------

        private void OnOpenTopic(object parameter)
        {
            var topic = parameter as TopicVm;
            if (topic == null) return;
            if (_nav == null) return;

            object payload = new TopicNavigationArgs(_channelPeerKey, topic.TopicId);
            _nav.NavigateTo(Route.Chat, payload);
        }

        private async Task CreateTopicAsync()
        {
            ErrorMessage = null;

            if (_chats == null)
            {
                ErrorMessage = "Service not available";
                return;
            }
            PeerId channel = ParseChannelPeerKey(_channelPeerKey);
            if (channel == null)
            {
                ErrorMessage = "Cannot create topic: unknown channel.";
                return;
            }

            // Title default — page-side dialog will populate this in a later wave;
            // for now, send an empty string so the call surface is exercised.
            string title = _forumTitle ?? string.Empty;
            string icon = string.Empty;

            IsBusy = true;
            try
            {
                Result<ForumTopic, ChatError> result;
                try
                {
                    result = await _chats.CreateForumTopicAsync(channel, title, icon, CancellationToken.None).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.For("App.TopicsPage").Error("CreateForumTopicAsync threw: " + ex);
                    ErrorMessage = "Create failed: " + ex.GetType().Name;
                    return;
                }

                if (result.IsFail)
                {
                    ErrorMessage = FormatChatError(result.Error);
                    return;
                }

                long topicId = result.Value != null ? result.Value.TopicId : 0L;
                if (_nav == null) return;
                object payload = new TopicNavigationArgs(_channelPeerKey, topicId);
                _nav.NavigateTo(Route.Chat, payload);
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ---- Helpers --------------------------------------------------

        private static PeerId ParseChannelPeerKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            int colon = key.IndexOf(':');
            if (colon <= 0 || colon == key.Length - 1) return null;
            string kind = key.Substring(0, colon);
            string idText = key.Substring(colon + 1);
            long id;
            if (!long.TryParse(idText, NumberStyles.Integer, CultureInfo.InvariantCulture, out id)) return null;
            try
            {
                if (string.Equals(kind, "channel", StringComparison.OrdinalIgnoreCase))
                    return PeerId.Channel(id, 0L);
            }
            catch (ArgumentException)
            {
            }
            return null;
        }

        private static string FormatChatError(ChatError error)
        {
            if (error == null) return "Unknown error.";
            switch (error.Kind)
            {
                case ChatErrorKind.NetworkError:
                    return "Network error: " + (string.IsNullOrEmpty(error.Message) ? "no connection" : error.Message);
                case ChatErrorKind.AccessDenied:
                    return "Access denied.";
                case ChatErrorKind.PeerNotFound:
                    return "Channel not found.";
                default:
                    return error.Message ?? error.Code ?? error.Kind.ToString();
            }
        }
    }

    /// <summary>
    /// Navigation payload carried from TopicsPage to ChatPage when the
    /// user opens (or creates) a forum topic. Public + sealed so .NET
    /// Native can reflect it.
    /// </summary>
    public sealed class TopicNavigationArgs
    {
        public TopicNavigationArgs(string channelPeerKey, long topicId)
        {
            ChannelPeerKey = channelPeerKey ?? string.Empty;
            TopicId = topicId;
        }

        public string ChannelPeerKey { get; private set; }
        public long TopicId { get; private set; }
    }
}
