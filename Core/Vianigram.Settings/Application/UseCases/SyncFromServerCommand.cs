// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Settings.Application.UseCases
{
    /// <summary>
    /// Re-hydrate Settings from the server. The handler issues:
    ///
    ///   * <c>langpack.getLangPack#f2f2330a</c> for the active language pack
    ///     to refresh the version stamp.
    ///   * <c>account.getContentSettings#8b9b4dae</c> to mirror the
    ///     sensitive-content toggle.
    ///
    /// The language sync drives the result; content-settings failures are
    /// logged as warnings and do not poison the outcome.
    /// </summary>
    public sealed class SyncFromServerCommand
    {
        /// <summary>Singleton — there are no parameters.</summary>
        public static readonly SyncFromServerCommand Default = new SyncFromServerCommand();

        private SyncFromServerCommand() { }
    }
}
