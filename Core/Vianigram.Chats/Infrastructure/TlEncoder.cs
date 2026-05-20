// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the four RPC shapes Chats actually issues.
    /// We deliberately avoid pulling in the full TL schema generator from
    /// <c>Vianigram.Core.Tl</c> here — Chats only needs:
    ///
    ///   * messages.getDialogs#a0f4cb4f
    ///   * messages.toggleDialogPin#a731e257
    ///   * account.updateNotifySettings#84be5b93
    ///
    /// Anything Chats does not need is intentionally NOT covered. When schema deltas
    /// arrive (layer bump, new optional fields), we extend here rather than reach
    /// into the global TL codegen.
    ///
    /// All multi-byte integers are little-endian (TL convention).
    /// </summary>
    internal static class TlEncoder
    {
        // ---- TL constructor ids ---------------------------------------------------
        public const uint CtorGetDialogs = 0xa0f4cb4f;            // messages.getDialogs
        public const uint CtorToggleDialogPin = 0xa731e257;       // messages.toggleDialogPin
        public const uint CtorUpdateNotifySettings = 0x84be5b93;  // account.updateNotifySettings

        // Helper TL constructors used for inline composition.
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;
        public const uint CtorInputPeerEmpty = 0x7f3b18ea;
        public const uint CtorInputPeerSelf = 0x7da07ec9;
        public const uint CtorInputPeerUser = 0xdde8a54c;
        public const uint CtorInputPeerChat = 0x35a95cb9;
        public const uint CtorInputPeerChannel = 0x27bcbbfc;
        public const uint CtorInputDialogPeer = 0xfcaafeb7;
        public const uint CtorInputNotifyPeer = 0xb8bc5b0c;
        public const uint CtorInputPeerNotifySettings = 0xdf1f002b;

        // -------------------------------------------------------------------------
        // messages.getDialogs#a0f4cb4f
        //   flags:#  ?exclude_pinned:flags.0?true  ?folder_id:flags.1?int
        //   offset_date:int  offset_id:int  offset_peer:InputPeer  limit:int  hash:long
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetDialogs(int limit, DialogCursor cursor, int? folderId, long hash)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetDialogs);

                int flags = 0;
                if (folderId.HasValue) flags |= 1 << 1;
                w.Write(flags);

                if (folderId.HasValue) w.Write(folderId.Value);

                int offsetDate = ToUnixSeconds(cursor != null ? cursor.OffsetDate : default(DateTime));
                int offsetId = cursor != null ? (int)cursor.OffsetId : 0;
                w.Write(offsetDate);
                w.Write(offsetId);

                WriteInputPeer(w, cursor != null ? cursor.OffsetPeer : null);

                w.Write(limit);
                w.Write(hash);

                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.toggleDialogPin#a731e257  flags:# pinned:flags.0?true peer:InputDialogPeer
        // -------------------------------------------------------------------------
        public static byte[] EncodeToggleDialogPin(PeerId peer, bool pinned)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorToggleDialogPin);

                int flags = 0;
                if (pinned) flags |= 1 << 0; // pinned is a true-flag
                w.Write(flags);

                // inputDialogPeer#fcaafeb7 peer:InputPeer
                w.Write(CtorInputDialogPeer);
                WriteInputPeer(w, peer);

                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // account.updateNotifySettings#84be5b93 peer:InputNotifyPeer settings:InputPeerNotifySettings
        //
        // We model "muted" as: mute_until = now + window (or INT32_MAX for "forever").
        // Other settings (sound, show_previews) are deferred — server keeps prior values
        // when the corresponding bits in flags are not set.
        //
        // inputPeerNotifySettings#df1f002b  flags:#  show_previews:flags.0?Bool
        //   silent:flags.1?Bool  mute_until:flags.2?int  sound:flags.3?NotificationSound
        // -------------------------------------------------------------------------
        public static byte[] EncodeUpdateNotifySettings(PeerId peer, TimeSpan? muteFor, DateTime nowUtc)
        {
            if (peer == null) throw new ArgumentNullException("peer");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorUpdateNotifySettings);

                // inputNotifyPeer#b8bc5b0c peer:InputPeer
                w.Write(CtorInputNotifyPeer);
                WriteInputPeer(w, peer);

                // inputPeerNotifySettings
                w.Write(CtorInputPeerNotifySettings);

                int flags = 0;
                // mute_until present — flags.2
                flags |= 1 << 2;
                w.Write(flags);

                int muteUntil;
                if (!muteFor.HasValue)
                {
                    // "Muted forever" — Telegram convention: int.MaxValue.
                    muteUntil = int.MaxValue;
                }
                else if (muteFor.Value <= TimeSpan.Zero)
                {
                    // Unmute = mute_until in the past (0 unix time).
                    muteUntil = 0;
                }
                else
                {
                    long until = ToUnixSeconds(nowUtc.Add(muteFor.Value));
                    muteUntil = until > int.MaxValue ? int.MaxValue : (int)until;
                }
                w.Write(muteUntil);

                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // InputPeer wire-format dispatcher.
        // -------------------------------------------------------------------------
        private static void WriteInputPeer(BinaryWriter w, PeerId peer)
        {
            if (peer == null)
            {
                w.Write(CtorInputPeerEmpty);
                return;
            }

            switch (peer.Kind)
            {
                case PeerKind.User:
                    w.Write(CtorInputPeerUser);
                    w.Write(peer.Id);             // long user_id
                    w.Write(peer.AccessHash);     // long access_hash
                    break;
                case PeerKind.Chat:
                    w.Write(CtorInputPeerChat);
                    w.Write(peer.Id);             // long chat_id
                    break;
                case PeerKind.Channel:
                    w.Write(CtorInputPeerChannel);
                    w.Write(peer.Id);             // long channel_id
                    w.Write(peer.AccessHash);     // long access_hash
                    break;
                default:
                    w.Write(CtorInputPeerEmpty);
                    break;
            }
        }

        private static int ToUnixSeconds(DateTime utc)
        {
            if (utc == default(DateTime)) return 0;
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            DateTime asUtc = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            long secs = (long)(asUtc - epoch).TotalSeconds;
            if (secs < 0) return 0;
            return secs > int.MaxValue ? int.MaxValue : (int)secs;
        }

        // Optional helper used by tests / future callers: TL string encoding.
        // Kept private until needed externally.
        // ReSharper disable once UnusedMember.Local
        private static void WriteString(BinaryWriter w, string s)
        {
            byte[] bytes = string.IsNullOrEmpty(s) ? new byte[0] : Encoding.UTF8.GetBytes(s);
            int len = bytes.Length;
            int padding;
            if (len < 254)
            {
                w.Write((byte)len);
                w.Write(bytes);
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
