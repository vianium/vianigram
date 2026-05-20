// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Account.Application.Commands
{
    /// <summary>
    /// Outcome of a code-verification step. <see cref="TwoFaRequired"/> means
    /// the caller must follow up with <c>SubmitTwoFaPasswordAsync</c>;
    /// otherwise <see cref="UserId"/> is populated and the identity is
    /// authorized.
    /// </summary>
    public sealed class AuthOutcome
    {
        public bool TwoFaRequired { get; set; }
        public bool SignUpRequired { get; set; }
        public long? UserId { get; set; }
        public string PasswordHint { get; set; }
    }
}
