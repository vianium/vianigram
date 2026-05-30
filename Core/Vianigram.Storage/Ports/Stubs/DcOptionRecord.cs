// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Storage.Ports.Stubs
{
    /// <summary>
    /// One entry from a <c>help.getConfig</c> <c>dc_options</c> array,
    /// persisted so subsequent cold starts can race against the full
    /// up-to-date list of DC IPs instead of the (often single-host)
    /// hardcoded bootstrap plan.
    ///
    /// Field semantics mirror the TL definition
    /// <c>dcOption#18b7a10d flags:# ipv6:flags.0?true media_only:flags.1?true
    /// tcpo_only:flags.2?true cdn:flags.3?true static:flags.4?true
    /// this_port_only:flags.5?true id:int ip_address:string port:int
    /// secret:flags.10?bytes</c> documented at
    /// <see href="https://core.telegram.org/api/datacenter"/>.
    /// </summary>
    public sealed class DcOptionRecord
    {
        public int DcId { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }
        public bool Ipv6 { get; private set; }
        public bool MediaOnly { get; private set; }
        public bool TcpoOnly { get; private set; }
        public bool Cdn { get; private set; }
        public bool StaticFlag { get; private set; }
        public bool ThisPortOnly { get; private set; }
        public byte[] Secret { get; private set; }
        public DateTime FetchedAt { get; private set; }

        public DcOptionRecord(
            int dcId,
            string host,
            int port,
            bool ipv6,
            bool mediaOnly,
            bool tcpoOnly,
            bool cdn,
            bool staticFlag,
            bool thisPortOnly,
            byte[] secret,
            DateTime fetchedAt)
        {
            if (string.IsNullOrEmpty(host)) throw new ArgumentNullException("host");
            if (port <= 0 || port > 65535) throw new ArgumentOutOfRangeException("port");

            DcId = dcId;
            Host = host;
            Port = port;
            Ipv6 = ipv6;
            MediaOnly = mediaOnly;
            TcpoOnly = tcpoOnly;
            Cdn = cdn;
            StaticFlag = staticFlag;
            ThisPortOnly = thisPortOnly;
            Secret = secret;
            FetchedAt = fetchedAt;
        }
    }
}
