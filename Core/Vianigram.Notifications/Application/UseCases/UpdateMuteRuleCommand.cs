// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Application.UseCases
{
    /// <summary>
    /// Update the mute rule for one peer (or the global default when
    /// <see cref="PeerKey"/> equals <see cref="MuteRule.Global"/>). Issues
    /// <c>account.updateNotifySettings#84be5b93</c> against the configured
    /// RPC port.
    /// </summary>
    public sealed class UpdateMuteRuleCommand
    {
        public string PeerKey { get; private set; }
        public MuteRule Rule { get; private set; }

        public UpdateMuteRuleCommand(string peerKey, MuteRule rule)
        {
            PeerKey = peerKey;
            Rule = rule;
        }
    }
}
