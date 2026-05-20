// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.SecretChats.Domain
{
    /// <summary>
    /// Categories of failure that the SecretChats context can surface to
    /// callers without throwing exceptions. Mapped from MTProto rpc errors,
    /// crypto failures, persistence failures, or domain invariant violations
    /// by the application handlers.
    /// </summary>
    public enum SecretChatErrorKind
    {
        NetworkError = 0,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="SecretChatError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 1,
        /// <summary>DH validation, AES-IGE failure, or random-bytes shortfall.</summary>
        InvalidKey = 2,
        /// <summary>Locally computed key fingerprint disagrees with the peer's claimed value — abort the session.</summary>
        FingerprintMismatch = 3,
        ChatNotFound = 4,
        NotInExpectedState = 5,
        /// <summary>Wire decode error, unsupported TL constructor, malformed inner envelope.</summary>
        ProtocolError = 6,
        Unknown = 7
    }

    /// <summary>
    /// Structured error type for SecretChats. Carries a kind, a stable code
    /// (namespaced "secret_chats.&lt;kind&gt;"), a human-readable message,
    /// optional FLOOD_WAIT seconds, and an optional underlying
    /// <see cref="Exception"/> for diagnostics. Immutable.
    /// </summary>
    public sealed class SecretChatError
    {
        private readonly SecretChatErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public SecretChatError(
            SecretChatErrorKind kind,
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

        public SecretChatErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static SecretChatError NetworkError(string detail, Exception cause = null)
        {
            return new SecretChatError(SecretChatErrorKind.NetworkError, "secret_chats.network", detail ?? string.Empty, null, cause);
        }

        public static SecretChatError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new SecretChatError(
                SecretChatErrorKind.FloodWait,
                "secret_chats.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static SecretChatError InvalidKey(string detail, Exception cause = null)
        {
            return new SecretChatError(SecretChatErrorKind.InvalidKey, "secret_chats.invalid_key", detail ?? string.Empty, null, cause);
        }

        public static SecretChatError FingerprintMismatch(string detail)
        {
            return new SecretChatError(SecretChatErrorKind.FingerprintMismatch, "secret_chats.fingerprint_mismatch", detail ?? string.Empty);
        }

        public static SecretChatError ChatNotFound(string detail)
        {
            return new SecretChatError(SecretChatErrorKind.ChatNotFound, "secret_chats.chat_not_found", detail ?? string.Empty);
        }

        public static SecretChatError NotInExpectedState(string detail)
        {
            return new SecretChatError(SecretChatErrorKind.NotInExpectedState, "secret_chats.bad_state", detail ?? string.Empty);
        }

        public static SecretChatError ProtocolError(string detail, Exception cause = null)
        {
            return new SecretChatError(SecretChatErrorKind.ProtocolError, "secret_chats.protocol_error", detail ?? string.Empty, null, cause);
        }

        public static SecretChatError Unknown(string detail, Exception cause = null)
        {
            return new SecretChatError(SecretChatErrorKind.Unknown, "secret_chats.unknown", detail ?? string.Empty, null, cause);
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
