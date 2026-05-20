// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Application.UseCases
{
    /// <summary>
    /// Issue <c>account.getPrivacy#dadbc950</c> for a single
    /// <see cref="PrivacyKey"/>. The handler returns the freshly fetched
    /// <see cref="PrivacyRule"/> and caches it on the aggregate.
    /// </summary>
    public sealed class GetPrivacyRulesCommand
    {
        public PrivacyKey Key { get; private set; }

        public GetPrivacyRulesCommand(PrivacyKey key)
        {
            Key = key;
        }
    }
}
