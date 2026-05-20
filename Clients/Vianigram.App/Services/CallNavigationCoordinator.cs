// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// CallNavigationCoordinator.cs
//
// App-level bridge from Calls domain events to the foreground call UI. The
// Calls bounded context remains presentation-agnostic; this small shell service
// decides when an incoming call should take over the current frame.

using System;
using Vianigram.App.Navigation;
using Vianigram.App.Pages.Calls;
using Vianigram.Calls.Domain.Events;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Logging;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Services
{
    public sealed class CallNavigationCoordinator : IDisposable
    {
        private readonly INavigationService _nav;
        private readonly IComponentLogger _log;
        private readonly IDisposable _incomingSub;
        private int _disposed;

        public CallNavigationCoordinator(IEventBus bus, INavigationService nav, IComponentLogger log)
        {
            if (bus == null) throw new ArgumentNullException("bus");
            if (nav == null) throw new ArgumentNullException("nav");

            _nav = nav;
            _log = log;
            _incomingSub = bus.Subscribe<CallReceived>(OnCallReceived);

            if (_log != null) _log.Info("call navigation coordinator attached");
        }

        public void Dispose()
        {
            if (_disposed != 0) return;
            _disposed = 1;
            try
            {
                if (_incomingSub != null) _incomingSub.Dispose();
            }
            catch
            {
            }
        }

        private void OnCallReceived(CallReceived e)
        {
            if (e == null) return;

            var ignore = Dispatch.OnUiAsync(delegate
            {
                try
                {
                    if (IsCallSurfaceVisible()) return;

                    bool navigated = _nav.NavigateTo(Route.IncomingCall, e.CallId);
                    if (_log != null)
                    {
                        _log.Info("incoming call navigation callId=" + e.CallId
                            + " from=" + e.FromUserId
                            + " navigated=" + navigated);
                    }
                }
                catch (Exception ex)
                {
                    if (_log != null)
                        _log.Warn("incoming call navigation failed: " + ex.GetType().Name + ": " + ex.Message);
                }
            });
        }

        private static bool IsCallSurfaceVisible()
        {
            try
            {
                Frame frame = Window.Current == null ? null : Window.Current.Content as Frame;
                if (frame == null) return false;
                return frame.Content is IncomingCallPage || frame.Content is CallPage;
            }
            catch
            {
                return false;
            }
        }
    }
}
