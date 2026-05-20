// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Threading.Tasks;

namespace Vianigram.App.Services
{
    public static class CountrySelectionService
    {
        private static readonly TelegramCountryEntry FallbackCountry =
            new TelegramCountryEntry("1", "US", "USA", "XXX XXX XXXX", "USA");

        private static TelegramCountryEntry _current = FallbackCountry;
        private static bool _hasCatalogSelection;
        private static bool _hasUserSelection;

        public static TelegramCountryEntry Current
        {
            get { return _current ?? FallbackCountry; }
        }

        public static void Select(TelegramCountryEntry country)
        {
            if (country == null) return;

            _current = country;
            _hasCatalogSelection = true;
            _hasUserSelection = true;
        }

        public static async Task<TelegramCountryEntry> EnsureCurrentAsync()
        {
            if (_hasUserSelection || _hasCatalogSelection)
            {
                return Current;
            }

            try
            {
                var countries = await TelegramCountryCatalog.LoadAsync().ConfigureAwait(true);
                TelegramCountryEntry preferred = TelegramCountryCatalog.FindPreferred(countries);
                if (preferred != null)
                {
                    _current = preferred;
                    _hasCatalogSelection = true;
                }
            }
            catch
            {
                // The phone screen can still work with the fallback country.
            }

            return Current;
        }
    }
}
