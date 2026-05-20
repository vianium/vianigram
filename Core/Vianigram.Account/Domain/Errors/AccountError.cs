// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Account.Domain.Errors
{
    /// <summary>
    /// Closed catalog of failure kinds for the Account bounded context.
    /// Mapped from MTProto rpc errors and from local invariant violations.
    /// </summary>
    public enum AccountErrorKind
    {
        InvalidPhoneNumber = 0,
        PhoneCodeInvalid = 1,
        PhoneCodeExpired = 2,
        /// <summary>FLOOD_WAIT_X — the server forbids retry until RetryAfterSeconds elapses.</summary>
        PhoneNumberFlood = 3,
        SrpPasswordInvalid = 4,
        SessionExpired = 5,
        /// <summary>AUTH_RESTART — server asked us to drop this code attempt and call sendCode again.</summary>
        AuthRestart = 6,
        NetworkError = 7,
        Unknown = 8,
        /// <summary>The aggregate was not in the state expected by this operation.</summary>
        NotInExpectedState = 9,
        /// <summary>PHONE_MIGRATE_X / NETWORK_MIGRATE_X — the request must be redirected to MigrateToDc.</summary>
        DcMigrationRequired = 10
    }

    /// <summary>
    /// Structured error type. Immutable. Carries optional metadata for the two
    /// kinds with first-class semantics: <see cref="RetryAfterSeconds"/> for
    /// FLOOD_WAIT, <see cref="MigrateToDc"/> for *_MIGRATE_X.
    /// </summary>
    public sealed class AccountError
    {
        public AccountErrorKind Kind { get; private set; }
        public string Message { get; private set; }
        public int? RetryAfterSeconds { get; private set; }
        public int? MigrateToDc { get; private set; }
        public Exception Cause { get; private set; }

        public AccountError(
            AccountErrorKind kind,
            string message,
            int? retryAfterSeconds = null,
            int? migrateToDc = null,
            Exception cause = null)
        {
            Kind = kind;
            Message = message ?? string.Empty;
            RetryAfterSeconds = retryAfterSeconds;
            MigrateToDc = migrateToDc;
            Cause = cause;
        }

        public static AccountError InvalidPhoneNumber(string detail)
        {
            return new AccountError(AccountErrorKind.InvalidPhoneNumber, detail);
        }

        public static AccountError PhoneCodeInvalid(string detail)
        {
            return new AccountError(AccountErrorKind.PhoneCodeInvalid, detail);
        }

        public static AccountError PhoneCodeExpired(string detail)
        {
            return new AccountError(AccountErrorKind.PhoneCodeExpired, detail);
        }

        public static AccountError PhoneNumberFlood(int retryAfterSeconds)
        {
            return new AccountError(
                AccountErrorKind.PhoneNumberFlood,
                "FLOOD_WAIT_" + retryAfterSeconds,
                retryAfterSeconds,
                null,
                null);
        }

        public static AccountError SrpPasswordInvalid(string detail)
        {
            return new AccountError(AccountErrorKind.SrpPasswordInvalid, detail);
        }

        public static AccountError SessionExpired(string detail)
        {
            return new AccountError(AccountErrorKind.SessionExpired, detail);
        }

        public static AccountError AuthRestart(string detail)
        {
            return new AccountError(AccountErrorKind.AuthRestart, detail);
        }

        public static AccountError NetworkError(string detail, Exception cause = null)
        {
            return new AccountError(AccountErrorKind.NetworkError, detail, null, null, cause);
        }

        public static AccountError NotInExpectedState(string detail)
        {
            return new AccountError(AccountErrorKind.NotInExpectedState, detail);
        }

        public static AccountError DcMigrationRequired(int targetDc)
        {
            return new AccountError(
                AccountErrorKind.DcMigrationRequired,
                "PHONE_MIGRATE_" + targetDc,
                null,
                targetDc,
                null);
        }

        public static AccountError Unknown(string detail, Exception cause = null)
        {
            return new AccountError(AccountErrorKind.Unknown, detail, null, null, cause);
        }

        public override string ToString()
        {
            string suffix = "";
            if (RetryAfterSeconds.HasValue) suffix += " retry_after=" + RetryAfterSeconds.Value + "s";
            if (MigrateToDc.HasValue) suffix += " migrate_to=DC" + MigrateToDc.Value;
            return Kind + ": " + Message + suffix;
        }
    }
}
