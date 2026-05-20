// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Notifications.Domain
{
    /// <summary>
    /// Categories of failure that the Notifications context can surface to
    /// callers without throwing exceptions. Mapped from MTProto rpc errors,
    /// platform sink failures (toast / tile / badge), persistence faults, or
    /// domain invariant violations by the application handlers.
    /// </summary>
    public enum NotificationsErrorKind
    {
        NetworkError = 0,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="NotificationsError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 1,
        NotFound = 2,
        /// <summary>The platform refused to surface the notification (e.g. user denied toast capability).</summary>
        PlatformDenied = 3,
        NotInExpectedState = 4,
        Unknown = 5
    }

    /// <summary>
    /// Structured error type for Notifications. Carries a kind, a stable code
    /// (namespaced "notifications.&lt;kind&gt;"), a human-readable message,
    /// optional FLOOD_WAIT seconds, and an optional underlying
    /// <see cref="Exception"/> for diagnostics. Immutable.
    ///
    /// Mirrors the per-context error pattern documented in
    /// <c>docs/managed-architecture/principles.md</c>: every bounded context
    /// owns its error taxonomy; cross-boundary callers translate via ACL
    /// adapters.
    /// </summary>
    public sealed class NotificationsError
    {
        private readonly NotificationsErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public NotificationsError(
            NotificationsErrorKind kind,
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

        public NotificationsErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static NotificationsError NetworkError(string detail, Exception cause = null)
        {
            return new NotificationsError(NotificationsErrorKind.NetworkError, "notifications.network", detail ?? string.Empty, null, cause);
        }

        public static NotificationsError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new NotificationsError(
                NotificationsErrorKind.FloodWait,
                "notifications.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static NotificationsError NotFound(string detail)
        {
            return new NotificationsError(NotificationsErrorKind.NotFound, "notifications.not_found", detail ?? string.Empty);
        }

        public static NotificationsError PlatformDenied(string detail, Exception cause = null)
        {
            return new NotificationsError(
                NotificationsErrorKind.PlatformDenied,
                "notifications.platform_denied",
                detail ?? string.Empty,
                null,
                cause);
        }

        public static NotificationsError NotInExpectedState(string detail)
        {
            return new NotificationsError(NotificationsErrorKind.NotInExpectedState, "notifications.bad_state", detail ?? string.Empty);
        }

        public static NotificationsError Unknown(string detail, Exception cause = null)
        {
            return new NotificationsError(NotificationsErrorKind.Unknown, "notifications.unknown", detail ?? string.Empty, null, cause);
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
