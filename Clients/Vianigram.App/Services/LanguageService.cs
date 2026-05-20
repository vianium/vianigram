// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LanguageService.cs
//
// Owns the active UI language. Stores the user's choice in LocalSettings so
// it survives restarts, sets the WinRT
// Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride at
// startup, and exposes the catalogue of supported languages to the picker
// UI.
//
// To add a new language: drop a Strings/<lang-tag>/Resources.resw beside
// the existing ones, list the language in Package.appxmanifest under
// <Resources>, and append a LanguageOption entry to SupportedLanguages.

using System.Collections.Generic;
using Windows.ApplicationModel.Resources.Core;
using Windows.Globalization;
using Windows.Storage;

namespace Vianigram.App.Services
{
    public sealed class LanguageOption
    {
        public string Code { get; set; }         // e.g. "en-US", "es-ES"
        public string NativeName { get; set; }   // e.g. "English", "Español"
        public string EnglishName { get; set; }  // e.g. "English", "Spanish"
    }

    public static class LanguageService
    {
        private const string SettingsKey = "LanguageOverride";
        private const string DefaultLanguage = "en-US";

        /// <summary>
        /// All languages the app ships with. Order matters — it's the order
        /// shown in the picker.
        /// </summary>
        public static readonly List<LanguageOption> SupportedLanguages = new List<LanguageOption>
        {
            new LanguageOption { Code = "en-US", NativeName = "English", EnglishName = "English" },
            new LanguageOption { Code = "es-ES", NativeName = "Español", EnglishName = "Spanish" },
        };

        /// <summary>
        /// Currently-active language tag. Reflects PrimaryLanguageOverride
        /// when the user has explicitly chosen one, else the default ship
        /// language.
        /// </summary>
        public static string CurrentCode
        {
            get
            {
                string s = ApplicationLanguages.PrimaryLanguageOverride;
                if (string.IsNullOrEmpty(s)) return DefaultLanguage;
                return s;
            }
        }

        /// <summary>
        /// Restore a previously-saved language choice from LocalSettings.
        /// Call from App.OnLaunched before the first Frame.Navigate so the
        /// initial page renders in the correct language.
        /// </summary>
        public static void LoadFromSettings()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                object raw;
                if (!settings.Values.TryGetValue(SettingsKey, out raw)) return;
                string saved = raw as string;
                if (string.IsNullOrEmpty(saved)) return;
                ApplicationLanguages.PrimaryLanguageOverride = saved;
            }
            catch
            {
                // Resource access failures must never crash the boot path —
                // worst case the user sees the default language.
            }
        }

        /// <summary>
        /// Apply <paramref name="code"/> as the new active language and
        /// persist the choice. Also resets the WinRT ResourceContext caches
        /// so subsequent x:Uid lookups and ResourceLoader.GetString calls
        /// hit the new resource map instead of the previously-cached one
        /// (without this, navigating to a freshly-constructed page after
        /// an Apply still renders the old language until the SECOND apply).
        /// The caller is still responsible for forcing a visual refresh —
        /// re-navigating to the welcome page does the trick.
        /// </summary>
        public static void Apply(string code)
        {
            if (string.IsNullOrEmpty(code)) return;
            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = code;
                var settings = ApplicationData.Current.LocalSettings;
                if (settings != null) settings.Values[SettingsKey] = code;
            }
            catch
            {
                // Storage failure is non-fatal — the in-memory override still
                // applied, the user just won't keep the choice across launches.
            }

            // Invalidate the resource caches. View-independent first (covers
            // imperative Strings.Get from VMs / services), then per-view
            // (covers x:Uid resolution on the rendered tree).
            try { ResourceContext.GetForViewIndependentUse().Reset(); } catch { }
            try { ResourceContext.GetForCurrentView().Reset(); } catch { }
        }
    }
}
