// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// NavigationService.cs
//
// Concrete INavigationService implementation. Owns the root Frame reference
// and projects each Route enum value to its Pages.<Sub>.<Page> Type via the
// static BuildMap dictionary.

using System;
using System.Collections.Generic;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Navigation
{
    public sealed class NavigationService : INavigationService
    {
        private static readonly Dictionary<Route, Type> _map = BuildMap();

        private Frame _frame;

        public void Initialize(Frame rootFrame)
        {
            if (rootFrame == null) throw new ArgumentNullException("rootFrame");
            _frame = rootFrame;
        }

        public bool CanGoBack
        {
            get { return _frame != null && _frame.CanGoBack; }
        }

        public bool NavigateTo(Route route)
        {
            return NavigateTo(route, null);
        }

        public bool NavigateTo(Route route, object parameter)
        {
            if (_frame == null) return false;
            Type pageType;
            if (!_map.TryGetValue(route, out pageType)) return false;
            return _frame.Navigate(pageType, parameter);
        }

        public void GoBack()
        {
            if (_frame != null && _frame.CanGoBack) _frame.GoBack();
        }

        public void ClearCache()
        {
            if (_frame == null) return;
            // The canonical WP 8.1 trick: setting CacheSize = 0 evicts
            // every page (Required + Enabled) from the navigation
            // cache. Restoring the previous value re-enables caching
            // for the subsequent flow. Frame.BackStack.Clear() doesn't
            // touch the cache pool, so it can't be used here on its
            // own. We also clear back-stack to prevent the user from
            // navigating back into a stale page tree.
            int previous = _frame.CacheSize;
            try
            {
                _frame.CacheSize = 0;
                _frame.CacheSize = previous > 0 ? previous : 6;
            }
            catch { /* best-effort — never crash logout */ }
            try
            {
                _frame.BackStack.Clear();
                _frame.ForwardStack.Clear();
            }
            catch { }
        }

        private static Dictionary<Route, Type> BuildMap()
        {
            var map = new Dictionary<Route, Type>();

            // Already-wired:
            map[Route.Welcome]         = typeof(Pages.WelcomePage);
            map[Route.PhoneNumber]     = typeof(Pages.PhoneNumberPage);
            map[Route.SmsCode]         = typeof(Pages.SmsCodePage);
            map[Route.ChatList]        = typeof(Pages.ChatListPage);
            map[Route.Chat]            = typeof(Pages.ChatPage);
            map[Route.LanguagePicker]  = typeof(Pages.LanguagePickerPage);
            map[Route.CountryPicker]   = typeof(Pages.CountryPickerPage);

            // Auth:
            map[Route.QrLogin]         = typeof(Pages.Auth.QrLoginPage);
            map[Route.AccountSwitcher] = typeof(Pages.Auth.AccountSwitcherPage);
            map[Route.Passcode]        = typeof(Pages.Auth.PasscodePage);
            map[Route.TwoFaPassword]   = typeof(Pages.Auth.TwoFaPasswordPage);
            map[Route.SignUp]          = typeof(Pages.Auth.SignUpPage);
            map[Route.ProxySettings]   = typeof(Pages.Auth.ProxySettingsPage);

            // Profile:
            map[Route.Profile]         = typeof(Pages.Profile.ProfilePage);
            map[Route.EditProfile]     = typeof(Pages.Profile.EditProfilePage);
            map[Route.GroupInfo]       = typeof(Pages.Profile.GroupInfoPage);
            map[Route.Contacts]        = typeof(Pages.Profile.ContactsPage);
            map[Route.BlockedUsers]    = typeof(Pages.Profile.BlockedUsersPage);

            // Settings:
            map[Route.Settings]        = typeof(Pages.Settings.SettingsPage);
            map[Route.ActiveSessions]  = typeof(Pages.Settings.ActiveSessionsPage);
            map[Route.Scheduled]       = typeof(Pages.Settings.ScheduledPage);
            map[Route.Search]          = typeof(Pages.Settings.SearchPage);

            // Compose:
            map[Route.NewChat]         = typeof(Pages.Compose.NewChatPage);
            map[Route.NewChannel]      = typeof(Pages.Compose.NewChannelPage);
            map[Route.Forward]         = typeof(Pages.Compose.ForwardPage);
            map[Route.Poll]            = typeof(Pages.Compose.PollPage);

            // Media:
            map[Route.MediaViewer]     = typeof(Pages.Media.MediaViewerPage);

            // Calls:
            map[Route.Calls]           = typeof(Pages.Calls.CallsPage);
            map[Route.Call]            = typeof(Pages.Calls.CallPage);
            map[Route.IncomingCall]    = typeof(Pages.Calls.IncomingCallPage);

            // Secret:
            map[Route.SecretChat]      = typeof(Pages.Secret.SecretChatPage);
            map[Route.KeyFingerprint]  = typeof(Pages.Secret.KeyFingerprintPage);

            // Topics:
            map[Route.Topics]          = typeof(Pages.TopicsPage);

            return map;
        }
    }
}
