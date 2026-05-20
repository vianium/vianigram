// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Calls.Domain
{
    /// <summary>
    /// Categories of failure the Calls context can surface to callers
    /// without throwing exceptions. Mapped from MTProto rpc errors,
    /// peer-state assertions, native voip-plane callbacks, and domain
    /// invariant violations by the application handlers.
    /// </summary>
    public enum CallErrorKind
    {
        NetworkError = 0,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="CallError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 1,
        /// <summary>PARTICIPANT_VERSION_OUTDATED, peer has no compatible libtgvoip layer.</summary>
        ProtocolMismatch = 2,
        /// <summary>CALL_PROTOCOL_INCOMPATIBLE / CONNECTION_NOT_INITED.</summary>
        ProtocolError = 3,
        /// <summary>USER_PRIVACY_RESTRICTED, USER_DELETED, USER_BLOCKED — peer cannot be reached.</summary>
        ParticipantUnavailable = 4,
        /// <summary>The peer is on another call right now (CALL_ALREADY_DECLINED variant).</summary>
        Busy = 5,
        /// <summary>CALL_ALREADY_ACCEPTED — local already has an active call (one-active invariant).</summary>
        AlreadyInCall = 6,
        /// <summary>CallId not present in the repository.</summary>
        CallNotFound = 7,
        /// <summary>State machine forbids the requested transition (e.g. accept on Discarded).</summary>
        NotInExpectedState = 8,
        /// <summary>Locally computed key fingerprint disagrees with the peer's claimed value — security abort.</summary>
        FingerprintMismatch = 9,
        /// <summary>The native VoIP plane reported a non-recoverable failure (no audio device, codec missing).</summary>
        MediaPlaneFailed = 10,
        Unknown = 11
    }

    /// <summary>
    /// Structured error type for Calls. Carries a kind, a stable code
    /// (namespaced "calls.&lt;kind&gt;"), a human-readable message,
    /// optional FLOOD_WAIT seconds, and an optional underlying
    /// <see cref="Exception"/> for diagnostics. Immutable.
    /// </summary>
    public sealed class CallError
    {
        private readonly CallErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public CallError(
            CallErrorKind kind,
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

        public CallErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static CallError NetworkError(string detail, Exception cause = null)
        {
            return new CallError(CallErrorKind.NetworkError, "calls.network", detail ?? string.Empty, null, cause);
        }

        public static CallError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new CallError(
                CallErrorKind.FloodWait,
                "calls.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static CallError ProtocolMismatch(string detail)
        {
            return new CallError(CallErrorKind.ProtocolMismatch, "calls.protocol_mismatch", detail ?? string.Empty);
        }

        public static CallError ProtocolError(string detail, Exception cause = null)
        {
            return new CallError(CallErrorKind.ProtocolError, "calls.protocol_error", detail ?? string.Empty, null, cause);
        }

        public static CallError ParticipantUnavailable(string detail)
        {
            return new CallError(CallErrorKind.ParticipantUnavailable, "calls.participant_unavailable", detail ?? string.Empty);
        }

        public static CallError Busy(string detail = null)
        {
            return new CallError(CallErrorKind.Busy, "calls.busy", detail ?? string.Empty);
        }

        public static CallError AlreadyInCall(string detail = null)
        {
            return new CallError(CallErrorKind.AlreadyInCall, "calls.already_in_call", detail ?? string.Empty);
        }

        public static CallError CallNotFound(string detail)
        {
            return new CallError(CallErrorKind.CallNotFound, "calls.not_found", detail ?? string.Empty);
        }

        public static CallError NotInExpectedState(string detail)
        {
            return new CallError(CallErrorKind.NotInExpectedState, "calls.bad_state", detail ?? string.Empty);
        }

        public static CallError FingerprintMismatch(string detail)
        {
            return new CallError(CallErrorKind.FingerprintMismatch, "calls.fingerprint_mismatch", detail ?? string.Empty);
        }

        public static CallError MediaPlaneFailed(string detail, Exception cause = null)
        {
            return new CallError(CallErrorKind.MediaPlaneFailed, "calls.media_plane", detail ?? string.Empty, null, cause);
        }

        public static CallError Unknown(string detail, Exception cause = null)
        {
            return new CallError(CallErrorKind.Unknown, "calls.unknown", detail ?? string.Empty, null, cause);
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
