// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Settings.Application.UseCases
{
    /// <summary>
    /// Reset every preference to its catalog default. The handler wipes the
    /// store, hydrates the aggregate to an empty state, and emits a single
    /// <c>PreferencesReset</c> domain event so subscribers can refresh in one
    /// pass instead of per-key.
    /// </summary>
    public sealed class ResetToDefaultsCommand
    {
        /// <summary>Singleton — there are no parameters.</summary>
        public static readonly ResetToDefaultsCommand Default = new ResetToDefaultsCommand();

        private ResetToDefaultsCommand() { }
    }
}
