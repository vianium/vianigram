// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Stickers.Domain
{
    /// <summary>
    /// Categories of failure that the Stickers context can surface to callers
    /// without throwing exceptions. Mapped from MTProto rpc errors,
    /// persistence failures, or domain invariant violations by the application
    /// handlers.
    /// </summary>
    public enum StickersErrorKind
    {
        NetworkError = 0,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="StickersError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 1,
        NotFound = 2,
        AlreadyInstalled = 3,
        MaxSetsReached = 4,
        NotInExpectedState = 5,
        Unknown = 6
    }

    /// <summary>
    /// Structured error type for Stickers. Carries a kind, a stable code
    /// (namespaced "stickers.&lt;kind&gt;"), a human-readable message, optional
    /// FLOOD_WAIT seconds, and an optional underlying <see cref="Exception"/>
    /// for diagnostics. Immutable.
    ///
    /// Mirrors the per-context error pattern documented in
    /// <c>docs/managed-architecture/principles.md</c>: every bounded context
    /// owns its error taxonomy; cross-boundary callers translate via ACL
    /// adapters.
    /// </summary>
    public sealed class StickersError
    {
        private readonly StickersErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public StickersError(
            StickersErrorKind kind,
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

        public StickersErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static StickersError NetworkError(string detail, Exception cause = null)
        {
            return new StickersError(StickersErrorKind.NetworkError, "stickers.network", detail ?? string.Empty, null, cause);
        }

        public static StickersError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new StickersError(
                StickersErrorKind.FloodWait,
                "stickers.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static StickersError NotFound(string detail)
        {
            return new StickersError(StickersErrorKind.NotFound, "stickers.not_found", detail ?? string.Empty);
        }

        public static StickersError AlreadyInstalled(string detail)
        {
            return new StickersError(StickersErrorKind.AlreadyInstalled, "stickers.already_installed", detail ?? string.Empty);
        }

        public static StickersError MaxSetsReached(string detail)
        {
            return new StickersError(StickersErrorKind.MaxSetsReached, "stickers.max_sets", detail ?? string.Empty);
        }

        public static StickersError NotInExpectedState(string detail)
        {
            return new StickersError(StickersErrorKind.NotInExpectedState, "stickers.bad_state", detail ?? string.Empty);
        }

        public static StickersError Unknown(string detail, Exception cause = null)
        {
            return new StickersError(StickersErrorKind.Unknown, "stickers.unknown", detail ?? string.Empty, null, cause);
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
