// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Kernel.Events;
using Vianigram.Privacy.Domain.Events;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Domain.Entities
{
    /// <summary>
    /// Aggregate root for the Privacy bounded context. Holds:
    /// <list type="bullet">
    ///   <item><description>The map of <see cref="PrivacyKey"/> → <see cref="PrivacyRule"/> (cached server state).</description></item>
    ///   <item><description>The latest <see cref="ActiveSession"/> snapshot.</description></item>
    ///   <item><description>The local <see cref="PasscodeState"/> (M3-isolated; never expose hash bytes through public API).</description></item>
    /// </list>
    ///
    /// <para><b>Mutation pattern</b> (mirrors the Settings <c>UserPreferences</c>
    /// and Search <c>SearchSession</c> aggregates): handlers call mutators,
    /// then drain <see cref="DequeuePendingEvents"/> after the RPC / store
    /// commit succeeds and publish each event on the bus.</para>
    ///
    /// <para>Not thread-safe. Each <see cref="Application.PrivacyApplication"/>
    /// instance owns its own <c>PrivacyProfile</c> and serializes mutations
    /// via the handler call-chain.</para>
    /// </summary>
    public sealed class PrivacyProfile
    {
        private readonly Dictionary<PrivacyKey, PrivacyRule> _rules;
        private readonly List<ActiveSession> _sessions;
        private readonly List<IDomainEvent> _pending;
        private PasscodeState _passcode;

        public PrivacyProfile()
        {
            _rules = new Dictionary<PrivacyKey, PrivacyRule>(16);
            _sessions = new List<ActiveSession>(8);
            _pending = new List<IDomainEvent>(4);
            _passcode = PasscodeState.Disabled;
        }

        // ---- read accessors ----------------------------------------------------

        /// <summary>Get the cached rule for a key, or null if never loaded.</summary>
        public PrivacyRule TryGetRule(PrivacyKey key)
        {
            PrivacyRule rule;
            if (_rules.TryGetValue(key, out rule)) return rule;
            return null;
        }

        /// <summary>Defensive snapshot of the cached active sessions.</summary>
        public IList<ActiveSession> Sessions
        {
            get
            {
                if (_sessions.Count == 0) return new ActiveSession[0];
                return _sessions.ToArray();
            }
        }

        /// <summary>True iff the local passcode is enabled.</summary>
        public bool IsPasscodeEnabled { get { return _passcode.Enabled; } }

        /// <summary>
        /// Internal — exposes the current <see cref="PasscodeState"/> so the
        /// application handlers / hasher can call its <c>VerifyAgainst</c>
        /// helper. The state itself never leaks the hash bytes through a
        /// public surface.
        /// </summary>
        internal PasscodeState PasscodeSnapshot { get { return _passcode; } }

        // ---- transitions -------------------------------------------------------

        /// <summary>
        /// Record a freshly fetched / updated rule for <paramref name="key"/>.
        /// Stages a <see cref="PrivacyRuleChanged"/> event.
        /// </summary>
        public void RecordRule(PrivacyKey key, PrivacyRule rule, DateTime at)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            _rules[key] = rule;
            Stage(new PrivacyRuleChanged(key, rule, at));
        }

        /// <summary>
        /// Replace the active-session cache. Stages a
        /// <see cref="SessionsLoaded"/> event with the new list.
        /// </summary>
        public void RecordSessions(IList<ActiveSession> snapshot, DateTime at)
        {
            _sessions.Clear();
            if (snapshot != null)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    if (snapshot[i] != null) _sessions.Add(snapshot[i]);
                }
            }
            Stage(new SessionsLoaded(Sessions, at));
        }

        /// <summary>
        /// Drop a single session from the cache (post-<c>resetAuthorization</c>).
        /// Stages a <see cref="SessionTerminated"/> event regardless of whether
        /// the cache held the entry — terminations are reported authoritatively.
        /// </summary>
        public void RecordSessionTerminated(long hash, DateTime at)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                if (_sessions[i].Hash == hash) _sessions.RemoveAt(i);
            }
            Stage(new SessionTerminated(hash, at));
        }

        /// <summary>
        /// Drop every non-current session from the cache (post-<c>auth.resetAuthorizations</c>).
        /// Stages an <see cref="AllOtherSessionsTerminated"/> event.
        /// </summary>
        public void RecordAllOtherSessionsTerminated(DateTime at)
        {
            for (int i = _sessions.Count - 1; i >= 0; i--)
            {
                if (!_sessions[i].IsCurrent) _sessions.RemoveAt(i);
            }
            Stage(new AllOtherSessionsTerminated(at));
        }

        /// <summary>
        /// Replace the local passcode state. Stages a
        /// <see cref="PasscodeChanged"/> event.
        /// </summary>
        public void RecordPasscode(PasscodeState newState, DateTime at)
        {
            if (newState == null) throw new ArgumentNullException("newState");
            _passcode = newState;
            Stage(new PasscodeChanged(newState.Kind, newState.Enabled, at));
        }

        /// <summary>Stamp the <see cref="PasscodeState.LastUnlocked"/> timestamp and stage <see cref="PasscodeUnlocked"/>.</summary>
        public void RecordPasscodeUnlocked(DateTime at)
        {
            _passcode = _passcode.WithLastUnlocked(at);
            Stage(new PasscodeUnlocked(at));
        }

        /// <summary>Stage a <see cref="PasscodeFailedAttempt"/> — no state mutation.</summary>
        public void RecordPasscodeFailedAttempt(DateTime at)
        {
            Stage(new PasscodeFailedAttempt(at));
        }

        // ---- pending events ----------------------------------------------------

        /// <summary>Drain pending domain events for the handler to publish.</summary>
        public IList<IDomainEvent> DequeuePendingEvents()
        {
            if (_pending.Count == 0) return new IDomainEvent[0];
            var copy = _pending.ToArray();
            _pending.Clear();
            return copy;
        }

        private void Stage(IDomainEvent evt)
        {
            _pending.Add(evt);
        }

        public override string ToString()
        {
            return "PrivacyProfile(rules=" + _rules.Count + " sessions=" + _sessions.Count + " " + _passcode + ")";
        }
    }
}
