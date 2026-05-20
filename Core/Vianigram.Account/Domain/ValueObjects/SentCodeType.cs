// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Transport over which the verification code was delivered, mirroring the
    /// <c>auth.SentCodeType</c> TL union.
    /// </summary>
    public enum SentCodeType
    {
        /// <summary>App push (in-app notification on another logged-in device).</summary>
        App = 0,
        /// <summary>SMS to the phone number.</summary>
        Sms = 1,
        /// <summary>Voice call dictating the code.</summary>
        Call = 2,
        /// <summary>Flash call where the caller-ID itself encodes the code.</summary>
        FlashCall = 3
    }
}
