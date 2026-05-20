// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;

namespace Vianigram.Kernel.Telemetry
{
    /// <summary>
    /// No-op <see cref="ITelemetry"/> for tests and configurations where
    /// telemetry is intentionally disabled.
    /// </summary>
    public sealed class NullTelemetry : ITelemetry
    {
        public static readonly NullTelemetry Instance = new NullTelemetry();

        public void Track(string metric, double value, string unit = null)
        {
        }

        public void TrackEvent(string name, IDictionary<string, string> properties = null)
        {
        }
    }
}
