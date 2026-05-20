// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Application.UseCases
{
    /// <summary>
    /// Switch the active <see cref="Theme"/>. The handler persists the new
    /// value under <c>appearance.theme_mode</c> and stages a
    /// <c>ThemeChanged</c> domain event so the App layer can re-apply
    /// resources.
    /// </summary>
    public sealed class ApplyThemeCommand
    {
        public Theme Theme { get; private set; }

        public ApplyThemeCommand(Theme theme)
        {
            Theme = theme;
        }
    }
}
