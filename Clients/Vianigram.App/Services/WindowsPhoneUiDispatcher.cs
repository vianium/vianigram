// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// WindowsPhoneUiDispatcher.cs — Vianigram.App.Services
//
// IUiDispatcher implementation backed by the platform CoreDispatcher. Used
// in the foreground app process so VMs subscribed via
// IEventBus.SubscribeOnUi(...) automatically marshal back to the UI thread
// before mutating XAML-bound state.
//
// In WP 8.1 the dispatcher reachable via CoreApplication.MainView may be
// null briefly during cold start (between OnLaunched and Window.Activate).
// We defensively probe it on every call and fall through to inline
// execution if nothing is available — that's the same compromise the old
// Dispatch.OnUiAsync helper made.

using System;
using System.Threading.Tasks;
using Vianigram.Kernel.Concurrency;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace Vianigram.App.Services
{
    public sealed class WindowsPhoneUiDispatcher : IUiDispatcher
    {
        public bool HasUiThreadAccess
        {
            get
            {
                CoreDispatcher d = ResolveDispatcher();
                return d == null || d.HasThreadAccess;
            }
        }

        public Task RunOnUiAsync(Action action)
        {
            if (action == null) return CompletedTask;

            CoreDispatcher dispatcher = ResolveDispatcher();
            if (dispatcher == null || dispatcher.HasThreadAccess)
            {
                try { action(); }
                catch
                {
                    // Match IEventBus exception-swallow contract.
                }
                return CompletedTask;
            }

            return dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try { action(); }
                catch
                {
                    // Match IEventBus exception-swallow contract.
                }
            }).AsTask();
        }

        private static CoreDispatcher ResolveDispatcher()
        {
            try
            {
                CoreApplicationView view = CoreApplication.MainView;
                if (view == null) return null;
                CoreWindow window = view.CoreWindow;
                if (window == null) return null;
                return window.Dispatcher;
            }
            catch
            {
                return null;
            }
        }

        private static readonly Task CompletedTask = MakeCompleted();
        private static Task MakeCompleted()
        {
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);
            return tcs.Task;
        }
    }
}
