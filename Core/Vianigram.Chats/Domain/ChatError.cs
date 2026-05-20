// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Chats.Domain
{
    /// <summary>
    /// Categories of failure that the Chats context can surface to callers without
    /// throwing exceptions. Mapped from RPC errors, persistence failures, or domain
    /// invariant violations by the application handlers.
    /// </summary>
    public enum ChatErrorKind
    {
        PeerNotFound = 0,
        AccessDenied = 1,
        NotInExpectedState = 2,
        NetworkError = 3,
        Unknown = 4
    }

    /// <summary>
    /// Structured error type for Chats. Carries a kind, a stable code (namespaced
    /// "chats.&lt;kind&gt;.&lt;detail&gt;"), a human-readable message, and an optional
    /// underlying <see cref="Exception"/> for diagnostics. Immutable.
    ///
    /// Distinct from the kernel's general <see cref="Vianigram.Kernel.Result.Error"/>
    /// so consumers can pattern-match on <see cref="Kind"/> without parsing strings.
    /// </summary>
    public sealed class ChatError
    {
        private readonly ChatErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly Exception _cause;

        public ChatError(ChatErrorKind kind, string code, string message, Exception cause = null)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("code required", "code");
            _kind = kind;
            _code = code;
            _message = message ?? string.Empty;
            _cause = cause;
        }

        public ChatErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public Exception Cause { get { return _cause; } }

        public static ChatError PeerNotFound(string detail)
        {
            return new ChatError(ChatErrorKind.PeerNotFound, "chats.peer_not_found", detail ?? string.Empty);
        }

        public static ChatError AccessDenied(string detail)
        {
            return new ChatError(ChatErrorKind.AccessDenied, "chats.access_denied", detail ?? string.Empty);
        }

        public static ChatError NotInExpectedState(string detail)
        {
            return new ChatError(ChatErrorKind.NotInExpectedState, "chats.bad_state", detail ?? string.Empty);
        }

        public static ChatError NetworkError(string detail, Exception cause = null)
        {
            return new ChatError(ChatErrorKind.NetworkError, "chats.network", detail ?? string.Empty, cause);
        }

        public static ChatError Unknown(string detail, Exception cause = null)
        {
            return new ChatError(ChatErrorKind.Unknown, "chats.unknown", detail ?? string.Empty, cause);
        }

        public override string ToString()
        {
            if (_cause != null)
                return _code + ": " + _message + " (cause: " + _cause.GetType().Name + ": " + _cause.Message + ")";
            return _code + ": " + _message;
        }
    }
}
