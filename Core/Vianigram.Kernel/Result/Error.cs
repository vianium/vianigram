// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Kernel.Result
{
    /// <summary>
    /// Structured error: namespaced code + message + optional cause chain.
    /// Immutable. Avoids exceptions crossing bounded-context boundaries.
    /// </summary>
    public sealed class Error
    {
        public string Code { get; private set; }
        public string Message { get; private set; }
        public Exception Cause { get; private set; }

        public Error(string code, string message, Exception cause = null)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("code required", "code");
            Code = code;
            Message = message ?? string.Empty;
            Cause = cause;
        }

        public static Error From(Exception ex, string code = null)
        {
            if (ex == null) return new Error(code ?? "kernel.exception.null", "null exception");
            return new Error(code ?? "kernel.exception", ex.Message, ex);
        }

        public override string ToString()
        {
            if (Cause != null)
                return Code + ": " + Message + " (cause: " + Cause.GetType().Name + ": " + Cause.Message + ")";
            return Code + ": " + Message;
        }
    }
}
