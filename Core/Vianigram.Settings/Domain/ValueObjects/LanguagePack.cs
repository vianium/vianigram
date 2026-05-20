// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Identifier for an active language pack. Carries the pack's
    /// <see cref="LangCode"/> (e.g. <c>"en"</c>, <c>"es"</c>, <c>"pt-br"</c>),
    /// the server-published <see cref="Version"/> integer, and an optional
    /// <see cref="BaseLangCode"/> indicating fallback (e.g. <c>"pt-br"</c>
    /// inheriting from <c>"pt"</c>).
    ///
    /// Mirrors TDLib's <c>languagePackInfo</c> (<c>td/telegram/LanguagePack*.cpp</c>)
    /// which carries id / base_language_pack_id / strings_count / version.
    /// V1 surfaces only the fields we need for routing; richer metadata is
    /// owned by the future <c>Vianigram.I18n</c> context.
    ///
    /// Immutable.
    /// </summary>
    public sealed class LanguagePack : IEquatable<LanguagePack>
    {
        public static readonly LanguagePack Default = new LanguagePack("en", 0, null);

        private readonly string _langCode;
        private readonly int _version;
        private readonly string _baseLangCode;

        public LanguagePack(string langCode, int version, string baseLangCode)
        {
            if (string.IsNullOrEmpty(langCode)) throw new ArgumentException("langCode required", "langCode");
            if (version < 0) throw new ArgumentOutOfRangeException("version", "version must be non-negative");
            _langCode = langCode;
            _version = version;
            _baseLangCode = baseLangCode ?? string.Empty;
        }

        public string LangCode { get { return _langCode; } }
        public int Version { get { return _version; } }
        public string BaseLangCode { get { return _baseLangCode; } }

        public bool HasBase
        {
            get { return !string.IsNullOrEmpty(_baseLangCode); }
        }

        public LanguagePack WithVersion(int newVersion)
        {
            return new LanguagePack(_langCode, newVersion, _baseLangCode);
        }

        public bool Equals(LanguagePack other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return string.Equals(_langCode, other._langCode, StringComparison.OrdinalIgnoreCase)
                && _version == other._version
                && string.Equals(_baseLangCode, other._baseLangCode, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as LanguagePack);
        }

        public override int GetHashCode()
        {
            int h = StringComparer.OrdinalIgnoreCase.GetHashCode(_langCode);
            h = (h * 397) ^ _version;
            if (!string.IsNullOrEmpty(_baseLangCode))
                h = (h * 397) ^ StringComparer.OrdinalIgnoreCase.GetHashCode(_baseLangCode);
            return h;
        }

        public override string ToString()
        {
            return _langCode + " (v" + _version + (HasBase ? ", base=" + _baseLangCode : string.Empty) + ")";
        }
    }
}
