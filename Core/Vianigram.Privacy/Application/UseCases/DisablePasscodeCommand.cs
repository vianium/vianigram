// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Disable the passcode after verifying the user knows the current PIN.
    /// Wipes the local store and zeros the in-memory hash on the aggregate.
    /// </summary>
    public sealed class DisablePasscodeCommand
    {
        public string Pin { get; private set; }

        public DisablePasscodeCommand(string pin)
        {
            Pin = pin ?? string.Empty;
        }
    }
}
