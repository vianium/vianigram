// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Sync.Ports.Inbound
{
    /// <summary>
    /// Lightweight payload for the <see cref="ISyncApi.UpdatesApplied"/> event.
    /// Carries only the count and timestamp; subscribers wanting the actual
    /// derived events should subscribe directly to those types on IEventBus.
    /// </summary>
    public sealed class UpdatesAppliedEventArgs : EventArgs
    {
        public UpdatesAppliedEventArgs(int eventCount, DateTime timestampUtc)
        {
            EventCount = eventCount;
            TimestampUtc = timestampUtc;
        }

        public int EventCount { get; private set; }
        public DateTime TimestampUtc { get; private set; }
    }
}
