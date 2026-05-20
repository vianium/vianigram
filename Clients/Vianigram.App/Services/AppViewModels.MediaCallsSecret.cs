// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppViewModels.MediaCallsSecret.cs
//
// Partial owning the Create<Xxx>PageViewModel factories for the
// Media + Calls + Secret + Topics cluster (MediaViewer, Call, IncomingCall,
// SecretChat, KeyFingerprint, Topics).

using Vianigram.App.Navigation;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Calls.Ports.Inbound;
using Vianigram.Chats.Ports.Inbound;
using Vianigram.Composition.Infrastructure;
using Vianigram.Composition.Roots;
using Vianigram.Media.Ports.Inbound;
using Vianigram.SecretChats.Ports.Inbound;

namespace Vianigram.App.Services
{
    public static partial class AppViewModels
    {
        public static MediaViewerPageViewModel CreateMediaViewerPageViewModel(VianigramCompositionRoot c)
        {
            IMediaApi media = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IMediaApi>(out media);
                c.TryResolve<INavigationService>(out nav);
            }
            return new MediaViewerPageViewModel(media, nav);
        }

        public static CallPageViewModel CreateCallPageViewModel(VianigramCompositionRoot c)
        {
            ICallsApi calls = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<ICallsApi>(out calls);
                c.TryResolve<INavigationService>(out nav);
            }
            return new CallPageViewModel(calls, nav);
        }

        public static CallsPageViewModel CreateCallsPageViewModel(VianigramCompositionRoot c)
        {
            ICallsApi calls = null;
            INavigationService nav = null;
            IPeerCache peerCache = null;
            if (c != null)
            {
                c.TryResolve<ICallsApi>(out calls);
                c.TryResolve<INavigationService>(out nav);
                c.TryResolve<IPeerCache>(out peerCache);
            }
            return new CallsPageViewModel(calls, nav, peerCache);
        }

        public static IncomingCallPageViewModel CreateIncomingCallPageViewModel(VianigramCompositionRoot c)
        {
            ICallsApi calls = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<ICallsApi>(out calls);
                c.TryResolve<INavigationService>(out nav);
            }
            return new IncomingCallPageViewModel(calls, nav);
        }

        public static SecretChatPageViewModel CreateSecretChatPageViewModel(VianigramCompositionRoot c)
        {
            ISecretChatsApi secret = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<ISecretChatsApi>(out secret);
                c.TryResolve<INavigationService>(out nav);
            }
            return new SecretChatPageViewModel(secret, nav);
        }

        public static KeyFingerprintPageViewModel CreateKeyFingerprintPageViewModel(VianigramCompositionRoot c)
        {
            ISecretChatsApi secret = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<ISecretChatsApi>(out secret);
                c.TryResolve<INavigationService>(out nav);
            }
            return new KeyFingerprintPageViewModel(secret, nav);
        }

        public static TopicsPageViewModel CreateTopicsPageViewModel(VianigramCompositionRoot c)
        {
            IChatsApi chats = null;
            INavigationService nav = null;
            if (c != null)
            {
                c.TryResolve<IChatsApi>(out chats);
                c.TryResolve<INavigationService>(out nav);
            }
            return new TopicsPageViewModel(chats, nav);
        }
    }
}
