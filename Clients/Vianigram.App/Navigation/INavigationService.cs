// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// INavigationService.cs
//
// App-layer navigation contract injected into page ViewModels so command
// bodies can request a Frame transition without taking a direct dependency
// on Windows.UI.Xaml.Controls.Frame.

using System;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Navigation
{
    public interface INavigationService
    {
        bool CanGoBack { get; }
        bool NavigateTo(Route route);
        bool NavigateTo(Route route, object parameter);
        void GoBack();

        /// <summary>App.xaml.cs binds the rootFrame on first launch.</summary>
        void Initialize(Frame rootFrame);

        /// <summary>
        /// Drop every cached page from the navigation frame's LRU pool.
        /// Used on logout / account
        /// switch so the new session doesn't inherit a ChatListPage
        /// (NavigationCacheMode.Required) or ChatPage
        /// (NavigationCacheMode.Enabled) instance whose ViewModel
        /// still subscribes to the previous session's IEventBus and
        /// renders the old user's dialogs.
        /// </summary>
        void ClearCache();
    }
}
