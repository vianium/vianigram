// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Account.Domain.ValueObjects;

namespace Vianigram.Account.Application.Commands
{
    /// <summary>Submit the SMS/push code via auth.signIn.</summary>
    public sealed class VerifyPhoneCodeCommand
    {
        public PhoneNumber Phone { get; private set; }
        public string Code { get; private set; }

        public VerifyPhoneCodeCommand(PhoneNumber phone, string code)
        {
            Phone = phone;
            Code = code;
        }
    }
}
