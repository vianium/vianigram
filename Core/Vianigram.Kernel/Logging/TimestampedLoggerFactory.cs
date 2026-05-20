// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// TimestampedLoggerFactory.cs — Vianigram.Kernel.Logging
// Default ILoggerFactory wiring: produces TimestampedLogger over an inner ILogger.

using System;

namespace Vianigram.Kernel.Logging
{
    /// <summary>
    /// Default <see cref="ILoggerFactory"/> implementation. Captures a single
    /// underlying <see cref="ILogger"/> sink and hands out
    /// <see cref="TimestampedLogger"/> instances pre-tagged with the requested
    /// component name. Cheap enough that callers can resolve a fresh logger
    /// per class in their constructor.
    /// </summary>
    public sealed class TimestampedLoggerFactory : ILoggerFactory
    {
        private readonly ILogger _inner;

        public TimestampedLoggerFactory(ILogger inner)
        {
            if (inner == null) throw new ArgumentNullException("inner");
            _inner = inner;
        }

        public IComponentLogger ForComponent(string componentName)
        {
            return new TimestampedLogger(_inner, componentName);
        }
    }
}
