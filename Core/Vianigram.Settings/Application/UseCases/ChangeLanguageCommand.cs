// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Settings.Application.UseCases
{
    /// <summary>
    /// Switch the active language pack. The handler persists the new
    /// <c>LanguagePack</c> under <c>language.pack</c> and stages a
    /// <c>LanguageChanged</c> domain event; the actual strings table refresh
    /// is left to <c>SyncFromServerCommand</c> (or the future I18n context).
    /// </summary>
    public sealed class ChangeLanguageCommand
    {
        public string LangCode { get; private set; }

        public ChangeLanguageCommand(string langCode)
        {
            LangCode = langCode;
        }
    }
}
