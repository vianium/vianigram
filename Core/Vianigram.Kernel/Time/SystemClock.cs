// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Kernel.Time
{
    /// <summary>
    /// Default <see cref="IClock"/> implementation that delegates to the OS clock.
    /// </summary>
    public sealed class SystemClock : IClock
    {
        public DateTime UtcNow
        {
            get { return DateTime.UtcNow; }
        }

        public DateTimeOffset NowOffset
        {
            get { return DateTimeOffset.Now; }
        }
    }
}
