// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Privacy.Domain
{
    /// <summary>
    /// Categories of failure that the Privacy context can surface to callers
    /// without throwing exceptions. Mapped from MTProto rpc errors
    /// (<c>account.getPrivacy</c> / <c>account.setPrivacy</c> /
    /// <c>account.getAuthorizations</c> / <c>account.resetAuthorization</c> /
    /// <c>auth.resetAuthorizations</c>), validator rejections, or domain
    /// invariant violations by the application handlers.
    /// </summary>
    public enum PrivacyErrorKind
    {
        Unknown = 0,
        /// <summary>Network transport faulted before the server responded.</summary>
        NetworkError = 1,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="PrivacyError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 2,
        /// <summary>The supplied privacy key / session hash / user id was not recognized by the server.</summary>
        NotFound = 3,
        /// <summary>Validator rejected the input (empty PIN, malformed rule, etc.).</summary>
        InvalidValue = 4,
        /// <summary>The user typed the wrong PIN during verify / disable / change.</summary>
        PasscodeWrong = 5,
        /// <summary>Two PINs were supposed to match (e.g. enable confirm) and did not.</summary>
        PasscodeMismatch = 6,
        /// <summary>Local storage of passcode material failed (read / write / clear).</summary>
        StorageError = 7,
        /// <summary>The current session is not authorized to issue the call.</summary>
        NotAuthenticated = 8,
        /// <summary>Server rejected the call with FRESH_RESET_AUTHORISATION_FORBIDDEN — attempt too soon after login.</summary>
        ResetForbidden = 9,
        /// <summary>The supplied session hash refers to the current session — the server forbids self-reset.</summary>
        CurrentSessionTermination = 10,
        /// <summary>The application boundary cancelled the operation.</summary>
        Cancelled = 11
    }

    /// <summary>
    /// Structured error type for Privacy. Carries a kind, a stable code
    /// (namespaced "privacy.&lt;kind&gt;"), a human-readable message, optional
    /// FLOOD_WAIT seconds, and an optional underlying <see cref="Exception"/>
    /// for diagnostics. Immutable.
    ///
    /// Mirrors the per-context error pattern documented in
    /// <c>docs/managed-architecture/principles.md</c>: every bounded context
    /// owns its error taxonomy; cross-boundary callers translate via ACL
    /// adapters.
    /// </summary>
    public sealed class PrivacyError
    {
        private readonly PrivacyErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public PrivacyError(
            PrivacyErrorKind kind,
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

        public PrivacyErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static PrivacyError NetworkError(string detail, Exception cause = null)
        {
            return new PrivacyError(PrivacyErrorKind.NetworkError, "privacy.network", detail ?? string.Empty, null, cause);
        }

        public static PrivacyError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new PrivacyError(
                PrivacyErrorKind.FloodWait,
                "privacy.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static PrivacyError NotFound(string detail)
        {
            return new PrivacyError(PrivacyErrorKind.NotFound, "privacy.not_found", detail ?? "not found");
        }

        public static PrivacyError InvalidValue(string detail)
        {
            return new PrivacyError(PrivacyErrorKind.InvalidValue, "privacy.invalid_value", detail ?? string.Empty);
        }

        public static PrivacyError PasscodeWrong(string detail = null)
        {
            return new PrivacyError(PrivacyErrorKind.PasscodeWrong, "privacy.passcode_wrong", detail ?? "wrong passcode");
        }

        public static PrivacyError PasscodeMismatch(string detail = null)
        {
            return new PrivacyError(PrivacyErrorKind.PasscodeMismatch, "privacy.passcode_mismatch", detail ?? "passcodes do not match");
        }

        public static PrivacyError StorageError(string detail, Exception cause = null)
        {
            return new PrivacyError(PrivacyErrorKind.StorageError, "privacy.storage", detail ?? string.Empty, null, cause);
        }

        public static PrivacyError NotAuthenticated(string detail = null)
        {
            return new PrivacyError(PrivacyErrorKind.NotAuthenticated, "privacy.not_authenticated", detail ?? "not authenticated");
        }

        public static PrivacyError ResetForbidden(string detail = null)
        {
            return new PrivacyError(PrivacyErrorKind.ResetForbidden, "privacy.reset_forbidden", detail ?? "FRESH_RESET_AUTHORISATION_FORBIDDEN");
        }

        public static PrivacyError CurrentSessionTermination(string detail = null)
        {
            return new PrivacyError(PrivacyErrorKind.CurrentSessionTermination, "privacy.current_session", detail ?? "cannot terminate the current session");
        }

        public static PrivacyError Cancelled(string detail = null)
        {
            return new PrivacyError(PrivacyErrorKind.Cancelled, "privacy.cancelled", detail ?? "cancelled");
        }

        public static PrivacyError Unknown(string detail, Exception cause = null)
        {
            return new PrivacyError(PrivacyErrorKind.Unknown, "privacy.unknown", detail ?? string.Empty, null, cause);
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
