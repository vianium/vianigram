// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Configure a fresh PIN passcode. Replaces any existing passcode
    /// without prior verification — callers that need a "type your old PIN
    /// before changing" step use <see cref="ChangePasscodeCommand"/> instead.
    /// </summary>
    public sealed class EnablePasscodeCommand
    {
        public string Pin { get; private set; }

        public EnablePasscodeCommand(string pin)
        {
            Pin = pin ?? string.Empty;
        }
    }
}
