// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// TimestampedLogger.cs — Vianigram.Kernel.Logging
// IComponentLogger impl emitting [HH:MM:SS.fff Component] message lines (UTC).

using System;
using System.Globalization;
using System.Text;

namespace Vianigram.Kernel.Logging
{
    /// <summary>
    /// Concrete <see cref="IComponentLogger"/>. Wraps an underlying
    /// <see cref="ILogger"/> sink, formatting each message as
    /// <c>[HH:MM:SS.fff Component] message</c> in UTC. Severity is preserved
    /// as the <see cref="LogLevel"/> argument passed to the inner sink; the
    /// formatted line itself does NOT include the level token (the new
    /// convention is structured by component, not by level).
    /// </summary>
    public sealed class TimestampedLogger : IComponentLogger
    {
        private readonly ILogger _inner;
        private readonly string _component;

        public TimestampedLogger(ILogger inner, string componentName)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
            _component = string.IsNullOrEmpty(componentName) ? "?" : componentName;
        }

        public void Trace(string message) { Emit(LogLevel.Trace, message); }
        public void Debug(string message) { Emit(LogLevel.Debug, message); }
        public void Info(string message) { Emit(LogLevel.Info, message); }
        public void Warn(string message) { Emit(LogLevel.Warn, message); }
        public void Error(string message) { Emit(LogLevel.Error, message); }
        public void Fatal(string message) { Emit(LogLevel.Fatal, message); }

        public void Info(string message, long elapsedMs)
        {
            var body = (message ?? string.Empty) + " elapsed=" + elapsedMs.ToString(CultureInfo.InvariantCulture) + "ms";
            Emit(LogLevel.Info, body);
        }

        private void Emit(LogLevel level, string message)
        {
            // Single allocation per call: build "[HH:MM:SS.fff Component] message".
            var sb = new StringBuilder(32 + _component.Length + (message != null ? message.Length : 0));
            sb.Append('[');
            sb.Append(DateTime.UtcNow.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture));
            sb.Append(' ');
            sb.Append(_component);
            sb.Append("] ");
            if (message != null) sb.Append(message);
            _inner.Log(level, sb.ToString());
        }
    }
}
