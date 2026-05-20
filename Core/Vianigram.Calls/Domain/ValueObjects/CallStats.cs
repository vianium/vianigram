// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Globalization;

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// Snapshot of media-plane telemetry surfaced by
    /// <c>IVoipMediaPort.GetStatsAsync</c>. Carried on
    /// <see cref="Domain.Events.CallStatsUpdated"/> so UI can render audio
    /// levels, signal-quality bars, and packet-loss warnings.
    ///
    /// All values are best-effort sampled; the native VoIP runtime updates
    /// them on its own cadence during an active call. Hosts without a native
    /// media runtime return the default zero-valued sample.
    /// </summary>
    public struct CallStats
    {
        /// <summary>Outbound microphone level [0, 1] (RMS over the last sample window).</summary>
        public float OutboundLevel;
        /// <summary>Inbound speaker level [0, 1] (RMS over the last sample window).</summary>
        public float InboundLevel;
        /// <summary>Smoothed packet loss percentage [0, 1] (peer-side observed).</summary>
        public float PacketLossPercent;
        /// <summary>Round-trip estimate in milliseconds.</summary>
        public int RttMs;
        /// <summary>Currently negotiated bitrate in bits/sec.</summary>
        public int BitrateBps;
        /// <summary>Number of audio underruns since call start.</summary>
        public int Underruns;

        public static CallStats Empty
        {
            get { return new CallStats(); }
        }

        public override string ToString()
        {
            return "stats[lvl_out=" + OutboundLevel.ToString("F2", CultureInfo.InvariantCulture)
                   + " lvl_in=" + InboundLevel.ToString("F2", CultureInfo.InvariantCulture)
                   + " loss=" + PacketLossPercent.ToString("F2", CultureInfo.InvariantCulture)
                   + " rtt=" + RttMs.ToString(CultureInfo.InvariantCulture) + "ms"
                   + " br=" + BitrateBps.ToString(CultureInfo.InvariantCulture)
                   + " underruns=" + Underruns.ToString(CultureInfo.InvariantCulture)
                   + "]";
        }
    }
}
