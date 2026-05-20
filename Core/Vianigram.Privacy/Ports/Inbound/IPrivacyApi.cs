// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Result;
using Vianigram.Privacy.Domain;
using Vianigram.Privacy.Domain.ValueObjects;

namespace Vianigram.Privacy.Ports.Inbound
{
    /// <summary>
    /// Public surface of the Privacy bounded context.
    /// Every method is async, takes a <see cref="CancellationToken"/>, and
    /// returns <c>Result&lt;T, PrivacyError&gt;</c>; no exceptions cross this
    /// boundary.
    ///
    /// <para><b>Surface</b> (4 areas):</para>
    /// <list type="bullet">
    ///   <item><description><b>Privacy rules</b> — get / set the audience for a single <see cref="PrivacyKey"/>.</description></item>
    ///   <item><description><b>Active sessions</b> — list, terminate one, terminate all-others.</description></item>
    ///   <item><description><b>Passcode lock</b> — enable, disable, verify, change.</description></item>
    ///   <item><description><b>State query</b> — <see cref="IsPasscodeEnabled"/>.</description></item>
    /// </list>
    ///
    /// <para><b>Blocked users</b>: deliberately NOT exposed here — the
    /// blocking surface is owned by Vianigram.Contacts. UI flows that need
    /// "Blocked users" go through <c>IContactsApi</c>; the Privacy doc
    /// surfaces blocked_users only as a capability, with the implementation
    /// delegated.</para>
    ///
    /// <para><b>CLR-event surface</b>: <see cref="RuleChanged"/> and
    /// <see cref="SessionTerminated"/> mirror the corresponding domain events
    /// so XAML / UI consumers can subscribe without taking an
    /// <c>IEventBus</c> dependency.</para>
    /// </summary>
    public interface IPrivacyApi
    {
        // ---- Privacy rules ----------------------------------------------------

        /// <summary>
        /// Issue <c>account.getPrivacy#dadbc950</c> for the supplied key.
        /// Returns a fresh <see cref="PrivacyRule"/>; caches the result on the
        /// aggregate so subsequent reads of the same key are local.
        /// </summary>
        Task<Result<PrivacyRule, PrivacyError>> GetRuleAsync(PrivacyKey key, CancellationToken ct);

        /// <summary>
        /// Issue <c>account.setPrivacy#c9f81ce8</c> with the supplied key +
        /// rule (clauses serialized to <c>inputPrivacyRule*</c> in order).
        /// Refreshes the cache and raises <see cref="RuleChanged"/> on
        /// success.
        /// </summary>
        Task<Result<Unit, PrivacyError>> SetRuleAsync(PrivacyKey key, PrivacyRule rule, CancellationToken ct);

        // ---- Active sessions --------------------------------------------------

        /// <summary>Issue <c>account.getAuthorizations#e320c158</c>; refresh the cache.</summary>
        Task<Result<IList<ActiveSession>, PrivacyError>> GetSessionsAsync(CancellationToken ct);

        /// <summary>Issue <c>account.resetAuthorization#df77f3bc</c>. Raises <see cref="SessionTerminated"/>.</summary>
        Task<Result<Unit, PrivacyError>> TerminateSessionAsync(long hash, CancellationToken ct);

        /// <summary>Issue <c>auth.resetAuthorizations#9fab0d1a</c> — terminates every non-current session.</summary>
        Task<Result<Unit, PrivacyError>> TerminateAllOtherSessionsAsync(CancellationToken ct);

        // ---- Passcode lock ----------------------------------------------------

        /// <summary>
        /// Configure a fresh passcode. Validates the PIN format, generates a
        /// salt, computes the hash through the wired
        /// <c>IPasscodeHasher</c>, and persists the
        /// <see cref="PasscodeState"/>. Replaces any existing passcode
        /// without further confirmation.
        /// </summary>
        Task<Result<Unit, PrivacyError>> EnablePasscodeAsync(string pin, CancellationToken ct);

        /// <summary>Verify the supplied PIN, then clear the passcode store.</summary>
        Task<Result<Unit, PrivacyError>> DisablePasscodeAsync(string pin, CancellationToken ct);

        /// <summary>
        /// Verify the supplied PIN against the stored hash in constant time.
        /// Returns <c>Ok(true)</c> on match, <c>Ok(false)</c> on mismatch (NOT
        /// a fail-result — verify is a query, not an error). Raises
        /// <see cref="Domain.Events.PasscodeUnlocked"/> on success and
        /// <see cref="Domain.Events.PasscodeFailedAttempt"/> on mismatch.
        /// </summary>
        Task<Result<bool, PrivacyError>> VerifyPasscodeAsync(string pin, CancellationToken ct);

        /// <summary>
        /// Verify the old PIN, then atomically replace it with a new one
        /// (validated + re-hashed). Fails with
        /// <see cref="PrivacyErrorKind.PasscodeWrong"/> when
        /// <paramref name="oldPin"/> does not match.
        /// </summary>
        Task<Result<Unit, PrivacyError>> ChangePasscodeAsync(string oldPin, string newPin, CancellationToken ct);

        // ---- State query ------------------------------------------------------

        /// <summary>Synchronous flag: true iff a passcode is currently configured.</summary>
        bool IsPasscodeEnabled { get; }

        // ---- CLR events -------------------------------------------------------

        /// <summary>Raised when a privacy rule is successfully written back to the server.</summary>
        event EventHandler<PrivacyRuleChangedEventArgs> RuleChanged;

        /// <summary>Raised when a single session is terminated.</summary>
        event EventHandler<SessionTerminatedEventArgs> SessionTerminated;
    }
}
