// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Infrastructure;

namespace Vianigram.SmokeTests.Tests
{
    public static class CallsTlSmokeTest
    {
        public static Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var entries = new List<TestEntry>();

            try
            {
                byte[] payload = BuildEstablishedPhoneCall();
                TlDecoder.DecodedPhoneCall decoded = TlDecoder.DecodePhoneCall(payload);
                IList<CallEndpoint> endpoints = decoded.Endpoints ?? new CallEndpoint[0];

                bool ok = decoded.Shape == TlDecoder.DecodedPhoneCall.ShapeKind.Established
                    && decoded.CallId == 123456789L
                    && endpoints.Count == 1
                    && endpoints[0].Id == 42L
                    && endpoints[0].Ip == "149.154.175.50"
                    && endpoints[0].Port == 443
                    && endpoints[0].PeerTag != null
                    && endpoints[0].PeerTag.Length == 16;

                entries.Add(new TestEntry
                {
                    Suite = "Calls",
                    Name = "phoneCall decodes phoneConnection reflector",
                    Passed = ok,
                    Detail = ok
                        ? "decoded phoneConnection#9cc123c7 endpoint"
                        : "decoded shape=" + decoded.Shape + " endpoints=" + endpoints.Count
                });
            }
            catch (Exception ex)
            {
                entries.Add(new TestEntry
                {
                    Suite = "Calls",
                    Name = "phoneCall decodes phoneConnection reflector",
                    Passed = false,
                    Detail = ex.GetType().Name + ": " + ex.Message
                });
            }

            return Task.FromResult(entries);
        }

        private static byte[] BuildEstablishedPhoneCall()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(TlDecoder.CtorPhonePhoneCall);
                w.Write(TlDecoder.CtorPhoneCall);
                w.Write(0); // flags
                w.Write(123456789L); // id
                w.Write(987654321L); // access_hash
                w.Write(1); // date
                w.Write(1001L); // admin_id
                w.Write(1002L); // participant_id
                WriteBytes(w, CreateBytes(256, 3)); // g_a_or_b
                w.Write(unchecked((long)0x1122334455667788L)); // key_fingerprint
                WritePhoneCallProtocol(w);
                WriteConnectionVector(w);
                w.Write(2); // start_date

                w.Write(TlDecoder.CtorVector); // users
                w.Write(0);
                return ms.ToArray();
            }
        }

        private static void WritePhoneCallProtocol(BinaryWriter w)
        {
            w.Write(TlDecoder.CtorPhoneCallProtocol);
            w.Write(1 << 1); // udp_reflector
            w.Write(CallProtocol.MinSupportedLayer);
            w.Write(CallProtocol.MaxSupportedLayer);
            w.Write(TlDecoder.CtorVector);
            w.Write(1);
            WriteString(w, "2.7.7");
        }

        private static void WriteConnectionVector(BinaryWriter w)
        {
            w.Write(TlDecoder.CtorVector);
            w.Write(1);
            w.Write(TlDecoder.CtorPhoneConnection);
            w.Write(0); // flags
            w.Write(42L); // id
            WriteString(w, "149.154.175.50");
            WriteString(w, "");
            w.Write(443);
            WriteBytes(w, CreateBytes(16, 9));
        }

        private static byte[] CreateBytes(int count, int seed)
        {
            byte[] bytes = new byte[count];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)((seed + i) & 0xFF);
            }
            return bytes;
        }

        private static void WriteString(BinaryWriter w, string value)
        {
            byte[] bytes = string.IsNullOrEmpty(value)
                ? new byte[0]
                : Encoding.UTF8.GetBytes(value);
            WriteBytes(w, bytes);
        }

        private static void WriteBytes(BinaryWriter w, byte[] data)
        {
            if (data == null) data = new byte[0];
            int len = data.Length;
            int padding;
            if (len < 254)
            {
                w.Write((byte)len);
                w.Write(data);
                padding = (4 - ((len + 1) % 4)) % 4;
            }
            else
            {
                w.Write((byte)254);
                w.Write((byte)(len & 0xFF));
                w.Write((byte)((len >> 8) & 0xFF));
                w.Write((byte)((len >> 16) & 0xFF));
                w.Write(data);
                padding = (4 - (len % 4)) % 4;
            }
            for (int i = 0; i < padding; i++) w.Write((byte)0);
        }
    }
}
