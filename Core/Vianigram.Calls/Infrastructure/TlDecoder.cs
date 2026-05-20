// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the phone.* responses and
    /// updates this context consumes. Mirrors <see cref="TlEncoder"/>'s
    /// scope: the layer 214 generated header (<c>tl_layer_214.h</c>)
    /// does cover these constructors but the per-context tradition for
    /// hand-written wire payloads is preserved here for symmetry; a future
    /// revision may switch to the codegen path.
    ///
    /// Outer phone.PhoneCall constructors:
    ///   * phone.phoneCall#ec82e140    phone_call:PhoneCall users:Vector&lt;User&gt; = phone.PhoneCall;
    ///
    /// PhoneCall constructors (the embedded <c>phone_call</c> field):
    ///   * phoneCallEmpty#5366c915     id:long = PhoneCall;
    ///   * phoneCallWaiting#c5226f17   flags:# video:flags.6?true id:long access_hash:long date:int admin_id:long participant_id:long protocol:PhoneCallProtocol receive_date:flags.0?int = PhoneCall;
    ///   * phoneCallRequested#14b0ed0c flags:# video:flags.6?true id:long access_hash:long date:int admin_id:long participant_id:long g_a_hash:bytes protocol:PhoneCallProtocol = PhoneCall;
    ///   * phoneCallAccepted#3660c311  flags:# video:flags.6?true id:long access_hash:long date:int admin_id:long participant_id:long g_b:bytes protocol:PhoneCallProtocol = PhoneCall;
    ///   * phoneCall#30535af5          flags:# p2p_allowed:flags.5?true video:flags.6?true custom_parameters:flags.7?DataJSON conference_supported:flags.8?true id:long access_hash:long date:int admin_id:long participant_id:long g_a_or_b:bytes key_fingerprint:long protocol:PhoneCallProtocol connections:Vector&lt;PhoneConnection&gt; start_date:int custom_parameters:flags.7?DataJSON = PhoneCall;
    ///   * phoneCall#967f7c67          legacy layer shape without custom_parameters.
    ///   * phoneCallDiscarded#50ca4de1 flags:# need_rating:flags.2?true need_debug:flags.3?true video:flags.6?true id:long reason:flags.0?PhoneCallDiscardReason duration:flags.1?int = PhoneCall;
    ///
    /// PhoneConnection variants:
    ///   * phoneConnection#9cc123c7    flags:# tcp:flags.0?true id:long ip:string ipv6:string port:int peer_tag:bytes = PhoneConnection;
    ///   * phoneConnectionWebrtc#635fe375 flags:# turn:flags.0?true stun:flags.1?true id:long ip:string ipv6:string port:int username:string password:string = PhoneConnection;
    ///
    /// PhoneCallDiscardReason variants surface as bare ctor ids on
    /// <see cref="DecodedPhoneCall.DiscardReasonCtor"/>; the application
    /// layer maps them to <see cref="DiscardReason"/>.
    /// </summary>
    internal static class TlDecoder
    {
        // ---- outer wrappers ------------------------------------------------
        public const uint CtorPhonePhoneCall = 0xec82e140;

        // ---- PhoneCall variants --------------------------------------------
        public const uint CtorPhoneCallEmpty = 0x5366c915;
        public const uint CtorPhoneCallWaiting = 0xc5226f17;
        public const uint CtorPhoneCallRequested = 0x14b0ed0c;
        public const uint CtorPhoneCallAccepted = 0x3660c311;
        public const uint CtorPhoneCall = 0x30535af5;
        public const uint CtorPhoneCallLegacy = 0x967f7c67;
        public const uint CtorPhoneCallDiscarded = 0x50ca4de1;

        // ---- PhoneConnection -----------------------------------------------
        public const uint CtorPhoneConnection = 0x09cc123c7;
        public const uint CtorPhoneConnectionWebrtc = 0x635fe375;

        // ---- PhoneCallDiscardReason ----------------------------------------
        public const uint CtorDiscardReasonHangup = 0x57adc690;
        public const uint CtorDiscardReasonDisconnect = 0xe095c1a0;
        public const uint CtorDiscardReasonMissed = 0x85e42301;
        public const uint CtorDiscardReasonBusy = 0xfaf7e8c9;

        // ---- supporting ----------------------------------------------------
        public const uint CtorPhoneCallProtocol = 0xfc878fc8;
        public const uint CtorVector = 0x1cb5c415;
        public const uint CtorDataJson = 0x7d748d04;

        // ---- distilled view ------------------------------------------------

        public sealed class DecodedPhoneCall
        {
            public enum ShapeKind { Empty, Waiting, Requested, Accepted, Established, Discarded }
            public ShapeKind Shape { get; set; }
            public long CallId { get; set; }
            public long AccessHash { get; set; }
            public int Date { get; set; }
            public long AdminId { get; set; }
            public long ParticipantId { get; set; }
            public bool Video { get; set; }
            /// <summary>Waiting only — true when <c>receive_date</c> is set (peer device alerting).</summary>
            public bool HasReceiveDate { get; set; }
            /// <summary>Requested: <c>g_a_hash</c>. Accepted: <c>g_b</c>. Established: <c>g_a_or_b</c>. Null otherwise.</summary>
            public byte[] GAOrB { get; set; }
            /// <summary>Established only — peer-asserted key fingerprint.</summary>
            public long KeyFingerprint { get; set; }
            /// <summary>Established only — connection list. Always non-null on <see cref="ShapeKind.Established"/>.</summary>
            public IList<CallEndpoint> Endpoints { get; set; }
            /// <summary>Call protocol carried by waiting/requested/accepted/established shapes.</summary>
            public CallProtocol Protocol { get; set; }
            public bool HasProtocol { get; set; }
            /// <summary>Discarded only — wire ctor id of the discard reason; map via the application layer.</summary>
            public uint DiscardReasonCtor { get; set; }
            /// <summary>Discarded only — duration (in seconds) the server accumulated.</summary>
            public int DurationSeconds { get; set; }
        }

        // ---- top-level decoder ---------------------------------------------

        public static DecodedPhoneCall DecodePhoneCall(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor == CtorPhonePhoneCall)
                {
                    // phone.phoneCall wraps the inner PhoneCall + Vector<User>;
                    // we only need the inner phone_call here.
                    return ReadPhoneCall(r);
                }
                // Caller may also pass the bare PhoneCall constructor (e.g.
                // unwrapped from updatePhoneCall). Rewind 4 bytes and decode.
                ms.Position = 0;
                using (var r2 = new BinaryReader(ms))
                {
                    return ReadPhoneCall(r2);
                }
            }
        }

        public static string DecodeDataJson(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty DataJSON payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorDataJson)
                    throw new InvalidDataException("expected dataJSON; got 0x" + ctor.ToString("x8"));
                return ReadString(r);
            }
        }

        private static DecodedPhoneCall ReadPhoneCall(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            DecodedPhoneCall c = new DecodedPhoneCall();

            if (ctor == CtorPhoneCallEmpty)
            {
                c.Shape = DecodedPhoneCall.ShapeKind.Empty;
                c.CallId = r.ReadInt64();
                return c;
            }

            if (ctor == CtorPhoneCallWaiting)
            {
                c.Shape = DecodedPhoneCall.ShapeKind.Waiting;
                int flags = r.ReadInt32();
                c.Video = (flags & (1 << 6)) != 0;
                c.CallId = r.ReadInt64();
                c.AccessHash = r.ReadInt64();
                c.Date = r.ReadInt32();
                c.AdminId = r.ReadInt64();
                c.ParticipantId = r.ReadInt64();
                c.Protocol = ReadPhoneCallProtocol(r);
                c.HasProtocol = true;
                if ((flags & (1 << 0)) != 0)
                {
                    r.ReadInt32(); // receive_date
                    c.HasReceiveDate = true;
                }
                return c;
            }

            if (ctor == CtorPhoneCallRequested)
            {
                c.Shape = DecodedPhoneCall.ShapeKind.Requested;
                int flags = r.ReadInt32();
                c.Video = (flags & (1 << 6)) != 0;
                c.CallId = r.ReadInt64();
                c.AccessHash = r.ReadInt64();
                c.Date = r.ReadInt32();
                c.AdminId = r.ReadInt64();
                c.ParticipantId = r.ReadInt64();
                c.GAOrB = ReadBytes(r);
                c.Protocol = ReadPhoneCallProtocol(r);
                c.HasProtocol = true;
                return c;
            }

            if (ctor == CtorPhoneCallAccepted)
            {
                c.Shape = DecodedPhoneCall.ShapeKind.Accepted;
                int flags = r.ReadInt32();
                c.Video = (flags & (1 << 6)) != 0;
                c.CallId = r.ReadInt64();
                c.AccessHash = r.ReadInt64();
                c.Date = r.ReadInt32();
                c.AdminId = r.ReadInt64();
                c.ParticipantId = r.ReadInt64();
                c.GAOrB = ReadBytes(r);
                c.Protocol = ReadPhoneCallProtocol(r);
                c.HasProtocol = true;
                return c;
            }

            if (ctor == CtorPhoneCall || ctor == CtorPhoneCallLegacy)
            {
                c.Shape = DecodedPhoneCall.ShapeKind.Established;
                int flags = r.ReadInt32();
                c.Video = (flags & (1 << 6)) != 0;
                c.CallId = r.ReadInt64();
                c.AccessHash = r.ReadInt64();
                c.Date = r.ReadInt32();
                c.AdminId = r.ReadInt64();
                c.ParticipantId = r.ReadInt64();
                c.GAOrB = ReadBytes(r);
                c.KeyFingerprint = r.ReadInt64();
                c.Protocol = ReadPhoneCallProtocol(r);
                c.HasProtocol = true;
                c.Endpoints = ReadConnectionVector(r);
                r.ReadInt32(); // start_date
                if (ctor == CtorPhoneCall && (flags & (1 << 7)) != 0)
                {
                    ReadString(r); // custom_parameters:DataJSON
                }
                return c;
            }

            if (ctor == CtorPhoneCallDiscarded)
            {
                c.Shape = DecodedPhoneCall.ShapeKind.Discarded;
                int flags = r.ReadInt32();
                c.Video = (flags & (1 << 6)) != 0;
                c.CallId = r.ReadInt64();
                if ((flags & (1 << 0)) != 0)
                {
                    c.DiscardReasonCtor = r.ReadUInt32();
                }
                if ((flags & (1 << 1)) != 0)
                {
                    c.DurationSeconds = r.ReadInt32();
                }
                return c;
            }

            throw new InvalidDataException("Unexpected PhoneCall constructor: 0x" + ctor.ToString("x8"));
        }

        // ---- shared readers -------------------------------------------------

        private static IList<CallEndpoint> ReadConnectionVector(BinaryReader r)
        {
            uint header = r.ReadUInt32();
            if (header != CtorVector) throw new InvalidDataException("expected Vector header for connections; got 0x" + header.ToString("x8"));
            int count = r.ReadInt32();
            List<CallEndpoint> result = new List<CallEndpoint>(count);
            for (int i = 0; i < count; i++)
            {
                uint ctor = r.ReadUInt32();
                if (ctor == CtorPhoneConnection)
                {
                    int flags = r.ReadInt32(); // tcp:flags.0?true is bit-only; nothing to consume.
                    bool tcp = (flags & (1 << 0)) != 0;
                    long id = r.ReadInt64();
                    string ip = ReadString(r);
                    string ipv6 = ReadString(r);
                    int port = r.ReadInt32();
                    byte[] peerTag = ReadBytes(r);
                    result.Add(new CallEndpoint(
                        CallEndpointKind.Reflector,
                        id,
                        ip,
                        ipv6,
                        port,
                        peerTag,
                        tcp,
                        false,
                        false,
                        string.Empty,
                        string.Empty,
                        id));
                }
                else if (ctor == CtorPhoneConnectionWebrtc)
                {
                    int flags = r.ReadInt32();
                    bool turn = (flags & (1 << 0)) != 0;
                    bool stun = (flags & (1 << 1)) != 0;
                    long id = r.ReadInt64();
                    string ip = ReadString(r);
                    string ipv6 = ReadString(r);
                    int port = r.ReadInt32();
                    string username = ReadString(r);
                    string password = ReadString(r);
                    result.Add(new CallEndpoint(
                        CallEndpointKind.WebRtc,
                        id,
                        ip,
                        ipv6,
                        port,
                        new byte[0],
                        false,
                        stun,
                        turn,
                        username,
                        password,
                        id));
                }
                else
                {
                    throw new InvalidDataException("Unexpected PhoneConnection constructor: 0x" + ctor.ToString("x8"));
                }
            }
            return result;
        }

        private static CallProtocol ReadPhoneCallProtocol(BinaryReader r)
        {
            uint header = r.ReadUInt32();
            if (header != CtorPhoneCallProtocol)
                throw new InvalidDataException("expected phoneCallProtocol; got 0x" + header.ToString("x8"));
            int flags = r.ReadInt32();
            bool udpP2p = (flags & (1 << 0)) != 0;
            bool udpReflector = (flags & (1 << 1)) != 0;
            int minLayer = r.ReadInt32();
            int maxLayer = r.ReadInt32();
            uint vec = r.ReadUInt32();
            if (vec != CtorVector) throw new InvalidDataException("expected Vector for library_versions");
            int count = r.ReadInt32();
            string[] versions = new string[count < 0 ? 0 : count];
            for (int i = 0; i < count; i++)
            {
                versions[i] = ReadString(r);
            }
            return new CallProtocol(udpP2p, udpReflector, minLayer, maxLayer, versions);
        }

        // ---- primitives -----------------------------------------------------

        private static byte[] ReadBytes(BinaryReader r)
        {
            byte first = r.ReadByte();
            int len;
            int prefixLen;
            if (first == 254)
            {
                byte b1 = r.ReadByte();
                byte b2 = r.ReadByte();
                byte b3 = r.ReadByte();
                len = b1 | (b2 << 8) | (b3 << 16);
                prefixLen = 4;
            }
            else
            {
                len = first;
                prefixLen = 1;
            }
            byte[] bytes = r.ReadBytes(len);
            int padding = (4 - ((prefixLen + len) % 4)) % 4;
            for (int i = 0; i < padding; i++) r.ReadByte();
            return bytes;
        }

        private static string ReadString(BinaryReader r)
        {
            byte[] bytes = ReadBytes(r);
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }
    }
}
