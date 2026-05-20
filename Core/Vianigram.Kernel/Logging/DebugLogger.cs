// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// DebugLogger.cs — Vianigram.Kernel.Logging
// Sink for the new logging convention. Emits messages verbatim; formatting is
// handled by TimestampedLogger above this layer.

using System.Diagnostics;

namespace Vianigram.Kernel.Logging
{
    /// <summary>
    /// Routes log entries to <see cref="System.Diagnostics.Debug.WriteLine(string)"/>.
    /// Suitable for development; in Release builds, Debug.WriteLine is compiled out.
    ///
    /// This class is the SINK only. The canonical
    /// <c>[HH:MM:SS.fff Component] message</c> prefix is produced by
    /// <see cref="TimestampedLogger"/> above this layer; <see cref="DebugLogger"/>
    /// itself emits the message verbatim. Any caller passing a raw
    /// pre-formatted string sees it unchanged. There is no <c>[Level]</c>
    /// prefix — severity is encoded in the call site (the level argument is
    /// filtered by <see cref="_minLevel"/>) and the line shape is
    /// component-structured rather than level-structured.
    /// </summary>
    public sealed class DebugLogger : ILogger
    {
        private readonly LogLevel _minLevel;

        public DebugLogger() : this(LogLevel.Trace)
        {
        }

        public DebugLogger(LogLevel minLevel)
        {
            _minLevel = minLevel;
        }

        public void Log(LogLevel level, string message)
        {
            if ((int)level < (int)_minLevel) return;
            Debug.WriteLine(message ?? string.Empty);
        }
    }
}
