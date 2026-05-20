// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Search.Domain
{
    /// <summary>
    /// Categories of failure that the Search context can surface to callers
    /// without throwing exceptions. Mapped from MTProto rpc errors
    /// (<c>messages.search</c> / <c>messages.searchGlobal</c> /
    /// <c>contacts.search</c>), validator rejections, or domain invariant
    /// violations by the application handlers.
    /// </summary>
    public enum SearchErrorKind
    {
        Unknown = 0,
        /// <summary>Network transport faulted before the server responded.</summary>
        NetworkError = 1,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="SearchError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 2,
        /// <summary>Query length is below the per-RPC minimum (Telegram requires &gt;= 1 for global, &gt;= 2 for in-chat).</summary>
        QueryTooShort = 3,
        /// <summary>Server returned 0 hits — handlers may surface this OR an empty <c>SearchSession</c>; reserved for explicit "no results" semantics.</summary>
        NoResults = 4,
        /// <summary>Validator rejected the query / cursor / filter (e.g. negative limit, unknown peer key).</summary>
        InvalidValue = 5,
        /// <summary>The supplied peer key references a chat the user is not in or that no longer exists.</summary>
        PeerNotFound = 6,
        /// <summary>The active session was cancelled by the consumer.</summary>
        Cancelled = 7
    }

    /// <summary>
    /// Structured error type for Search. Carries a kind, a stable code
    /// (namespaced "search.&lt;kind&gt;"), a human-readable message, optional
    /// FLOOD_WAIT seconds, and an optional underlying <see cref="Exception"/>
    /// for diagnostics. Immutable.
    ///
    /// Mirrors the per-context error pattern documented in
    /// <c>docs/managed-architecture/principles.md</c>: every bounded context
    /// owns its error taxonomy; cross-boundary callers translate via ACL
    /// adapters.
    /// </summary>
    public sealed class SearchError
    {
        private readonly SearchErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public SearchError(
            SearchErrorKind kind,
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

        public SearchErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static SearchError NetworkError(string detail, Exception cause = null)
        {
            return new SearchError(SearchErrorKind.NetworkError, "search.network", detail ?? string.Empty, null, cause);
        }

        public static SearchError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new SearchError(
                SearchErrorKind.FloodWait,
                "search.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static SearchError QueryTooShort(string detail)
        {
            return new SearchError(SearchErrorKind.QueryTooShort, "search.query_too_short", detail ?? "query too short");
        }

        public static SearchError NoResults(string detail = null)
        {
            return new SearchError(SearchErrorKind.NoResults, "search.no_results", detail ?? "no results");
        }

        public static SearchError InvalidValue(string detail)
        {
            return new SearchError(SearchErrorKind.InvalidValue, "search.invalid_value", detail ?? string.Empty);
        }

        public static SearchError PeerNotFound(string detail)
        {
            return new SearchError(SearchErrorKind.PeerNotFound, "search.peer_not_found", detail ?? string.Empty);
        }

        public static SearchError Cancelled(string detail = null)
        {
            return new SearchError(SearchErrorKind.Cancelled, "search.cancelled", detail ?? "cancelled");
        }

        public static SearchError Unknown(string detail, Exception cause = null)
        {
            return new SearchError(SearchErrorKind.Unknown, "search.unknown", detail ?? string.Empty, null, cause);
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
