// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Account.Domain.ValueObjects;

namespace Vianigram.Account.Application.Commands
{
    /// <summary>Initiate a phone-number login by issuing auth.sendCode.</summary>
    public sealed class SendPhoneCodeCommand
    {
        public PhoneNumber Phone { get; private set; }

        public SendPhoneCodeCommand(PhoneNumber phone)
        {
            Phone = phone;
        }
    }
}
