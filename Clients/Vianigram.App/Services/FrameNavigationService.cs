// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// FrameNavigationService.cs
//
// SUPERSEDED: Use Vianigram.App.Navigation.NavigationService and the
// INavigationService interface. App.xaml.cs no longer instantiates this
// type, but the file is retained until any remaining call sites are
// migrated. New code should not depend on this class.
//
// Thin wrapper around the root <c>Frame</c> exposed to ViewModels so that
// they can request a page transition without taking a direct dependency on
// <c>Windows.UI.Xaml.Controls.Frame</c>. Keeping this in the app project
// is intentional — it could eventually migrate to a port in
// Vianigram.Shell.Navigation, but a local impl is enough for now and keeps
// wiring obvious.

using System;
using Windows.UI.Xaml.Controls;

namespace Vianigram.App.Services
{
    public sealed class FrameNavigationService
    {
        private readonly Frame _frame;

        public FrameNavigationService(Frame frame)
        {
            if (frame == null) throw new ArgumentNullException("frame");
            _frame = frame;
        }

        public bool Navigate(Type pageType)
        {
            if (pageType == null) throw new ArgumentNullException("pageType");
            return _frame.Navigate(pageType);
        }

        public bool Navigate(Type pageType, object parameter)
        {
            if (pageType == null) throw new ArgumentNullException("pageType");
            return _frame.Navigate(pageType, parameter);
        }

        public bool CanGoBack
        {
            get { return _frame.CanGoBack; }
        }

        public void GoBack()
        {
            if (_frame.CanGoBack) _frame.GoBack();
        }
    }
}
