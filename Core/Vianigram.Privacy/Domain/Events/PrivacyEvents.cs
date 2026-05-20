// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Domain.Events
{
    /// <summary>
    /// Emitted whenever a single <see cref="PrivacyKey"/> rule is
    /// successfully written (after <c>account.setPrivacy</c> returns).
    /// Subscribers (Settings UI, in-process caches) refresh their view from
    /// the carried <see cref="Rule"/> without hitting the wire.
    /// </summary>
    public sealed class PrivacyRuleChanged : IDomainEvent
    {
        public PrivacyKey Key { get; private set; }
        public PrivacyRule Rule { get; private set; }
        public DateTime At { get; private set; }

        public PrivacyRuleChanged(PrivacyKey key, PrivacyRule rule, DateTime at)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            Key = key;
            Rule = rule;
            At = at;
        }
    }

    /// <summary>
    /// Emitted whenever the active-session list is refreshed from
    /// <c>account.getAuthorizations</c>. Carries the full snapshot so
    /// subscribers do not have to re-issue the call.
    /// </summary>
    public sealed class SessionsLoaded : IDomainEvent
    {
        public IList<ActiveSession> Sessions { get; private set; }
        public DateTime At { get; private set; }

        public SessionsLoaded(IList<ActiveSession> sessions, DateTime at)
        {
            Sessions = sessions ?? (IList<ActiveSession>)new ActiveSession[0];
            At = at;
        }
    }

    /// <summary>
    /// Emitted when a single session is terminated via
    /// <c>account.resetAuthorization(hash)</c>.
    /// </summary>
    public sealed class SessionTerminated : IDomainEvent
    {
        public long Hash { get; private set; }
        public DateTime At { get; private set; }

        public SessionTerminated(long hash, DateTime at)
        {
            Hash = hash;
            At = at;
        }
    }

    /// <summary>
    /// Emitted when every non-current session is terminated via
    /// <c>auth.resetAuthorizations#9fab0d1a</c>.
    /// </summary>
    public sealed class AllOtherSessionsTerminated : IDomainEvent
    {
        public DateTime At { get; private set; }

        public AllOtherSessionsTerminated(DateTime at) { At = at; }
    }

    /// <summary>
    /// Emitted whenever the passcode configuration is rewritten — enable,
    /// disable, or change. Carries no material; subscribers query
    /// <c>IPrivacyApi.IsPasscodeEnabled</c> if they need the new state.
    /// </summary>
    public sealed class PasscodeChanged : IDomainEvent
    {
        public PasscodeKind NewKind { get; private set; }
        public bool NowEnabled { get; private set; }
        public DateTime At { get; private set; }

        public PasscodeChanged(PasscodeKind newKind, bool nowEnabled, DateTime at)
        {
            NewKind = newKind;
            NowEnabled = nowEnabled;
            At = at;
        }
    }

    /// <summary>
    /// Emitted on a successful <c>VerifyPasscodeAsync</c> call. Listened to by
    /// the App shell (navigate away from the lock screen) and by other
    /// contexts that gated content behind unlock.
    /// </summary>
    public sealed class PasscodeUnlocked : IDomainEvent
    {
        public DateTime At { get; private set; }

        public PasscodeUnlocked(DateTime at) { At = at; }
    }

    /// <summary>
    /// Emitted on a failed <c>VerifyPasscodeAsync</c> call. Carries no input —
    /// the App shell uses this to drive a failed-attempt counter / backoff
    /// banner.
    /// </summary>
    public sealed class PasscodeFailedAttempt : IDomainEvent
    {
        public DateTime At { get; private set; }

        public PasscodeFailedAttempt(DateTime at) { At = at; }
    }
}
