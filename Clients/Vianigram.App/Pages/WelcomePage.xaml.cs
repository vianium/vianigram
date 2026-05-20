// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// WelcomePage.xaml.cs — code-behind only handles plumbing.
//
// Resolves WelcomePageViewModel through the AppViewModels factory, starts
// the hero pulse animation, and switches between the Wide / Compact visual
// states as the window is resized so the layout adapts from WVGA up to
// 1080p phones and small tablets without breaking. All click handling
// lives in the VM via ICommand.

using Vianigram.App.Services;
using Vianigram.App.ViewModels.Pages;
using Vianigram.Kernel.Logging;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media.Animation;
using Windows.UI.Xaml.Navigation;

namespace Vianigram.App.Pages
{
    public sealed partial class WelcomePage : Page
    {
        // Threshold (effective DIPs) below which the layout shrinks the hero
        // so the marketing copy still fits comfortably. Tuned for qHD-class
        // screens which expose ~360×640 effective pixels.
        private const double CompactHeightThreshold = 700.0;
        private const double CompactWidthThreshold = 360.0;

        private WelcomePageViewModel _vm;

        public WelcomePage()
        {
            EarlyLog.Write("Boot", "WelcomePage ctor begin");
            InitializeComponent();
            EarlyLog.Write("Boot", "WelcomePage ctor end");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (_vm == null)
            {
                _vm = AppViewModels.CreateWelcomePageViewModel();
                DataContext = _vm;
            }

            // Kick off the ambient pulse animation. Failure to find the
            // storyboard is non-fatal — the page remains usable without
            // motion.
            var pulse = Resources["HeroPulse"] as Storyboard;
            if (pulse != null) pulse.Begin();

            // Subscribe to size changes so we can flip between the layout
            // states (Wide ↔ Compact). Apply once now so the initial state
            // matches the current window size.
            Window.Current.SizeChanged += OnWindowSizeChanged;
            ApplyLayoutState(Window.Current.Bounds.Width, Window.Current.Bounds.Height);
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            Window.Current.SizeChanged -= OnWindowSizeChanged;
            base.OnNavigatedFrom(e);
        }

        private void OnWindowSizeChanged(object sender, WindowSizeChangedEventArgs e)
        {
            ApplyLayoutState(e.Size.Width, e.Size.Height);
        }

        private void ApplyLayoutState(double width, double height)
        {
            string state = (height < CompactHeightThreshold || width < CompactWidthThreshold)
                ? "Compact"
                : "Wide";
            VisualStateManager.GoToState(this, state, useTransitions: false);
        }
    }
}
