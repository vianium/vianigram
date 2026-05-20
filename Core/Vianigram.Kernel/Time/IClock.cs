// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Kernel.Time
{
    /// <summary>
    /// Abstraction over the system clock so domain code remains testable.
    /// Always prefer injection of <see cref="IClock"/> over direct <see cref="DateTime"/> access.
    /// </summary>
    public interface IClock
    {
        DateTime UtcNow { get; }
        DateTimeOffset NowOffset { get; }
    }
}
