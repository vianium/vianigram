// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppViewModels.AuthProfile.cs
// Partial owning Create<Xxx>PageViewModel factories for the Auth + Profile
// cluster. Each factory resolves ports via TryResolve and constructs the VM
// with null-tolerant DI.

using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Calls.Ports.Inbound;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Composition.Roots;
using Vianigram.Contacts.Ports.Inbound;
using Vianigram.Media.Ports.Inbound;
using Vianigram.Notifications.Ports.Inbound;
using Vianigram.Privacy.Ports.Inbound;
using Vianigram.Settings.Ports.Inbound;

namespace Vianigram.App.Services
{
    public static partial class AppViewModels
    {
        public static QrLoginPageViewModel CreateQrLoginPageViewModel(VianigramCompositionRoot c)
        {
            IAccountApi account = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IAccountApi>(out account);
                c.TryResolve<INavigationService>(out nav);
            }
            return new QrLoginPageViewModel(account, nav);
        }

        public static AccountSwitcherPageViewModel CreateAccountSwitcherPageViewModel(VianigramCompositionRoot c)
        {
            INavigationService nav = null;
            IAccountApi account = null;
            if (c != null)
            {
                c.TryResolve<INavigationService>(out nav);
                c.TryResolve<IAccountApi>(out account);
            }
            return new AccountSwitcherPageViewModel(nav, account);
        }

        public static PasscodePageViewModel CreatePasscodePageViewModel(VianigramCompositionRoot c)
        {
            IPrivacyApi privacy = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IPrivacyApi>(out privacy);
                c.TryResolve<INavigationService>(out nav);
            }
            return new PasscodePageViewModel(privacy, nav);
        }

        public static TwoFaPasswordPageViewModel CreateTwoFaPasswordPageViewModel(VianigramCompositionRoot c)
        {
            IAccountApi account = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IAccountApi>(out account);
                c.TryResolve<INavigationService>(out nav);
            }
            return new TwoFaPasswordPageViewModel(account, nav);
        }

        public static SignUpPageViewModel CreateSignUpPageViewModel(VianigramCompositionRoot c)
        {
            IAccountApi account = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IAccountApi>(out account);
                c.TryResolve<INavigationService>(out nav);
            }
            return new SignUpPageViewModel(account, nav);
        }

        public static ProxySettingsPageViewModel CreateProxySettingsPageViewModel(VianigramCompositionRoot c)
        {
            INavigationService nav = null;
            ISettingsApi settings = null;
            if (c != null)
            {
                c.TryResolve<INavigationService>(out nav);
                c.TryResolve<ISettingsApi>(out settings);
            }
            return new ProxySettingsPageViewModel(nav, settings);
        }

        public static ProfilePageViewModel CreateProfilePageViewModel(VianigramCompositionRoot c)
        {
            IContactsApi contacts = null;
            ICallsApi calls = null;
            IAccountApi account = null;
            INotificationsApi notifications = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IContactsApi>(out contacts);
                c.TryResolve<ICallsApi>(out calls);
                c.TryResolve<IAccountApi>(out account);
                c.TryResolve<INotificationsApi>(out notifications);
                c.TryResolve<INavigationService>(out nav);
            }
            return new ProfilePageViewModel(contacts, calls, account, notifications, nav);
        }

        public static EditProfilePageViewModel CreateEditProfilePageViewModel(VianigramCompositionRoot c)
        {
            IAccountApi account = null;
            IMediaApi media = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IAccountApi>(out account);
                c.TryResolve<IMediaApi>(out media);
                c.TryResolve<INavigationService>(out nav);
            }
            return new EditProfilePageViewModel(account, media, nav);
        }

        public static GroupInfoPageViewModel CreateGroupInfoPageViewModel(VianigramCompositionRoot c)
        {
            IChatsApi chats = null;
            INotificationsApi notifications = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IChatsApi>(out chats);
                c.TryResolve<INotificationsApi>(out notifications);
                c.TryResolve<INavigationService>(out nav);
            }
            return new GroupInfoPageViewModel(chats, notifications, nav);
        }

        public static ContactsPageViewModel CreateContactsPageViewModel(VianigramCompositionRoot c)
        {
            IContactsApi contacts = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IContactsApi>(out contacts);
                c.TryResolve<INavigationService>(out nav);
            }
            return new ContactsPageViewModel(contacts, nav);
        }

        public static BlockedUsersPageViewModel CreateBlockedUsersPageViewModel(VianigramCompositionRoot c)
        {
            IContactsApi contacts = null;
            if (c != null) c.TryResolve<IContactsApi>(out contacts);
            return new BlockedUsersPageViewModel(contacts);
        }
    }
}
