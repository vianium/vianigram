// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using Vianigram.Account.Domain.Errors;
using Vianigram.Account.Domain.Events;
using Vianigram.Account.Domain.ValueObjects;
using Vianigram.Kernel.Events;
using Vianigram.Kernel.Result;
using Vianigram.Kernel.Time;

namespace Vianigram.Account.Domain.Entities
{
    /// <summary>
    /// Aggregate root for the Account bounded context.
    ///
    /// Owns the auth state machine (Anonymous → WaitingForCode → optional
    /// WaitingForPassword → Authorized) and the per-DC auth_key set.
    ///
    /// All transitions emit a domain event through the injected
    /// <see cref="IEventBus"/>; no other context observes the aggregate
    /// directly.
    /// </summary>
    public sealed class AccountIdentity
    {
        private readonly IEventBus _events;
        private readonly IClock _clock;
        private readonly Dictionary<int, AuthKey> _authKeysByDc;

        public AccountId Id { get; private set; }
        public PhoneNumber Phone { get; private set; }
        public AuthState State { get; private set; }
        public DateTime CreatedAtUtc { get; private set; }
        public DateTime LastActivityUtc { get; private set; }

        public AccountIdentity(IEventBus events, IClock clock)
            : this(events, clock, AccountId.New())
        {
        }

        public AccountIdentity(IEventBus events, IClock clock, AccountId id)
        {
            if (events == null) throw new ArgumentNullException("events");
            if (clock == null) throw new ArgumentNullException("clock");
            _events = events;
            _clock = clock;
            _authKeysByDc = new Dictionary<int, AuthKey>();
            Id = id;
            State = AuthState.Anonymous();
            CreatedAtUtc = clock.UtcNow;
            LastActivityUtc = CreatedAtUtc;
        }

        public IDictionary<int, AuthKey> AuthKeysByDc
        {
            get { return _authKeysByDc; }
        }

        /// <summary>Begin a phone+SMS auth flow. Resets prior pending state.</summary>
        public Result<Unit, AccountError> BeginPhoneAuth(PhoneNumber phone)
        {
            if (phone == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.InvalidPhoneNumber("phone is null"));
            }

            if (State.IsAuthorized)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("Cannot begin phone auth on an authorized identity; logout first."));
            }

            Phone = phone;
            // We intentionally stay in Anonymous until the server confirms the
            // sentCode; the handler calls ApplyCodeSent on success.
            State = AuthState.Anonymous();
            Touch();
            _events.Publish(new PhoneAuthInitiated(Id, phone, _clock.UtcNow));
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        /// <summary>Server confirmed auth.sentCode — transition to WaitingForCode.</summary>
        public Result<Unit, AccountError> ApplyCodeSent(
            PhoneCodeHash hash,
            SentCodeType type,
            SentCodeType? nextType,
            TimeSpan timeout)
        {
            if (hash == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("phone_code_hash is null"));
            }

            if (Phone == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("phone not set; call BeginPhoneAuth first"));
            }

            State = AuthState.WaitingForCode(Phone, hash, type, nextType, _clock.UtcNow + timeout);
            Touch();
            _events.Publish(new CodeSent(Id, type, nextType, _clock.UtcNow));
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        /// <summary>
        /// 2FA challenge required after auth.signIn or auth.importLoginToken —
        /// transition to WaitingForPassword. Valid source states:
        ///   - WaitingForCode (phone-number flow: server returned
        ///     SESSION_PASSWORD_NEEDED on auth.signIn).
        ///   - Anonymous (QR-login flow: server returned
        ///     SESSION_PASSWORD_NEEDED on auth.importLoginToken — the user
        ///     never went through phone entry, so the aggregate is still
        ///     anonymous when 2FA is requested).
        /// </summary>
        public Result<Unit, AccountError> Apply2faRequired(SrpChallenge challenge)
        {
            if (challenge == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("srp challenge is null"));
            }

            if (State.Kind != AuthState.AuthStateKind.WaitingForCode &&
                State.Kind != AuthState.AuthStateKind.Anonymous)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState(
                        "2FA prompt only valid from WaitingForCode (phone) or Anonymous (qr)"));
            }

            State = AuthState.WaitingForPassword(challenge);
            Touch();
            _events.Publish(new TwoFaRequired(Id, _clock.UtcNow));
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        /// <summary>auth.authorization received — transition to Authorized and store the DC's auth_key.</summary>
        public Result<Unit, AccountError> ApplyAuthSuccess(UserId userId, AuthKey key, int dcId)
        {
            if (key == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("auth_key is null"));
            }

            if (dcId < 1 || dcId > 5)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("dcId out of range 1..5: " + dcId));
            }

            _authKeysByDc[dcId] = key;
            State = AuthState.Authorized(userId, dcId);
            Touch();
            _events.Publish(new AuthSucceeded(Id, userId, dcId, _clock.UtcNow));
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        /// <summary>
        /// Boot-time rehydration: install a previously persisted (userId,
        /// authKey, dcId) tuple into the aggregate without re-running the
        /// login state machine. Unlike <see cref="ApplyAuthSuccess"/>, this
        /// does NOT publish an AuthSucceeded event — there's no fresh login
        /// happening, just a state restore from disk. Subscribers that
        /// hydrate on AuthSucceeded must instead listen for app start.
        ///
        /// Idempotent: silently no-ops if the aggregate is already
        /// Authorized for the same user.
        /// </summary>
        public Result<Unit, AccountError> RestoreAuthorized(UserId userId, AuthKey key, int dcId)
        {
            if (key == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("auth_key is null"));
            }

            if (dcId < 1 || dcId > 5)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("dcId out of range 1..5: " + dcId));
            }

            // Idempotent: same user, same DC, no-op. UserId is a struct so
            // we compare values directly — no null check.
            if (State.IsAuthorized
                && State.AuthorizedUserId.Equals(userId)
                && State.AuthorizedDcId == dcId)
            {
                return Result<Unit, AccountError>.Ok(Unit.Value);
            }

            _authKeysByDc[dcId] = key;
            State = AuthState.Authorized(userId, dcId);
            Touch();
            // Deliberately silent — boot-time rehydration is not a fresh
            // auth event. The host wires Sync.Bootstrap separately.
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        /// <summary>
        /// Boot-time fast restore from a persisted session marker. The
        /// transport layer still loads/decrypts the auth_key before the first
        /// RPC; this keeps first paint from waiting on key unprotection.
        /// </summary>
        public Result<Unit, AccountError> RestoreAuthorizedMarker(UserId userId, int dcId)
        {
            if (dcId < 1 || dcId > 5)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("dcId out of range 1..5: " + dcId));
            }

            if (State.IsAuthorized
                && State.AuthorizedUserId.Equals(userId)
                && State.AuthorizedDcId == dcId)
            {
                return Result<Unit, AccountError>.Ok(Unit.Value);
            }

            State = AuthState.Authorized(userId, dcId);
            Touch();
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        /// <summary>Record a terminal failure of an auth attempt.</summary>
        public Result<Unit, AccountError> ApplyAuthFailure(AccountError error)
        {
            if (error == null)
            {
                return Result<Unit, AccountError>.Fail(
                    AccountError.NotInExpectedState("error is null"));
            }

            Touch();
            _events.Publish(new AuthFailed(Id, error, _clock.UtcNow));
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        /// <summary>Forget all auth keys, return to Anonymous, emit LoggedOut.</summary>
        public Result<Unit, AccountError> ApplyLogout()
        {
            _authKeysByDc.Clear();
            Phone = null;
            State = AuthState.Anonymous();
            Touch();
            _events.Publish(new LoggedOut(Id, _clock.UtcNow));
            return Result<Unit, AccountError>.Ok(Unit.Value);
        }

        public AccountStateSnapshot Snapshot()
        {
            long? userId = State.IsAuthorized ? (long?)State.AuthorizedUserId.Value : null;
            int? dcId = State.IsAuthorized ? (int?)State.AuthorizedDcId : null;
            SentCodeType? sentCodeType = State.Kind == AuthState.AuthStateKind.WaitingForCode
                ? (SentCodeType?)State.WaitingCodeType
                : null;
            SentCodeType? nextCodeType = State.Kind == AuthState.AuthStateKind.WaitingForCode
                ? State.WaitingNextCodeType
                : null;
            DateTime? codeExpiresAtUtc = State.Kind == AuthState.AuthStateKind.WaitingForCode
                ? (DateTime?)State.WaitingExpiresUtc
                : null;
            return new AccountStateSnapshot(
                Id,
                Phone == null ? null : Phone.E164,
                State.Kind,
                userId,
                dcId,
                sentCodeType,
                nextCodeType,
                codeExpiresAtUtc,
                CreatedAtUtc,
                LastActivityUtc);
        }

        private void Touch()
        {
            LastActivityUtc = _clock.UtcNow;
        }
    }
}
