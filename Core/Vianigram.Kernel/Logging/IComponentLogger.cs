// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// IComponentLogger.cs — Vianigram.Kernel.Logging
// Component-scoped logger producing lines in [HH:MM:SS.fff Component] message format (UTC).

namespace Vianigram.Kernel.Logging
{
    /// <summary>
    /// Component-scoped logger. Each method emits a line shaped
    /// <c>[HH:MM:SS.fff Component] message</c> (UTC) through the
    /// underlying <see cref="ILogger"/> sink. Resolve via
    /// <see cref="ILoggerFactory.ForComponent"/>; do NOT instantiate
    /// directly outside the composition root.
    /// </summary>
    public interface IComponentLogger
    {
        void Trace(string message);
        void Debug(string message);
        void Info(string message);
        void Warn(string message);
        void Error(string message);
        void Fatal(string message);

        // Convenience: surface duration explicitly.
        void Info(string message, long elapsedMs);
    }
}
