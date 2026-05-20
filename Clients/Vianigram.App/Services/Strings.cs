// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// Strings.cs
//
// Thin static facade over Windows.ApplicationModel.Resources.ResourceLoader.
// View-models and code-behind that need a localized string by key go through
// here so we have one swallow point for the lookup miss (returning the key
// itself) and one place to swap out the loader (e.g. for tests).
//
// XAML pages bind localized text via x:Uid (the native WP/UWP pattern); this
// helper exists for the imperative path only (error messages composed in C#).
//
// Important: the loader is intentionally NOT cached. ResourceLoader is cheap
// to construct, and caching it traps the first observed locale — when the
// user picks a new language at runtime, a cached loader keeps returning the
// old strings until the app restarts. Recreating per-call costs nothing in
// practice (WinRT caches the underlying ResourceMap globally).

using Windows.ApplicationModel.Resources;

namespace Vianigram.App.Services
{
    public static class Strings
    {
        /// <summary>
        /// Returns the localized string for <paramref name="key"/>, or the
        /// key itself if the lookup misses. Never throws.
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            string value = Lookup(key);
            if (!string.IsNullOrEmpty(value)) return value;

            value = Lookup(key + ".Text");
            if (!string.IsNullOrEmpty(value)) return value;

            value = Lookup(key + "/Text");
            if (!string.IsNullOrEmpty(value)) return value;

            value = Lookup(key + ".Content");
            if (!string.IsNullOrEmpty(value)) return value;

            value = Lookup(key + "/Content");
            if (!string.IsNullOrEmpty(value)) return value;

            value = Lookup(key + ".PlaceholderText");
            if (!string.IsNullOrEmpty(value)) return value;

            value = Lookup(key + "/PlaceholderText");
            if (!string.IsNullOrEmpty(value)) return value;

            return key;
        }

        private static string Lookup(string key)
        {
            try
            {
                var loader = ResourceLoader.GetForCurrentView();
                if (loader != null)
                {
                    string value = loader.GetString(key);
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch
            {
                // Current-view lookup is only available from a view-bound UI
                // thread. Background callers fall back below.
            }

            try
            {
                var loader = ResourceLoader.GetForViewIndependentUse();
                if (loader != null)
                {
                    string value = loader.GetString(key);
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch
            {
                // Designer / unit-test environments may not have resource
                // context available. Get returns the key on a full miss.
            }

            return null;
        }
    }
}
