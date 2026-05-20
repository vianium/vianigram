// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// App.xaml.cs — Vianigram.SmokeRunner.App entry point.
//
// This is the WP8.1 host shell for the Vianigram smoke-test suite.
// Responsibility: stand up a Frame, navigate to MainPage, and let the user
// (or auto-run logic) trigger SmokeTestRunner.RunAllAsync(). Nothing more —
// no DI container, no logging plumbing, no business state. Keep it bare.

using System;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Vianigram.Kernel.Logging;
using Vianigram.SmokeTests;

namespace Vianigram.SmokeRunner.App
{
    public sealed partial class App : Application
    {
        public App()
        {
            InitializeComponent();
            Suspending += OnSuspending;
            UnhandledException += (sender, args) =>
            {
                try
                {
                    EarlyLog.Write("SmokeRunner.App", "UnhandledException: " + args.Message);
                    if (args.Exception != null)
                        EarlyLog.Write("SmokeRunner.App", "Exception: " + args.Exception);
                }
                catch
                {
                    // Last-chance handler must never throw.
                }
            };
        }

        protected override void OnLaunched(LaunchActivatedEventArgs e)
        {
            try
            {
                EarlyLog.Write("SmokeRunner.App", "OnLaunched begin");

                var rootFrame = Window.Current.Content as Frame;
                if (rootFrame == null)
                {
                    rootFrame = new Frame();
                    rootFrame.NavigationFailed += OnNavigationFailed;
                    Window.Current.Content = rootFrame;
                }

                if (rootFrame.Content == null)
                    rootFrame.Navigate(typeof(MainPage), e != null ? e.Arguments : null);

                Window.Current.Activate();
                EarlyLog.Write("SmokeRunner.App", "Window activated");
            }
            catch (Exception ex)
            {
                EarlyLog.Write("SmokeRunner.App", "OnLaunched FATAL: " + ex);
                throw;
            }
        }

        private void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
        {
            EarlyLog.Write("SmokeRunner.App", "Navigation failed for " +
                (e.SourcePageType != null ? e.SourcePageType.FullName : "(null)") +
                " — Exception: " + (e.Exception != null ? e.Exception.ToString() : "(none)"));
            throw new InvalidOperationException("Failed to load Page " + e.SourcePageType.FullName, e.Exception);
        }

        private void OnSuspending(object sender, SuspendingEventArgs e)
        {
            // No persistent state. The runner is stateless across launches.
            var deferral = e.SuspendingOperation.GetDeferral();
            try
            {
                SmokeTestRunner.ShutdownWarmResources();
                EarlyLog.Write("SmokeRunner.App", "OnSuspending");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
