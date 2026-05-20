// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Collections.Generic;

namespace Vianigram.Kernel.Telemetry
{
    /// <summary>
    /// Telemetry sink port. Domain code emits metrics and named events via this
    /// abstraction so production wiring (App Insights, custom backend) stays out
    /// of business logic.
    /// </summary>
    public interface ITelemetry
    {
        void Track(string metric, double value, string unit = null);

        void TrackEvent(string name, IDictionary<string, string> properties = null);
    }
}
