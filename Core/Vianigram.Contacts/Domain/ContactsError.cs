// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Contacts.Domain
{
    /// <summary>
    /// Categories of failure that the Contacts context can surface to callers
    /// without throwing exceptions. Mapped from MTProto rpc errors,
    /// persistence failures, or domain invariant violations by the application
    /// handlers.
    /// </summary>
    public enum ContactsErrorKind
    {
        NetworkError = 0,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="ContactsError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 1,
        AlreadyImported = 2,
        NotFound = 3,
        PermissionDenied = 4,
        NotInExpectedState = 5,
        Unknown = 6
    }

    /// <summary>
    /// Structured error type for Contacts. Carries a kind, a stable code
    /// (namespaced "contacts.&lt;kind&gt;"), a human-readable message, optional
    /// FLOOD_WAIT seconds, and an optional underlying <see cref="Exception"/>
    /// for diagnostics. Immutable.
    /// </summary>
    public sealed class ContactsError
    {
        private readonly ContactsErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public ContactsError(
            ContactsErrorKind kind,
            string code,
            string message,
            int? retryAfterSeconds = null,
            Exception cause = null)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("code required", "code");
            _kind = kind;
            _code = code;
            _message = message ?? string.Empty;
            _retryAfterSeconds = retryAfterSeconds;
            _cause = cause;
        }

        public ContactsErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static ContactsError NetworkError(string detail, Exception cause = null)
        {
            return new ContactsError(ContactsErrorKind.NetworkError, "contacts.network", detail ?? string.Empty, null, cause);
        }

        public static ContactsError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new ContactsError(
                ContactsErrorKind.FloodWait,
                "contacts.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static ContactsError AlreadyImported(string detail)
        {
            return new ContactsError(ContactsErrorKind.AlreadyImported, "contacts.already_imported", detail ?? string.Empty);
        }

        public static ContactsError NotFound(string detail)
        {
            return new ContactsError(ContactsErrorKind.NotFound, "contacts.not_found", detail ?? string.Empty);
        }

        public static ContactsError PermissionDenied(string detail)
        {
            return new ContactsError(ContactsErrorKind.PermissionDenied, "contacts.permission_denied", detail ?? string.Empty);
        }

        public static ContactsError NotInExpectedState(string detail)
        {
            return new ContactsError(ContactsErrorKind.NotInExpectedState, "contacts.bad_state", detail ?? string.Empty);
        }

        public static ContactsError Unknown(string detail, Exception cause = null)
        {
            return new ContactsError(ContactsErrorKind.Unknown, "contacts.unknown", detail ?? string.Empty, null, cause);
        }

        public override string ToString()
        {
            string suffix = _retryAfterSeconds.HasValue ? " retry_after=" + _retryAfterSeconds.Value + "s" : string.Empty;
            if (_cause != null)
                return _code + ": " + _message + suffix + " (cause: " + _cause.GetType().Name + ": " + _cause.Message + ")";
            return _code + ": " + _message + suffix;
        }
    }
}
