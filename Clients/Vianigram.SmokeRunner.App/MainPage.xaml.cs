// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MainPage.xaml.cs — UI for the Vianigram smoke-test runner.
//
// The page is a thin shell over Vianigram.SmokeTests.SmokeTestRunner. It
// exposes one button ("Run smoke tests") and renders the resulting
// TestSummary as a ListView of TestRow items. The "Include live test"
// toggle filters the "Live" suite out for offline runs.
//
// No business logic lives here — everything substantial is in
// Vianigram.SmokeTests. Keep this code-behind dumb.

using System;
using System.Threading;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Vianigram.Kernel.Logging;
using Vianigram.SmokeTests;

namespace Vianigram.SmokeRunner.App
{
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            SmokeTestRunner.BeginWarmUp();
            // Intentionally not auto-running: we want the user to see the UI
            // and pick whether the live DC test should participate.
        }

        private async void RunButton_Click(object sender, RoutedEventArgs e)
        {
            RunButton.IsEnabled = false;
            StatusText.Text = "Running...";
            ResultsList.Items.Clear();
            SummaryText.Text = "";

            try
            {
                bool includeLive = LiveTestToggle.IsChecked.HasValue ? LiveTestToggle.IsChecked.Value : true;
                EarlyLog.Write("SmokeRunner", "RunAllAsync begin includeLive=" + includeLive);

                var summary = await SmokeTestRunner.RunAllAsync(default(CancellationToken)).ConfigureAwait(true);

                int displayed = 0;
                int totalElapsedMs = 0;
                int passDisplayed = 0;
                int failDisplayed = 0;
                int skippedDisplayed = 0;

                foreach (var entry in summary.Entries)
                {
                    if (entry == null)
                        continue;
                    if (!includeLive && string.Equals(entry.Suite, "Live", StringComparison.OrdinalIgnoreCase))
                        continue;

                    int elapsedMs = (int)entry.Elapsed.TotalMilliseconds;
                    totalElapsedMs += elapsedMs;
                    displayed++;
                    if (entry.Skipped) skippedDisplayed++;
                    else if (entry.Passed) passDisplayed++;
                    else failDisplayed++;

                    ResultsList.Items.Add(new TestRow
                    {
                        Suite = entry.Suite ?? "?",
                        Name = entry.Name ?? "(unnamed)",
                        Passed = entry.Passed,
                        Skipped = entry.Skipped,
                        Detail = entry.Detail ?? string.Empty,
                        ElapsedMs = elapsedMs
                    });
                }

                SummaryText.Text = string.Format("{0} passed / {1} failed / {2} skipped / {3} total in {4} ms",
                    passDisplayed, failDisplayed, skippedDisplayed, displayed, totalElapsedMs);

                bool overallPass = (failDisplayed == 0) && (displayed > 0);
                StatusText.Text = overallPass ? "Done - no failures" : "Done - failures present";
                StatusText.Foreground = new SolidColorBrush(
                    overallPass
                        ? Color.FromArgb(0xFF, 0x3F, 0xB9, 0x50)   // green
                        : Color.FromArgb(0xFF, 0xF8, 0x51, 0x49)); // red
            }
            catch (Exception ex)
            {
                StatusText.Text = "Crashed: " + ex.GetType().Name + ": " + ex.Message;
                StatusText.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0xF8, 0x51, 0x49));
                EarlyLog.Write("SmokeRunner", ex.ToString());
            }
            finally
            {
                RunButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// View-model row backing each ListView item. Kept public+sealed for
        /// XAML binding — .NET Native (Release) requires types referenced by
        /// templates to be visible in Default.rd.xml.
        /// </summary>
        public sealed class TestRow
        {
            public string Suite { get; set; }
            public string Name { get; set; }
            public bool Passed { get; set; }
            public bool Skipped { get; set; }
            public string Detail { get; set; }
            public int ElapsedMs { get; set; }

            public string Header
            {
                get
                {
                    return (Skipped ? "SKIP" : (Passed ? "PASS" : "FAIL")) + " / " + Suite + " / " + Name +
                           " / " + ElapsedMs + " ms";
                }
            }

            public SolidColorBrush HeaderBrush
            {
                get
                {
                    return new SolidColorBrush(
                        Passed
                            ? Color.FromArgb(0xFF, 0x3F, 0xB9, 0x50)
                            : Skipped
                                ? Color.FromArgb(0xFF, 0x8B, 0x94, 0x9E)
                                : Color.FromArgb(0xFF, 0xF8, 0x51, 0x49));
                }
            }

            public Visibility HasDetail
            {
                get { return string.IsNullOrEmpty(Detail) ? Visibility.Collapsed : Visibility.Visible; }
            }
        }
    }
}
