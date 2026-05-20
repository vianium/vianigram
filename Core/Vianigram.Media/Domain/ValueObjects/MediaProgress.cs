// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

namespace Vianigram.Media.Domain.ValueObjects
{
    /// <summary>
    /// Snapshot of a transfer's progress. Throughput is bytes-per-second over
    /// the most-recent observation window (handler-defined, currently
    /// last-known-completion). UI binds to TransferProgress events for live
    /// updates.
    /// </summary>
    public struct MediaProgress
    {
        public MediaProgress(long bytesLoaded, long bytesTotal, long bytesPerSecond)
            : this()
        {
            BytesLoaded = bytesLoaded;
            BytesTotal = bytesTotal;
            BytesPerSecond = bytesPerSecond;
        }

        public long BytesLoaded { get; private set; }
        public long BytesTotal { get; private set; }
        public long BytesPerSecond { get; private set; }

        /// <summary>
        /// Fraction in [0,1]. Returns 0 when total is unknown so the UI can
        /// fall back to an indeterminate spinner.
        /// </summary>
        public double Fraction
        {
            get
            {
                if (BytesTotal <= 0) return 0.0;
                return (double)BytesLoaded / (double)BytesTotal;
            }
        }

        public override string ToString()
        {
            return BytesLoaded + "/" + BytesTotal + " @ " + BytesPerSecond + " B/s";
        }
    }
}
