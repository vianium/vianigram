// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using Vianigram.Calls.Domain.ValueObjects;

namespace Vianigram.Calls.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the phone.* RPC shapes the
    /// Calls context issues. Mirrors the per-context approach used by
    /// <c>Vianigram.SecretChats</c>, <c>Vianigram.Contacts</c>, and
    /// <c>Vianigram.Chats</c>. The layer 214 generated header does include the
    /// phone.* constructors, but the secret-chat / per-context tradition for
    /// hand-written wire payloads is preserved here for symmetry; a future
    /// revision may switch to the codegen path.
    ///
    /// Outer constructors (sent on the wire):
    ///   * phone.requestCall#42ff96ed  flags:# video:flags.0?true user_id:InputUser random_id:int g_a_hash:bytes protocol:PhoneCallProtocol = phone.PhoneCall;
    ///   * phone.acceptCall#3bd2b4a0   peer:InputPhoneCall g_b:bytes protocol:PhoneCallProtocol = phone.PhoneCall;
    ///   * phone.confirmCall#2efe1722  peer:InputPhoneCall g_a:bytes key_fingerprint:long protocol:PhoneCallProtocol = phone.PhoneCall;
    ///   * phone.receivedCall#17d54f61 peer:InputPhoneCall = Bool;
    ///   * phone.discardCall#b2cbc1c0  flags:# video:flags.0?true peer:InputPhoneCall duration:int reason:PhoneCallDiscardReason connection_id:long = Updates;
    ///
    /// All multi-byte integers are little-endian (TL convention). Strings
    /// use the standard TL byte-string framing (1- or 4-byte length
    /// prefix + padding to a 4-byte alignment).
    /// </summary>
    internal static class TlEncoder
    {
        // ---- outer (server-bound) RPC constructor ids -----------------------
        public const uint CtorRequestCall = 0x42ff96ed;
        public const uint CtorAcceptCall = 0x3bd2b4a0;
        public const uint CtorConfirmCall = 0x2efe1722;
        public const uint CtorReceivedCall = 0x17d54f61;
        public const uint CtorDiscardCall = 0xb2cbc1c0;
        public const uint CtorSendSignalingData = 0xff7a9383;
        public const uint CtorGetCallConfig = 0x55451fa9;

        // ---- supporting TL constructors ------------------------------------
        public const uint CtorInputUserEmpty = 0xb98886cf;
        public const uint CtorInputUser = 0xf21158c6;
        public const uint CtorInputPhoneCall = 0x1e36fded;
        public const uint CtorPhoneCallProtocol = 0xfc878fc8;

        // ---- discard reasons -----------------------------------------------
        public const uint CtorDiscardReasonHangup = 0x57adc690;
        public const uint CtorDiscardReasonDisconnect = 0xe095c1a0;
        public const uint CtorDiscardReasonMissed = 0x85e42301;
        public const uint CtorDiscardReasonBusy = 0xfaf7e8c9;

        // ---- TL booleans (referenced by alt protocol forms) ----------------
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        public const uint CtorVector = 0x1cb5c415;

        // -------------------------------------------------------------------------
        // phone.requestCall#42ff96ed
        //   flags:# video:flags.0?true user_id:InputUser random_id:int g_a_hash:bytes protocol:PhoneCallProtocol = phone.PhoneCall;
        // -------------------------------------------------------------------------
        public static byte[] EncodeRequestCall(long userId, long accessHash, int randomId, byte[] gAHash, CallProtocol protocol, bool video)
        {
            if (gAHash == null) throw new ArgumentNullException("gAHash");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorRequestCall);
                int flags = 0;
                if (video) flags |= 1 << 0;
                w.Write(flags);
                if (userId <= 0)
                {
                    w.Write(CtorInputUserEmpty);
                }
                else
                {
                    w.Write(CtorInputUser);
                    w.Write(userId);
                    w.Write(accessHash);
                }
                w.Write(randomId);
                WriteBytes(w, gAHash);
                WritePhoneCallProtocol(w, protocol);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // phone.acceptCall#3bd2b4a0  peer:InputPhoneCall g_b:bytes protocol:PhoneCallProtocol = phone.PhoneCall;
        // -------------------------------------------------------------------------
        public static byte[] EncodeAcceptCall(long callId, long accessHash, byte[] gB, CallProtocol protocol)
        {
            if (gB == null) throw new ArgumentNullException("gB");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAcceptCall);
                WriteInputPhoneCall(w, callId, accessHash);
                WriteBytes(w, gB);
                WritePhoneCallProtocol(w, protocol);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // phone.confirmCall#2efe1722  peer:InputPhoneCall g_a:bytes key_fingerprint:long protocol:PhoneCallProtocol = phone.PhoneCall;
        // -------------------------------------------------------------------------
        public static byte[] EncodeConfirmCall(long callId, long accessHash, byte[] gA, long keyFingerprint, CallProtocol protocol)
        {
            if (gA == null) throw new ArgumentNullException("gA");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorConfirmCall);
                WriteInputPhoneCall(w, callId, accessHash);
                WriteBytes(w, gA);
                w.Write(keyFingerprint);
                WritePhoneCallProtocol(w, protocol);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // phone.receivedCall#17d54f61 peer:InputPhoneCall = Bool
        // -------------------------------------------------------------------------
        public static byte[] EncodeReceivedCall(long callId, long accessHash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorReceivedCall);
                WriteInputPhoneCall(w, callId, accessHash);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // phone.discardCall#b2cbc1c0
        //   flags:# video:flags.0?true peer:InputPhoneCall duration:int reason:PhoneCallDiscardReason connection_id:long = Updates;
        // -------------------------------------------------------------------------
        public static byte[] EncodeDiscardCall(long callId, long accessHash, int durationSeconds, DiscardReason reason, long connectionId, bool video)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorDiscardCall);
                int flags = 0;
                if (video) flags |= 1 << 0;
                w.Write(flags);
                WriteInputPhoneCall(w, callId, accessHash);
                w.Write(durationSeconds);
                w.Write(MapReasonToCtor(reason));
                w.Write(connectionId);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // phone.sendSignalingData#ff7a9383 peer:InputPhoneCall data:bytes = Bool
        // -------------------------------------------------------------------------
        public static byte[] EncodeSendSignalingData(long callId, long accessHash, byte[] data)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorSendSignalingData);
                WriteInputPhoneCall(w, callId, accessHash);
                WriteBytes(w, data ?? new byte[0]);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // phone.getCallConfig#55451fa9 = DataJSON
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetCallConfig()
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetCallConfig);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // helpers
        // -------------------------------------------------------------------------
        private static void WriteInputPhoneCall(BinaryWriter w, long callId, long accessHash)
        {
            w.Write(CtorInputPhoneCall);
            w.Write(callId);
            w.Write(accessHash);
        }

        private static void WritePhoneCallProtocol(BinaryWriter w, CallProtocol protocol)
        {
            w.Write(CtorPhoneCallProtocol);
            int flags = 0;
            if (protocol.UdpP2p) flags |= 1 << 0;
            if (protocol.UdpReflector) flags |= 1 << 1;
            w.Write(flags);
            w.Write(protocol.MinLayer);
            w.Write(protocol.MaxLayer);
            // library_versions:Vector<string>
            w.Write(CtorVector);
            string[] versions = protocol.LibraryVersions ?? new string[0];
            w.Write(versions.Length);
            for (int i = 0; i < versions.Length; i++)
            {
                WriteString(w, versions[i] ?? string.Empty);
            }
        }

        private static uint MapReasonToCtor(DiscardReason reason)
        {
            switch (reason)
            {
                case DiscardReason.Hangup: return CtorDiscardReasonHangup;
                case DiscardReason.Disconnect: return CtorDiscardReasonDisconnect;
                case DiscardReason.Missed: return CtorDiscardReasonMissed;
                case DiscardReason.Busy: return CtorDiscardReasonBusy;
                // Client-only reasons translate to plain hangup on the wire.
                case DiscardReason.ProtocolError: return CtorDiscardReasonHangup;
                case DiscardReason.LocalShutdown: return CtorDiscardReasonHangup;
                default: return CtorDiscardReasonHangup;
            }
        }

        /// <summary>
        /// TL <c>bytes</c> framing — identical to <c>string</c>: 1-byte
        /// length (or 0xFE + 3-byte length when length &gt;= 254) followed
        /// by the raw bytes and padding to a 4-byte boundary.
        /// </summary>
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

        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? new byte[0] : System.Text.Encoding.UTF8.GetBytes(s);
            WriteBytes(w, bytes);
        }
    }
}
