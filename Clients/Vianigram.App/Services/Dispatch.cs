// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Dispatch.cs
//
// Tiny helper to marshal work back to the UI thread. ViewModels call this
// from event handlers that arrive on a non-UI thread (IChatsApi.DialogChanged
// and IMessagesApi.MessagesChanged are raised on the publisher's thread per
// IEventBus contract). Keep the implementation no-op when already on the UI
// thread to avoid an unnecessary dispatcher round-trip.
//
// Delegates to a process-static IUiDispatcher when one is registered.
// Tests / smoke runs / background tasks register an InlineUiDispatcher;
// the foreground host registers a WindowsPhoneUiDispatcher in
// App.OnLaunched. If no dispatcher is registered we fall back to the
// CoreDispatcher logic.

using System;
using System.Threading.Tasks;
using Vianigram.Kernel.Concurrency;
using Windows.ApplicationModel.Core;
using Windows.UI.Core;

namespace Vianigram.App.Services
{
    public static class Dispatch
    {
        private static IUiDispatcher _registered;

        /// <summary>
        /// Registers the process-wide UI dispatcher. Called once from
        /// <c>App.OnLaunched</c> with a <see cref="WindowsPhoneUiDispatcher"/>.
        /// Tests can override with <see cref="InlineUiDispatcher"/>.
        /// </summary>
        public static void Register(IUiDispatcher dispatcher)
        {
            _registered = dispatcher;
        }

        /// <summary>The currently registered dispatcher, or null if none.</summary>
        public static IUiDispatcher Current { get { return _registered; } }

        /// <summary>
        /// Run <paramref name="action"/> on the UI dispatcher. Safe to call
        /// from any thread; if no dispatcher is available the action runs
        /// inline.
        /// </summary>
        public static Task OnUiAsync(Action action)
        {
            if (action == null) return CompletedTask;

            IUiDispatcher dispatcher = _registered;
            if (dispatcher != null)
            {
                return dispatcher.RunOnUiAsync(action);
            }

            return LegacyOnUiAsync(action);
        }

        // Legacy path — preserved for the "no-one called Register yet"
        // window during cold start. Behaviour identical to the original
        // pre-Task-7 implementation.
        private static async Task LegacyOnUiAsync(Action action)
        {
            CoreDispatcher dispatcher = null;
            try
            {
                CoreApplicationView view = CoreApplication.MainView;
                if (view != null && view.CoreWindow != null) dispatcher = view.CoreWindow.Dispatcher;
            }
            catch
            {
                dispatcher = null;
            }

            if (dispatcher == null || dispatcher.HasThreadAccess)
            {
                try { action(); } catch { }
                return;
            }

            await dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                try { action(); } catch { }
            }).AsTask().ConfigureAwait(false);
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
