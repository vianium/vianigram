// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Kernel.Logging
{
    /// <summary>
    /// Minimal logging port. Implementations route messages to specific sinks
    /// (Debug output, file, telemetry) and are wired by the composition root.
    /// </summary>
    public interface ILogger
    {
        void Log(LogLevel level, string message);
    }
}
