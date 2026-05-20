// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain
{
    /// <summary>
    /// Categories of failure that the Settings context can surface to callers
    /// without throwing exceptions. Mapped from MTProto rpc errors
    /// (langpack.* / account.getContentSettings), preferences-store faults,
    /// validator rejections, or domain invariant violations by the application
    /// handlers.
    /// </summary>
    public enum SettingsErrorKind
    {
        Unknown = 0,
        NotFound = 1,
        /// <summary>Validator rejected the supplied value (range / enum / type-shape).</summary>
        InvalidValue = 2,
        NetworkError = 3,
        /// <summary>FLOOD_WAIT_X — server forbids retry until <see cref="SettingsError.RetryAfterSeconds"/> elapses.</summary>
        FloodWait = 4,
        /// <summary>Underlying preferences store (LocalSettings, file, in-memory) failed.</summary>
        StorageError = 5,
        /// <summary>Existing stored value cannot be deserialized into the requested type.</summary>
        TypeMismatch = 6
    }

    /// <summary>
    /// Structured error type for Settings. Carries a kind, a stable code
    /// (namespaced "settings.&lt;kind&gt;"), a human-readable message, optional
    /// FLOOD_WAIT seconds, and an optional underlying <see cref="Exception"/>
    /// for diagnostics. Immutable.
    ///
    /// Mirrors the per-context error pattern documented in
    /// <c>docs/managed-architecture/principles.md</c>: every bounded context
    /// owns its error taxonomy; cross-boundary callers translate via ACL
    /// adapters.
    /// </summary>
    public sealed class SettingsError
    {
        private readonly SettingsErrorKind _kind;
        private readonly string _code;
        private readonly string _message;
        private readonly int? _retryAfterSeconds;
        private readonly Exception _cause;

        public SettingsError(
            SettingsErrorKind kind,
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

        public SettingsErrorKind Kind { get { return _kind; } }
        public string Code { get { return _code; } }
        public string Message { get { return _message; } }
        public int? RetryAfterSeconds { get { return _retryAfterSeconds; } }
        public Exception Cause { get { return _cause; } }

        public static SettingsError NotFound(string detail)
        {
            return new SettingsError(SettingsErrorKind.NotFound, "settings.not_found", detail ?? string.Empty);
        }

        public static SettingsError InvalidValue(string detail)
        {
            return new SettingsError(SettingsErrorKind.InvalidValue, "settings.invalid_value", detail ?? string.Empty);
        }

        public static SettingsError NetworkError(string detail, Exception cause = null)
        {
            return new SettingsError(SettingsErrorKind.NetworkError, "settings.network", detail ?? string.Empty, null, cause);
        }

        public static SettingsError FloodWait(int retryAfterSeconds, string detail = null)
        {
            return new SettingsError(
                SettingsErrorKind.FloodWait,
                "settings.flood_wait",
                detail ?? ("FLOOD_WAIT_" + retryAfterSeconds),
                retryAfterSeconds);
        }

        public static SettingsError StorageError(string detail, Exception cause = null)
        {
            return new SettingsError(SettingsErrorKind.StorageError, "settings.storage", detail ?? string.Empty, null, cause);
        }

        public static SettingsError TypeMismatch(string detail)
        {
            return new SettingsError(SettingsErrorKind.TypeMismatch, "settings.type_mismatch", detail ?? string.Empty);
        }

        public static SettingsError Unknown(string detail, Exception cause = null)
        {
            return new SettingsError(SettingsErrorKind.Unknown, "settings.unknown", detail ?? string.Empty, null, cause);
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
