// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Result;

namespace Vianigram.Sync.Domain.Errors
{
    /// <summary>
    /// Sync-context error codes. All Result&lt;T, SyncError&gt; failures funnel
    /// through here so callers never see free-form strings.
    ///
    /// Codes are namespaced "sync.&lt;area&gt;.&lt;condition&gt;" per the
    /// telemetry naming convention.
    /// </summary>
    public sealed class SyncError
    {
        public const string TransportFailure = "sync.transport.failure";
        public const string TlDecodeFailure = "sync.tl.decode_failure";
        public const string TlEncodeFailure = "sync.tl.encode_failure";
        public const string GapUnresolved = "sync.gap.unresolved";
        public const string GapBufferOverflow = "sync.gap.buffer_overflow";
        public const string CursorPersistFailure = "sync.cursor.persist_failure";
        public const string CursorLoadFailure = "sync.cursor.load_failure";
        public const string AlreadyBootstrapped = "sync.bootstrap.already_bootstrapped";
        public const string NotBootstrapped = "sync.bootstrap.not_bootstrapped";
        public const string LoopAlreadyRunning = "sync.loop.already_running";
        public const string Cancelled = "sync.cancelled";
        public const string FloodWait = "sync.flood_wait";
        public const string AuthRequired = "sync.auth.required";
        public const string ChannelDifferenceTooLong = "sync.channel.difference_too_long";
        public const string Unknown = "sync.unknown";

        public string Code { get; private set; }
        public string Message { get; private set; }
        public Exception Cause { get; private set; }
        public int FloodWaitSeconds { get; private set; }

        private SyncError(string code, string message, Exception cause, int floodWaitSeconds)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("code required", "code");
            Code = code;
            Message = message ?? string.Empty;
            Cause = cause;
            FloodWaitSeconds = floodWaitSeconds;
        }

        public static SyncError Make(string code, string message, Exception cause = null)
        {
            return new SyncError(code, message, cause, 0);
        }

        public static SyncError Flood(int seconds, string method)
        {
            return new SyncError(FloodWait,
                "flood_wait " + seconds + "s on " + (method ?? "?"),
                null,
                seconds);
        }

        public static SyncError From(Exception ex, string code = null)
        {
            if (ex == null) return new SyncError(code ?? Unknown, "null exception", null, 0);
            return new SyncError(code ?? Unknown, ex.Message, ex, 0);
        }

        /// <summary>
        /// Bridge to Vianigram.Kernel.Result.Error for ports/handlers that return
        /// the kernel-level Error type (e.g. infrastructure adapters that pre-date
        /// SyncError).
        /// </summary>
        public Error ToKernelError()
        {
            return new Error(Code, Message, Cause);
        }

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }
}
