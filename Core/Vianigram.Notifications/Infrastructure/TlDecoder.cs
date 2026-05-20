// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Notifications.Domain.ValueObjects;

namespace Vianigram.Notifications.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the notification responses
    /// this context consumes. Mirrors the per-context approach used in
    /// <c>Vianigram.Stickers</c> and <c>Vianigram.Contacts</c>.
    ///
    /// Supported response constructors:
    ///   * peerNotifySettings#a83b0426
    ///       flags:#  show_previews:flags.0?Bool  silent:flags.1?Bool
    ///       mute_until:flags.2?int               ios_sound:flags.3?NotificationSound
    ///       android_sound:flags.4?NotificationSound
    ///       other_sound:flags.5?NotificationSound
    ///   * boolTrue#997275b5 / boolFalse#bc799737
    ///   * notificationSoundDefault#97e8bebe
    ///   * notificationSoundNone#6f0c34df
    ///   * notificationSoundLocal#830b9ae4 title:string data:string
    ///   * notificationSoundRingtone#ff6c8049 id:long
    ///
    /// V1 limitation: we only surface <see cref="MuteRule"/> fields the
    /// domain layer cares about (mute_until + show_previews + a single sound
    /// label). The various per-platform sound channels are collapsed onto
    /// a single <c>Sound</c> string in the value object; a future revision
    /// may surface them distinctly.
    /// </summary>
    internal static class TlDecoder
    {
        public const uint CtorPeerNotifySettings = 0xa83b0426;

        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        public const uint CtorNotificationSoundDefault = 0x97e8bebe;
        public const uint CtorNotificationSoundNone = 0x6f0c34df;
        public const uint CtorNotificationSoundLocal = 0x830b9ae4;
        public const uint CtorNotificationSoundRingtone = 0xff6c8049;

        public sealed class DecodedNotifySettings
        {
            public bool ShowPreviews { get; set; }
            public bool HasMuteUntil { get; set; }
            public int MuteUntilSeconds { get; set; }
            public string Sound { get; set; }
        }

        /// <summary>
        /// Decode a top-level peerNotifySettings response. Returns a populated
        /// container; on shape drift the unread fields fall back to defaults
        /// (show_previews=true, no mute, empty sound).
        /// </summary>
        public static DecodedNotifySettings DecodePeerNotifySettings(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorPeerNotifySettings)
                    throw new InvalidDataException("Unexpected peerNotifySettings constructor: 0x" + ctor.ToString("x8"));

                var result = new DecodedNotifySettings
                {
                    ShowPreviews = true,
                    HasMuteUntil = false,
                    MuteUntilSeconds = 0,
                    Sound = string.Empty
                };

                int flags;
                try { flags = r.ReadInt32(); } catch { return result; }

                if ((flags & (1 << 0)) != 0)
                {
                    result.ShowPreviews = TryReadBool(r, true);
                }
                if ((flags & (1 << 1)) != 0)
                {
                    // silent — read but discard for V1.
                    TryReadBool(r, false);
                }
                if ((flags & (1 << 2)) != 0)
                {
                    int sec;
                    try { sec = r.ReadInt32(); } catch { return result; }
                    result.HasMuteUntil = true;
                    result.MuteUntilSeconds = sec;
                }
                // ios_sound, android_sound, other_sound — first non-empty wins.
                if ((flags & (1 << 3)) != 0)
                {
                    string s = TryReadSound(r);
                    if (!string.IsNullOrEmpty(s)) result.Sound = s;
                }
                if ((flags & (1 << 4)) != 0)
                {
                    string s = TryReadSound(r);
                    if (string.IsNullOrEmpty(result.Sound) && !string.IsNullOrEmpty(s)) result.Sound = s;
                }
                if ((flags & (1 << 5)) != 0)
                {
                    string s = TryReadSound(r);
                    if (string.IsNullOrEmpty(result.Sound) && !string.IsNullOrEmpty(s)) result.Sound = s;
                }

                return result;
            }
        }

        /// <summary>
        /// Convenience: decode + convert into a <see cref="MuteRule"/>
        /// addressed to the supplied <paramref name="peerKey"/>.
        /// </summary>
        public static MuteRule DecodeMuteRule(byte[] payload, string peerKey)
        {
            DecodedNotifySettings d = DecodePeerNotifySettings(payload);
            DateTime? muteUntil = null;
            if (d.HasMuteUntil)
            {
                if (d.MuteUntilSeconds <= 0)
                {
                    muteUntil = null;
                }
                else if (d.MuteUntilSeconds == int.MaxValue)
                {
                    muteUntil = DateTime.MaxValue;
                }
                else
                {
                    DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    muteUntil = epoch.AddSeconds(d.MuteUntilSeconds);
                }
            }
            return new MuteRule(peerKey ?? MuteRule.Global, muteUntil, d.Sound ?? string.Empty, d.ShowPreviews);
        }

        // ---- helpers --------------------------------------------------------

        private static bool TryReadBool(BinaryReader r, bool fallback)
        {
            uint ctor;
            try { ctor = r.ReadUInt32(); } catch { return fallback; }
            if (ctor == CtorBoolTrue) return true;
            if (ctor == CtorBoolFalse) return false;
            return fallback;
        }

        /// <summary>
        /// Reads a single NotificationSound TL slot. Returns a label
        /// ("default", "none", "<title>") or empty string when the cursor is
        /// misaligned. We do not advance past unrecognized variants — the
        /// caller stops reading further optional fields.
        /// </summary>
        private static string TryReadSound(BinaryReader r)
        {
            uint ctor;
            try { ctor = r.ReadUInt32(); } catch { return string.Empty; }
            if (ctor == CtorNotificationSoundDefault) return "default";
            if (ctor == CtorNotificationSoundNone) return "none";
            if (ctor == CtorNotificationSoundLocal)
            {
                try
                {
                    string title = ReadString(r);
                    ReadString(r); // data, discarded
                    return title ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }
            if (ctor == CtorNotificationSoundRingtone)
            {
                try
                {
                    long id = r.ReadInt64();
                    return "ringtone:" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    return string.Empty;
                }
            }
            return string.Empty;
        }

        private static string ReadString(BinaryReader r)
        {
            byte[] bytes = ReadBytes(r);
            if (bytes == null || bytes.Length == 0) return string.Empty;
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

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
    }
}
