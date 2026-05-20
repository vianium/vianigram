// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Issue <c>account.setPrivacy#c9f81ce8</c> with the supplied
    /// <see cref="PrivacyKey"/> + <see cref="PrivacyRule"/>. The clauses are
    /// serialized to <c>inputPrivacyRule*</c> in order and the first matching
    /// clause wins on the server side.
    /// </summary>
    public sealed class SetPrivacyRuleCommand
    {
        public PrivacyKey Key { get; private set; }
        public PrivacyRule Rule { get; private set; }

        public SetPrivacyRuleCommand(PrivacyKey key, PrivacyRule rule)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            Key = key;
            Rule = rule;
        }
    }
}
