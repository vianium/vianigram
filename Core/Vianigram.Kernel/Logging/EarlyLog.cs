// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// EarlyLog.cs — Vianigram.Kernel.Logging
// Pre-DI bootstrap log helper. Inline format; emits via Debug.WriteLine.

using System;
using System.Diagnostics;
using System.Globalization;

namespace Vianigram.Kernel.Logging
{
    /// <summary>
    /// Last-resort logger for code paths that run BEFORE the composition
    /// root is built (e.g. <c>App.OnLaunched</c> entry, exception handlers
    /// in static ctors). Emits the same <c>[HH:MM:SS.fff Component] message</c>
    /// format as <see cref="TimestampedLogger"/> but bypasses DI entirely
    /// and writes directly to <see cref="Debug.WriteLine(string)"/>.
    /// </summary>
    public static class EarlyLog
    {
        public static void Write(string component, string message)
        {
            var c = string.IsNullOrEmpty(component) ? "?" : component;
            var ts = DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
            Debug.WriteLine("[" + ts + " " + c + "] " + (message ?? string.Empty));
        }
    }
}
