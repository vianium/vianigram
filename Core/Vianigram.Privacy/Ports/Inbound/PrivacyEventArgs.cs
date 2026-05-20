// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Ports.Inbound
{
    /// <summary>
    /// Event payload raised by <see cref="IPrivacyApi.RuleChanged"/> when a
    /// privacy rule is successfully written back to the server. Mirrors the
    /// <c>PrivacyRuleChanged</c> domain event in a CLR-event shape so XAML /
    /// UI layers that don't take an <c>IEventBus</c> dependency can still
    /// subscribe.
    /// </summary>
    public sealed class PrivacyRuleChangedEventArgs : EventArgs
    {
        public PrivacyKey Key { get; private set; }
        public PrivacyRule Rule { get; private set; }
        public DateTime At { get; private set; }

        public PrivacyRuleChangedEventArgs(PrivacyKey key, PrivacyRule rule, DateTime at)
        {
            Key = key;
            Rule = rule;
            At = at;
        }
    }

    /// <summary>
    /// Event payload raised by <see cref="IPrivacyApi.SessionTerminated"/>
    /// after a single session is reset via
    /// <c>account.resetAuthorization(hash)</c>.
    /// </summary>
    public sealed class SessionTerminatedEventArgs : EventArgs
    {
        public long Hash { get; private set; }
        public DateTime At { get; private set; }

        public SessionTerminatedEventArgs(long hash, DateTime at)
        {
            Hash = hash;
            At = at;
        }
    }
}
