// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.ValueObjects
{
    /// <summary>
    /// Discriminated-union of the auth state machine. Used as enum-class:
    /// callers branch on <see cref="Kind"/> and read the matching nested type.
    ///
    /// Kept immutable: each transition produces a fresh AuthState instance so
    /// the aggregate's history is observable through events.
    /// </summary>
    public sealed class AuthState
    {
        public enum AuthStateKind
        {
            Anonymous = 0,
            WaitingForCode = 1,
            WaitingForPassword = 2,
            Authorized = 3
        }

        public AuthStateKind Kind { get; private set; }

        // WaitingForCode payload
        public PhoneNumber WaitingPhone { get; private set; }
        public PhoneCodeHash WaitingHash { get; private set; }
        public SentCodeType WaitingCodeType { get; private set; }
        public SentCodeType? WaitingNextCodeType { get; private set; }
        public DateTime WaitingExpiresUtc { get; private set; }

        // WaitingForPassword payload
        public SrpChallenge SrpChallenge { get; private set; }

        // Authorized payload
        public UserId AuthorizedUserId { get; private set; }
        public int AuthorizedDcId { get; private set; }

        private AuthState(AuthStateKind kind)
        {
            Kind = kind;
        }

        public static AuthState Anonymous()
        {
            return new AuthState(AuthStateKind.Anonymous);
        }

        public static AuthState WaitingForCode(
            PhoneNumber phone,
            PhoneCodeHash hash,
            SentCodeType type,
            SentCodeType? nextType,
            DateTime expiresUtc)
        {
            if (phone == null) throw new ArgumentNullException("phone");
            if (hash == null) throw new ArgumentNullException("hash");
            return new AuthState(AuthStateKind.WaitingForCode)
            {
                WaitingPhone = phone,
                WaitingHash = hash,
                WaitingCodeType = type,
                WaitingNextCodeType = nextType,
                WaitingExpiresUtc = expiresUtc
            };
        }

        public static AuthState WaitingForPassword(SrpChallenge challenge)
        {
            if (challenge == null) throw new ArgumentNullException("challenge");
            return new AuthState(AuthStateKind.WaitingForPassword)
            {
                SrpChallenge = challenge
            };
        }

        public static AuthState Authorized(UserId userId, int dcId)
        {
            return new AuthState(AuthStateKind.Authorized)
            {
                AuthorizedUserId = userId,
                AuthorizedDcId = dcId
            };
        }

        public bool IsAuthorized
        {
            get { return Kind == AuthStateKind.Authorized; }
        }
    }
}
