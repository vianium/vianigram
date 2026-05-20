// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.System.UserProfile;

namespace Vianigram.App.Services
{
    public sealed class TelegramCountryEntry
    {
        public TelegramCountryEntry(string code, string iso2, string name, string pattern, string displayName)
        {
            Code = code ?? string.Empty;
            Iso2 = iso2 ?? string.Empty;
            Name = name ?? string.Empty;
            Pattern = pattern ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        public string Code { get; private set; }
        public string Iso2 { get; private set; }
        public string Name { get; private set; }
        public string Pattern { get; private set; }
        public string DisplayName { get; private set; }

        public string DialCode
        {
            get { return string.IsNullOrWhiteSpace(Code) ? string.Empty : "+" + Code; }
        }
    }

    public static class TelegramCountryCatalog
    {
        private const string AssetUri = "ms-appx:///Assets/TelegramCountries.txt";

        private static readonly Dictionary<string, string> FallbackLocalPatterns =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "AR", "00 0000 0000" },
                { "MX", "00 0000 0000" },
                { "US", "000 000 0000" },
            };

        private static IList<TelegramCountryEntry> _cachedEntries;

        public static async Task<IList<TelegramCountryEntry>> LoadAsync()
        {
            if (_cachedEntries != null)
            {
                return _cachedEntries;
            }

            IList<string> lines = await LoadRawLinesAsync().ConfigureAwait(true);
            List<RawCountryEntry> rawEntries = new List<RawCountryEntry>();
            Dictionary<string, int> nameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (int index = 0; index < lines.Count; index++)
            {
                string line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] parts = line.Split(';');
                if (parts.Length < 3)
                {
                    continue;
                }

                string code = parts[0].Trim();
                string iso2 = parts[1].Trim().ToUpperInvariant();
                string name = parts[2].Trim();
                string pattern = parts.Length > 3 ? parts[3].Trim() : string.Empty;

                if (code.Length == 0 || iso2.Length == 0 || name.Length == 0)
                {
                    continue;
                }

                if (string.Equals(iso2, "YL", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                rawEntries.Add(new RawCountryEntry(code, iso2, name, pattern));

                int count;
                if (!nameCounts.TryGetValue(name, out count))
                {
                    count = 0;
                }

                nameCounts[name] = count + 1;
            }

            rawEntries.Sort(CompareEntries);

            List<TelegramCountryEntry> parsedEntries = new List<TelegramCountryEntry>(rawEntries.Count);
            for (int index = 0; index < rawEntries.Count; index++)
            {
                RawCountryEntry rawEntry = rawEntries[index];
                int duplicateCount;
                bool hasDuplicateName =
                    nameCounts.TryGetValue(rawEntry.Name, out duplicateCount) && duplicateCount > 1;

                string displayName = hasDuplicateName
                    ? string.Format("{0} (+{1})", rawEntry.Name, rawEntry.Code)
                    : rawEntry.Name;

                parsedEntries.Add(
                    new TelegramCountryEntry(
                        rawEntry.Code,
                        rawEntry.Iso2,
                        rawEntry.Name,
                        rawEntry.Pattern,
                        displayName));
            }

            _cachedEntries = parsedEntries;
            return _cachedEntries;
        }

        public static string GetPreferredIso2()
        {
            try
            {
                string region = GlobalizationPreferences.HomeGeographicRegion;
                if (!string.IsNullOrWhiteSpace(region))
                {
                    return region.Trim().ToUpperInvariant();
                }
            }
            catch
            {
            }

            return "US";
        }

        public static TelegramCountryEntry FindPreferred(IList<TelegramCountryEntry> countries)
        {
            if (countries == null || countries.Count == 0)
            {
                return null;
            }

            string preferredIso = GetPreferredIso2();
            for (int index = 0; index < countries.Count; index++)
            {
                TelegramCountryEntry country = countries[index];
                if (country != null &&
                    string.Equals(country.Iso2, preferredIso, StringComparison.OrdinalIgnoreCase))
                {
                    return country;
                }
            }

            return countries[0];
        }

        public static string CreateLocalPhonePlaceholder(TelegramCountryEntry country)
        {
            string localPattern = ResolveLocalPattern(country);
            if (!string.IsNullOrWhiteSpace(localPattern))
            {
                return localPattern;
            }

            return "000 000 0000";
        }

        public static string FormatLocalPhoneNumber(TelegramCountryEntry country, string value)
        {
            string digits = StripToLocalDigits(country, value);
            if (digits.Length == 0)
            {
                return string.Empty;
            }

            string localPattern = ResolveLocalPattern(country);
            if (string.IsNullOrWhiteSpace(localPattern))
            {
                localPattern = "000 000 0000";
            }

            return FormatUsingPattern(digits, localPattern);
        }

        public static int GetExpectedLocalDigitCount(TelegramCountryEntry country)
        {
            string localPattern = ResolveLocalPattern(country);
            if (string.IsNullOrWhiteSpace(localPattern))
            {
                return 0;
            }

            List<int> groupSizes = ParseGroupSizes(localPattern);
            int total = 0;
            for (int index = 0; index < groupSizes.Count; index++)
            {
                total += groupSizes[index];
            }

            return total;
        }

        public static string StripToLocalDigits(TelegramCountryEntry country, string value)
        {
            string digits = StripToDigits(value);
            if (digits.Length == 0 || country == null || string.IsNullOrWhiteSpace(country.Code))
            {
                return digits;
            }

            string trimmed = (value ?? string.Empty).Trim();
            string countryCode = country.Code.Trim();

            if (trimmed.StartsWith("+", StringComparison.Ordinal) &&
                digits.StartsWith(countryCode, StringComparison.Ordinal))
            {
                return digits.Substring(countryCode.Length);
            }

            string internationalPrefix = "00" + countryCode;
            if (digits.StartsWith(internationalPrefix, StringComparison.Ordinal))
            {
                return digits.Substring(internationalPrefix.Length);
            }

            return digits;
        }

        public static string StripToDigits(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char current = value[index];
                if (char.IsDigit(current))
                {
                    builder.Append(current);
                }
            }

            return builder.ToString();
        }

        private static async Task<IList<string>> LoadRawLinesAsync()
        {
            StorageFile sourceFile = null;

            try
            {
                sourceFile = await StorageFile.GetFileFromApplicationUriAsync(new Uri(AssetUri));
            }
            catch
            {
            }

            if (sourceFile == null)
            {
                StorageFolder assetsFolder = await Package.Current.InstalledLocation.GetFolderAsync("Assets");
                sourceFile = await assetsFolder.GetFileAsync("TelegramCountries.txt");
            }

            IList<string> lines = await FileIO.ReadLinesAsync(sourceFile);
            if (lines == null || lines.Count == 0)
            {
                throw new InvalidOperationException("Telegram countries catalog is empty.");
            }

            return lines;
        }

        private static string NormalizePattern(string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(pattern.Length);
            for (int index = 0; index < pattern.Length; index++)
            {
                char current = pattern[index];
                builder.Append(current == 'X' ? '0' : current);
            }

            return builder.ToString();
        }

        private static string ResolveLocalPattern(TelegramCountryEntry country)
        {
            string normalizedPattern = NormalizePattern(country != null ? country.Pattern : string.Empty);
            string localPattern = StripCountryPrefix(normalizedPattern, country);
            if (!string.IsNullOrWhiteSpace(localPattern))
            {
                return localPattern;
            }

            if (country != null)
            {
                string fallbackPattern;
                if (FallbackLocalPatterns.TryGetValue(country.Iso2, out fallbackPattern))
                {
                    return fallbackPattern;
                }
            }

            return string.Empty;
        }

        private static string StripCountryPrefix(string normalizedPattern, TelegramCountryEntry country)
        {
            if (string.IsNullOrWhiteSpace(normalizedPattern))
            {
                return string.Empty;
            }

            string expectedPrefix = country != null && !string.IsNullOrWhiteSpace(country.Code)
                ? "+" + country.Code
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(expectedPrefix) &&
                normalizedPattern.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                return normalizedPattern.Substring(expectedPrefix.Length).Trim();
            }

            return normalizedPattern.Trim();
        }

        private static string FormatUsingPattern(string digits, string pattern)
        {
            List<int> groupSizes = ParseGroupSizes(pattern);
            if (groupSizes.Count == 0)
            {
                return digits;
            }

            StringBuilder builder = new StringBuilder(digits.Length + 4);
            int digitIndex = 0;

            for (int index = 0; index < groupSizes.Count && digitIndex < digits.Length; index++)
            {
                int groupSize = groupSizes[index];
                int take = Math.Min(groupSize, digits.Length - digitIndex);
                if (take <= 0)
                {
                    break;
                }

                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(digits.Substring(digitIndex, take));
                digitIndex += take;
            }

            if (digitIndex < digits.Length)
            {
                if (builder.Length > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(digits.Substring(digitIndex));
            }

            return builder.ToString();
        }

        private static List<int> ParseGroupSizes(string pattern)
        {
            List<int> groups = new List<int>();
            if (string.IsNullOrWhiteSpace(pattern))
            {
                return groups;
            }

            int currentGroupSize = 0;
            for (int index = 0; index < pattern.Length; index++)
            {
                char current = pattern[index];
                if (char.IsDigit(current))
                {
                    currentGroupSize++;
                    continue;
                }

                if (currentGroupSize > 0)
                {
                    groups.Add(currentGroupSize);
                    currentGroupSize = 0;
                }
            }

            if (currentGroupSize > 0)
            {
                groups.Add(currentGroupSize);
            }

            return groups;
        }

        private static int CompareEntries(RawCountryEntry left, RawCountryEntry right)
        {
            int byName = string.Compare(left.Name, right.Name, StringComparison.CurrentCultureIgnoreCase);
            if (byName != 0)
            {
                return byName;
            }

            int byCodeLength = left.Code.Length.CompareTo(right.Code.Length);
            if (byCodeLength != 0)
            {
                return byCodeLength;
            }

            return string.Compare(left.Code, right.Code, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class RawCountryEntry
        {
            public RawCountryEntry(string code, string iso2, string name, string pattern)
            {
                Code = code;
                Iso2 = iso2;
                Name = name;
                Pattern = pattern;
            }

            public string Code;
            public string Iso2;
            public string Name;
            public string Pattern;
        }
    }
}
