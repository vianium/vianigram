// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;
using Windows.System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using Vianigram.Kernel.Logging;

namespace Vianigram.App.Pages
{
    /// <summary>
    /// Terminal page shown when <see cref="BuildExpiry.IsExpired"/>
    /// returns true at startup. Replaces the entire navigation root —
    /// the welcome screen / chat list / settings / profile / calls are
    /// unreachable until the user installs a fresh build.
    ///
    /// All static copy is x:Uid-bound to <c>ExpiredPage_*</c> keys in
    /// <c>Strings/&lt;locale&gt;/Resources.resw</c>. The only dynamic
    /// content is the expiration date itself, formatted in the user's
    /// current culture and assigned to the <c>ExpirationDateText</c>
    /// TextBlock in <see cref="OnNavigatedTo"/>.
    /// </summary>
    public sealed partial class ExpiredPage : Page
    {
        // Brand handles — single edit point for both this page and any
        // future "about" / settings screen that wants to surface the
        // download channel.
        private const string TelegramUri = "https://t.me/PivoraApps";
        private const string DonateUri   = "https://buymeacoffee.com/soyangelcareaga";

        public ExpiredPage()
        {
            InitializeComponent();
            EarlyLog.Write("ExpiredPage", "ctor");
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            EarlyLog.Write("ExpiredPage", "OnNavigatedTo mode=" +
                (e == null ? "null" : e.NavigationMode.ToString()));

            // Resolve the expiration date in the user's current culture.
            // We pull CurrentCulture (NOT CurrentUICulture) because the
            // user's BCP47 override at startup already set the UI thread
            // culture; CurrentCulture matches the locale the rest of
            // the page renders in.
            try
            {
                ExpirationDateText.Text =
                    BuildExpiry.GetLocalExpirationString(CultureInfo.CurrentCulture);
            }
            catch (Exception ex)
            {
                EarlyLog.Write("ExpiredPage",
                    "expiration date format failed: " + ex.Message);
                // Fall back to ISO 8601 — never leave the row empty.
                ExpirationDateText.Text =
                    BuildExpiry.ExpiresAtUtc.ToString(
                        "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        // -----------------------------------------------------------------
        // Tap handlers
        // -----------------------------------------------------------------

        private async void OnTelegramClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri(TelegramUri));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("ExpiredPage", "telegram launch failed: " + ex.Message);
            }
        }

        private async void OnDonateClicked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            try
            {
                await Launcher.LaunchUriAsync(new Uri(DonateUri));
            }
            catch (Exception ex)
            {
                EarlyLog.Write("ExpiredPage", "donate launch failed: " + ex.Message);
            }
        }
    }
}
