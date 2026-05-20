// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// AppServices.cs
//
// Tiny façade over the composition root. Pages and ViewModels never call
// VianigramCompositionRoot.Resolve directly — they go through here so the
// auth-state probe and degraded-mode fallbacks live in one place.

using System;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Account.Ports.Inbound;
using Vianigram.App.Pages;
using Vianigram.Composition.Roots;
using Vianigram.Kernel.Logging;

namespace Vianigram.App.Services
{
    public static class AppServices
    {
        /// <summary>
        /// Probe the composition root for an authenticated session. If the
        /// Account context is wired and reports <see cref="AuthState.AuthStateKind.Authorized"/>,
        /// route to the chat list. Otherwise route to the welcome screen.
        /// </summary>
        public static Type PickInitialPage(VianigramCompositionRoot composition)
        {
            if (composition == null) return typeof(WelcomePage);

            IComponentLogger log = AppLog.For("App.Services");
            try
            {
                IAccountApi account;
                if (!composition.TryResolve<IAccountApi>(out account) || account == null)
                {
                    log.Warn("IAccountApi not registered; defaulting to WelcomePage.");
                    return typeof(WelcomePage);
                }

                var snapshot = account.CurrentState;
                bool isAuthorized = (snapshot != null) && (snapshot.StateKind == AuthState.AuthStateKind.Authorized);
                return isAuthorized ? typeof(ChatListPage) : typeof(WelcomePage);
            }
            catch (Exception ex)
            {
                log.Error("PickInitialPage failed: " + ex);
                return typeof(WelcomePage);
            }
        }

        /// <summary>
        /// Convenience: pull the running <see cref="App"/> singleton.
        /// </summary>
        public static App Current
        {
            get { return (App)Windows.UI.Xaml.Application.Current; }
        }
    }
}
