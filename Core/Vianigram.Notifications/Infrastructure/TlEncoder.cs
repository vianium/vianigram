// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the Notifications RPC shapes
    /// this context issues. Mirrors the per-context approach used in
    /// <c>Vianigram.Stickers</c> and <c>Vianigram.Contacts</c> — Notifications
    /// only needs:
    ///
    ///   * account.updateNotifySettings#84be5b93
    ///       peer:InputNotifyPeer settings:InputPeerNotifySettings
    ///   * account.getNotifySettings#12b3ad31
    ///       peer:InputNotifyPeer
    ///
    /// Plus the helper TL constructors used inline:
    ///   - inputNotifyPeer#b8bc5b0c (peer:InputPeer)
    ///   - inputNotifyUsers#193b4417
    ///   - inputNotifyChats#4a95e84e
    ///   - inputNotifyBroadcasts#b1db7c7e
    ///   - inputPeerNotifySettings#df1f002b
    ///       flags:#  show_previews:flags.0?Bool  silent:flags.1?Bool
    ///       mute_until:flags.2?int               sound:flags.3?NotificationSound
    ///   - notificationSoundDefault#97e8bebe
    ///   - notificationSoundNone#6f0c34df
    ///   - notificationSoundLocal#830b9ae4 title:string data:string
    ///   - inputPeerSelf#7da07ec9
    ///   - inputPeerUser#dde8a54c user_id:long access_hash:long
    ///   - inputPeerChat#35a95cb9 chat_id:long
    ///   - inputPeerChannel#27bcbbfc channel_id:long access_hash:long
    ///   - boolTrue#997275b5 / boolFalse#bc799737
    ///
    /// All multi-byte integers are little-endian (TL convention). The peer
    /// addressing scheme uses the same "kind:numeric[:hash]" key format
    /// honored by the higher-level chats/contacts contexts so the encoder
    /// here can route each PeerKey to the right InputPeer constructor.
    /// </summary>
    internal static class TlEncoder
    {
        // ---- TL constructor ids ----------------------------------------------
        public const uint CtorUpdateNotifySettings = 0x84be5b93;
        public const uint CtorGetNotifySettings = 0x12b3ad31;

        public const uint CtorInputNotifyPeer = 0xb8bc5b0c;
        public const uint CtorInputNotifyUsers = 0x193b4417;
        public const uint CtorInputNotifyChats = 0x4a95e84e;
        public const uint CtorInputNotifyBroadcasts = 0xb1db7c7e;

        public const uint CtorInputPeerNotifySettings = 0xdf1f002b;

        public const uint CtorNotificationSoundDefault = 0x97e8bebe;
        public const uint CtorNotificationSoundNone = 0x6f0c34df;
        public const uint CtorNotificationSoundLocal = 0x830b9ae4;

        public const uint CtorInputPeerSelf = 0x7da07ec9;
        // inputPeerUser#dde8a54c — same fix as Vianigram.Messages.TlEncoder.
        // The legacy 0x7b8e7de6 is not a valid input peer ctor; the server
        // rejects the wrapping account.updateNotifySettings RPC with
        // INPUT_CONSTRUCTOR_INVALID_00 when this lands inside the
        // inputNotifyPeer wrapper.
        public const uint CtorInputPeerUser = 0xdde8a54c;
        public const uint CtorInputPeerChat = 0x35a95cb9;
        public const uint CtorInputPeerChannel = 0x27bcbbfc;

        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        // -------------------------------------------------------------------------
        // account.getNotifySettings#12b3ad31  peer:InputNotifyPeer
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetNotifySettings(string peerKey)
        {
            return EncodeGetNotifySettings(peerKey, 0L);
        }

        public static byte[] EncodeGetNotifySettings(string peerKey, long accessHash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetNotifySettings);
                WriteInputNotifyPeer(w, peerKey, accessHash);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // account.updateNotifySettings#84be5b93  peer:InputNotifyPeer
        //                                        settings:InputPeerNotifySettings
        // -------------------------------------------------------------------------
        public static byte[] EncodeUpdateNotifySettings(string peerKey, MuteRule rule)
        {
            return EncodeUpdateNotifySettings(peerKey, rule, 0L);
        }

        public static byte[] EncodeUpdateNotifySettings(string peerKey, MuteRule rule, long accessHash)
        {
            if (rule == null) throw new ArgumentNullException("rule");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorUpdateNotifySettings);
                WriteInputNotifyPeer(w, peerKey ?? MuteRule.Global, accessHash);
                WriteInputPeerNotifySettings(w, rule);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // helpers
        // -------------------------------------------------------------------------

        private static void WriteInputNotifyPeer(BinaryWriter w, string peerKey)
        {
            WriteInputNotifyPeer(w, peerKey, 0L);
        }

        private static void WriteInputNotifyPeer(BinaryWriter w, string peerKey, long accessHash)
        {
            // Global / scope keys honored by the wire:
            //   "*"          -> handled by caller (uses default scope; we pick users).
            //   "scope:users" / "scope:chats" / "scope:broadcasts" -> dedicated ctors.
            //   "user:N[:H]" / "chat:N" / "channel:N[:H]" / "self" -> InputNotifyPeer.
            string key = peerKey ?? string.Empty;
            if (string.Equals(key, MuteRule.Global, StringComparison.Ordinal) ||
                string.Equals(key, "scope:users", StringComparison.Ordinal))
            {
                w.Write(CtorInputNotifyUsers);
                return;
            }
            if (string.Equals(key, "scope:chats", StringComparison.Ordinal))
            {
                w.Write(CtorInputNotifyChats);
                return;
            }
            if (string.Equals(key, "scope:broadcasts", StringComparison.Ordinal))
            {
                w.Write(CtorInputNotifyBroadcasts);
                return;
            }

            w.Write(CtorInputNotifyPeer);
            WriteInputPeer(w, key, accessHash);
        }

        private static void WriteInputPeer(BinaryWriter w, string peerKey)
        {
            WriteInputPeer(w, peerKey, 0L);
        }

        private static void WriteInputPeer(BinaryWriter w, string peerKey, long accessHash)
        {
            // peerKey shapes:
            //   "self"
            //   "user:<id>[:<accessHash>]"
            //   "chat:<id>"
            //   "channel:<id>[:<accessHash>]"
            // Unknown / empty -> inputPeerSelf (safe default for V1).
            if (string.IsNullOrEmpty(peerKey) || string.Equals(peerKey, "self", StringComparison.Ordinal))
            {
                w.Write(CtorInputPeerSelf);
                return;
            }

            string[] parts = peerKey.Split(':');
            if (parts.Length < 2)
            {
                w.Write(CtorInputPeerSelf);
                return;
            }

            long id;
            long.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out id);
            long resolvedAccessHash = accessHash;
            if (parts.Length >= 3)
            {
                long accessHashFromKey;
                if (long.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out accessHashFromKey) &&
                    resolvedAccessHash == 0L)
                {
                    resolvedAccessHash = accessHashFromKey;
                }
            }

            switch (parts[0])
            {
                case "user":
                    w.Write(CtorInputPeerUser);
                    w.Write(id);
                    w.Write(resolvedAccessHash);
                    break;
                case "chat":
                    w.Write(CtorInputPeerChat);
                    w.Write(id);
                    break;
                case "channel":
                    w.Write(CtorInputPeerChannel);
                    w.Write(id);
                    w.Write(resolvedAccessHash);
                    break;
                default:
                    w.Write(CtorInputPeerSelf);
                    break;
            }
        }

        private static void WriteInputPeerNotifySettings(BinaryWriter w, MuteRule rule)
        {
            w.Write(CtorInputPeerNotifySettings);

            // Build flags:
            //   bit0: show_previews
            //   bit1: silent (always omitted for V1)
            //   bit2: mute_until
            //   bit3: sound
            int flags = 0;
            flags |= 1 << 0; // we always send show_previews
            int muteUntilSec = 0;
            if (rule.MuteUntil.HasValue)
            {
                flags |= 1 << 2;
                if (rule.MuteUntil.Value == DateTime.MaxValue)
                {
                    // "Forever" — Telegram convention: 2147483647 (max int).
                    muteUntilSec = int.MaxValue;
                }
                else
                {
                    DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    long secs = (long)(rule.MuteUntil.Value.ToUniversalTime() - epoch).TotalSeconds;
                    if (secs < 0) secs = 0;
                    if (secs > int.MaxValue) secs = int.MaxValue;
                    muteUntilSec = (int)secs;
                }
            }
            bool hasSound = !string.IsNullOrEmpty(rule.Sound);
            if (hasSound)
            {
                flags |= 1 << 3;
            }

            w.Write(flags);
            // show_previews:flags.0?Bool
            w.Write(rule.ShowPreviews ? CtorBoolTrue : CtorBoolFalse);
            // mute_until:flags.2?int
            if ((flags & (1 << 2)) != 0)
            {
                w.Write(muteUntilSec);
            }
            // sound:flags.3?NotificationSound
            if (hasSound)
            {
                if (string.Equals(rule.Sound, "default", StringComparison.OrdinalIgnoreCase))
                {
                    w.Write(CtorNotificationSoundDefault);
                }
                else if (string.Equals(rule.Sound, "none", StringComparison.OrdinalIgnoreCase))
                {
                    w.Write(CtorNotificationSoundNone);
                }
                else
                {
                    // notificationSoundLocal#830b9ae4 title:string data:string
                    w.Write(CtorNotificationSoundLocal);
                    WriteString(w, rule.Sound);
                    WriteString(w, rule.Sound);
                }
            }
        }

        /// <summary>
        /// TL string encoding: 1 length byte + bytes + padding to 4-byte align
        /// (or, for length >= 254, 0xFE + 3 length bytes + bytes + padding).
        /// </summary>
        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? new byte[0] : Encoding.UTF8.GetBytes(s);
            WriteBytes(w, bytes);
        }

        private static void WriteBytes(BinaryWriter w, byte[] bytes)
        {
            int len = bytes == null ? 0 : bytes.Length;
            int padding;
            if (len < 254)
            {
                w.Write((byte)len);
                if (len > 0) w.Write(bytes);
                padding = (4 - ((len + 1) % 4)) % 4;
            }
            else
            {
                w.Write((byte)254);
                w.Write((byte)(len & 0xFF));
                w.Write((byte)((len >> 8) & 0xFF));
                w.Write((byte)((len >> 16) & 0xFF));
                w.Write(bytes);
                padding = (4 - (len % 4)) % 4;
            }
            for (int i = 0; i < padding; i++) w.Write((byte)0);
        }
    }
}
