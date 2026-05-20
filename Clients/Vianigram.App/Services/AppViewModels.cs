// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppViewModels.cs
//
// App-layer view-model factory. Pages call AppViewModels.CreateXxxPageViewModel()
// from their OnNavigatedTo to obtain a fully-wired VM resolved against the
// VianigramCompositionRoot. Split into partial files grouped by feature
// cluster.

using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Navigation;
using Vianigram.App.ViewModels;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Composition.Roots;
using Vianigram.Kernel.Events;
using Vianigram.Messages.Ports.Inbound;

namespace Vianigram.App.Services
{
    public static partial class AppViewModels
    {
        private static VianigramCompositionRoot _root;

        /// <summary>
        /// Wires the singleton composition root used by every Create* factory.
        /// Called once from App.xaml.cs after BuildPhase2Async returns.
        /// </summary>
        public static void Initialize(VianigramCompositionRoot root)
        {
            _root = root;
        }

        /// <summary>Composition root configured by <see cref="Initialize"/>; null in degraded mode.</summary>
        public static VianigramCompositionRoot Composition
        {
            get { return _root; }
        }

        // ---- Page view-model factories ----
        // These mirror the inline construction in the page code-behinds
        // (WelcomePage / PhoneNumberPage / SmsCodePage / ChatListPage / ChatPage).
        // The pages have not been migrated to the unified pattern yet — these
        // factories let new code resolve the same VMs through the AppViewModels
        // surface.

        public static WelcomePageViewModel CreateWelcomePageViewModel()
        {
            INavigationService nav = null;
            if (_root != null) _root.TryResolve<INavigationService>(out nav);
            return new WelcomePageViewModel(nav);
        }

        public static PhoneNumberPageViewModel CreatePhoneNumberPageViewModel()
        {
            IAccountApi account = null;
            INavigationService nav = null;
            if (_root != null)
            {
                _root.TryResolve<IAccountApi>(out account);
                _root.TryResolve<INavigationService>(out nav);
            }
            return new PhoneNumberPageViewModel(account, nav);
        }

        public static LanguagePickerPageViewModel CreateLanguagePickerPageViewModel()
        {
            INavigationService nav = null;
            if (_root != null) _root.TryResolve<INavigationService>(out nav);
            return new LanguagePickerPageViewModel(nav);
        }

        public static CountryPickerPageViewModel CreateCountryPickerPageViewModel()
        {
            INavigationService nav = null;
            if (_root != null) _root.TryResolve<INavigationService>(out nav);
            return new CountryPickerPageViewModel(nav);
        }

        public static SmsCodePageViewModel CreateSmsCodePageViewModel(string phoneNumber)
        {
            IAccountApi account = null;
            INavigationService nav = null;
            if (_root != null)
            {
                _root.TryResolve<IAccountApi>(out account);
                _root.TryResolve<INavigationService>(out nav);
            }
            return new SmsCodePageViewModel(account, phoneNumber, nav);
        }

        public static ChatListPageViewModel CreateChatListPageViewModel()
        {
            IChatsApi chats = null;
            IEventBus bus = null;
            if (_root != null)
            {
                _root.TryResolve<IChatsApi>(out chats);
                _root.TryResolve<IEventBus>(out bus);
            }
            return new ChatListPageViewModel(chats, bus);
        }

        public static ChatPageViewModel CreateChatPageViewModel(string peerKey, string peerTitle)
        {
            IMessagesApi messages = null;
            if (_root != null) _root.TryResolve<IMessagesApi>(out messages);
            return new ChatPageViewModel(messages, peerKey, peerTitle);
        }
    }
}
