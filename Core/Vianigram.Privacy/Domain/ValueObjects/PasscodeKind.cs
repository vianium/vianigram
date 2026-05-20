// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Privacy.Domain.ValueObjects
{
    /// <summary>
    /// Shape of the user-supplied passcode. The current privacy flow accepts
    /// numeric PINs; pattern is modeled so persisted values have a stable
    /// discriminator if the UI exposes a pattern keypad.
    /// </summary>
    public enum PasscodeKind
    {
        None = 0,
        Pin = 1,
        Pattern = 2
    }
}
