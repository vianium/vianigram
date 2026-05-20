// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppViewModels.SettingsCompose.cs
// Partial owning the Create<Xxx>PageViewModel factories for the
// Settings + Compose cluster (Settings, ActiveSessions, Scheduled, Search,
// NewChat, NewChannel, Forward, Poll).

using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Composition.Roots;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Messages.Ports.Inbound;
using Vianigram.Privacy.Ports.Inbound;
using Vianigram.Search.Ports.Inbound;
using Vianigram.SecretChats.Ports.Inbound;
using Vianigram.Settings.Ports.Inbound;

namespace Vianigram.App.Services
{
    public static partial class AppViewModels
    {
        // ---- Settings batch ---------------------------------------------

        public static SettingsPageViewModel CreateSettingsPageViewModel(VianigramCompositionRoot c)
        {
            IAccountApi account = null;
            ISettingsApi settings = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IAccountApi>(out account);
                c.TryResolve<ISettingsApi>(out settings);
                c.TryResolve<INavigationService>(out nav);
            }
            return new SettingsPageViewModel(account, settings, nav);
        }

        public static ActiveSessionsPageViewModel CreateActiveSessionsPageViewModel(VianigramCompositionRoot c)
        {
            IPrivacyApi privacy = null;
            if (c != null) c.TryResolve<IPrivacyApi>(out privacy);
            return new ActiveSessionsPageViewModel(privacy);
        }

        public static ScheduledPageViewModel CreateScheduledPageViewModel(VianigramCompositionRoot c)
        {
            IMessagesApi messages = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IMessagesApi>(out messages);
                c.TryResolve<INavigationService>(out nav);
            }
            return new ScheduledPageViewModel(messages, nav);
        }

        public static SearchPageViewModel CreateSearchPageViewModel(VianigramCompositionRoot c)
        {
            ISearchApi search = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<ISearchApi>(out search);
                c.TryResolve<INavigationService>(out nav);
            }
            return new SearchPageViewModel(search, nav);
        }

        // ---- Compose batch ----------------------------------------------

        public static NewChatPageViewModel CreateNewChatPageViewModel(VianigramCompositionRoot c)
        {
            IContactsApi contacts = null;
            IChatsApi chats = null;
            ISecretChatsApi secret = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IContactsApi>(out contacts);
                c.TryResolve<IChatsApi>(out chats);
                c.TryResolve<ISecretChatsApi>(out secret);
                c.TryResolve<INavigationService>(out nav);
            }
            return new NewChatPageViewModel(contacts, chats, secret, nav);
        }

        public static NewChannelPageViewModel CreateNewChannelPageViewModel(VianigramCompositionRoot c)
        {
            IChatsApi chats = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IChatsApi>(out chats);
                c.TryResolve<INavigationService>(out nav);
            }
            return new NewChannelPageViewModel(chats, nav);
        }

        public static ForwardPageViewModel CreateForwardPageViewModel(VianigramCompositionRoot c)
        {
            IChatsApi chats = null;
            IMessagesApi messages = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IChatsApi>(out chats);
                c.TryResolve<IMessagesApi>(out messages);
                c.TryResolve<INavigationService>(out nav);
            }
            return new ForwardPageViewModel(chats, messages, nav);
        }

        public static PollPageViewModel CreatePollPageViewModel(VianigramCompositionRoot c)
        {
            IMessagesApi messages = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IMessagesApi>(out messages);
                c.TryResolve<INavigationService>(out nav);
            }
            return new PollPageViewModel(messages, nav);
        }
    }
}
