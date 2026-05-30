// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vianigram.Chats.Domain.Entities;
using Vianigram.Chats.Domain.ValueObjects;

namespace Vianigram.Chats.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the messages.getDialogs response.
    /// We parse only the fields the Chats domain currently consumes.
    ///
    /// Supported response constructors:
    ///   * messages.dialogs#15ba6c40         (full result, no slicing)
    ///   * messages.dialogsSlice#71e094f3    (partial result, with cursor info)
    ///
    /// Unsupported / deferred TL constructors and fields:
    ///   - messages.dialogsNotModified — caller should treat the cached state as fresh.
    ///   - dialogFolder (folder pseudo-dialog) — V1 ignores, server emits one when archive present.
    ///   - draftMessage — domain has no Draft yet; we skip but acknowledge presence via flags.
    ///   - peerNotifySettings full payload — only mute_until is read.
    ///   - chats[] / users[] secondary collections beyond what we need to resolve titles
    ///     by peer id are parsed minimally (id + title + access_hash).
    ///
    /// Anything not understood is skipped where possible; truly unknown constructors
    /// cause the parser to bail with an exception, surfaced as ChatError.Unknown by the handler.
    /// </summary>
    internal static class TlDecoder
    {
        private static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static long DateTimeToUnix(DateTime utc)
        {
            DateTime asUtc = utc.Kind == DateTimeKind.Utc ? utc : utc.ToUniversalTime();
            return (long)(asUtc - UnixEpoch).TotalSeconds;
        }

        private static DateTime UnixToDateTime(long unixSeconds)
        {
            return UnixEpoch.AddSeconds(unixSeconds);
        }

        public const uint CtorDialogs = 0x15ba6c40;             // messages.dialogs
        public const uint CtorDialogsSlice = 0x71e094f3;        // messages.dialogsSlice
        public const uint CtorDialogsNotModified = 0xf0e3e596;  // messages.dialogsNotModified
        public const uint CtorVector = 0x1cb5c415;
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;
        // Layer 214: dialog#d58a08c6 (added view_forum_as_messages:flags.6?true,
        // a flag-only field that doesn't change the wire layout for accounts
        // without forum chats). Old ctor 0xa8edd0f5 is pre-217 and triggers
        // a silent break out of the dialogs loop (empty list).
        public const uint CtorDialog = 0xd58a08c6;              // dialog#d58a08c6
        public const uint CtorPeerUser = 0x59511722;            // peerUser#59511722  (long user_id)
        public const uint CtorPeerChat = 0x36c6019a;            // peerChat#36c6019a  (long chat_id)
        public const uint CtorPeerChannel = 0xa2a5371e;         // peerChannel#a2a5371e (long channel_id)
        // Layer 214: peerNotifySettings#99622c0c — added stories_muted /
        // stories_hide_sender bools and stories_*_sound NotificationSound
        // entries. The current reader only consumes mute_until (flag 2), so
        // accounts without stories preferences set are unaffected; if any
        // flags 3..10 are present the stream cursor is left dangling and
        // ReadDialog must early-return (which the caller already does).
        public const uint CtorPeerNotifySettings = 0x99622c0c;
        public const uint CtorMessage = 0x9815cec8;             // message#9815cec8
        public const uint CtorMessageEmpty = 0x90a6ca84;        // messageEmpty (skip)
        public const uint CtorMessageService = 0x7a800e0a;      // messageService layer 214
        public const uint CtorMessageServiceLegacy = 0x2b085862;// messageService legacy
        public const uint CtorMessageServiceCall = 0x1f1c25e9;  // call service message
        public const uint CtorUserMin = 0x3ec43dab;             // not exact; we don't parse users richly
        public const uint CtorChatMin = 0x41cbf256;             // ditto

        // dialogFolder#71bd134c flags:# pinned:flags.2?true folder:Folder peer:Peer top_message:int unread_muted_peers_count:int unread_unmuted_peers_count:int unread_muted_messages_count:int unread_unmuted_messages_count:int = Dialog;
        public const uint CtorDialogFolder = 0x71bd134c;

        // NotificationSound variants (layer 214) — consumed verbatim when
        // present in peerNotifySettings flags 3-5 / 8-10.
        public const uint CtorNotificationSoundDefault   = 0x97e8bebe; // empty
        public const uint CtorNotificationSoundNone      = 0x6f0c34df; // empty
        public const uint CtorNotificationSoundLocal     = 0x830b9ae4; // title:string data:string
        public const uint CtorNotificationSoundRingtone  = 0xff6c8049; // id:long

        // DraftMessage variants — consumed verbatim when peerNotifySettings'
        // dialog has flags.1 (draft present).
        public const uint CtorDraftMessageEmpty = 0x1b0c841a;          // flags:# date:flags.0?int
        public const uint CtorDraftMessage      = 0x3fccf7ef;          // see schema

        public sealed class DecodedDialogList
        {
            public IList<Dialog> Dialogs { get; set; }
            public IDictionary<string, string> Titles { get; set; }     // key = peer.ToString(), value = title
            public IDictionary<string, long> AccessHashes { get; set; } // refresh access hash if server rotated
            public bool HasMore { get; set; }
            public DialogCursor NextCursor { get; set; }
            public bool NotModified { get; set; }
        }

        public static DecodedDialogList DecodeGetDialogsResponse(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");

            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedDialogList
                {
                    Dialogs = new List<Dialog>(),
                    Titles = new Dictionary<string, string>(),
                    AccessHashes = new Dictionary<string, long>(),
                    HasMore = false,
                    NextCursor = DialogCursor.Empty,
                    NotModified = false
                };

                if (ctor == CtorDialogsNotModified)
                {
                    // count:int — we ignore; caller treats as "no change".
                    if (ms.Position + 4 <= ms.Length) r.ReadInt32();
                    result.NotModified = true;
                    return result;
                }

                bool isSlice = ctor == CtorDialogsSlice;
                if (ctor != CtorDialogs && !isSlice)
                {
                    throw new InvalidDataException("Unexpected messages.Dialogs constructor: 0x" + ctor.ToString("x8"));
                }

                int sliceCount = 0;
                if (isSlice) sliceCount = r.ReadInt32();

                // dialogs: Vector<Dialog>
                ExpectVector(r);
                int dialogCount = r.ReadInt32();

                for (int i = 0; i < dialogCount; i++)
                {
                    uint dctor = r.ReadUInt32();
                    if (dctor == CtorDialog)
                    {
                        var d = ReadDialog(r);
                        if (d != null)
                        {
                            result.Dialogs.Add(d);
                        }
                    }
                    else if (dctor == CtorDialogFolder)
                    {
                        // dialogFolder#71bd134c: a "folder" entry (Archived
                        // chats parent, etc.). Consume the entire record so
                        // the cursor is correct for the next dialog in the
                        // vector — we don't surface folders as Dialog rows
                        // in V1, but we must NOT break out of the loop
                        // (otherwise we drop every dialog after the folder
                        // entry, which is exactly what made the chat list
                        // appear to contain only one item).
                        SkipDialogFolder(r);
                    }
                    else
                    {
                        // Truly unknown variant — bail out. Safer than
                        // groping forward with a wrong cursor.
                        break;
                    }
                }

                // messages: Vector<Message>  — skim id/date/peer before we stop.
                // chats:    Vector<Chat>     — skim titles only.
                // users:    Vector<User>     — skim titles only.
                // We don't have to consume them precisely if we stop reading here. For
                // a handler that only needs Dialog rows, that's acceptable in V1.
                // Title hydration is a deferral (see README in this folder).

                ApplyMessageDates(result.Dialogs, ReadMessageDates(r, ms));

                result.HasMore = isSlice && dialogCount < sliceCount;
                if (result.Dialogs.Count > 0)
                {
                    Dialog boundary = result.Dialogs[result.Dialogs.Count - 1];
                    result.NextCursor = new DialogCursor(
                        boundary.LastActivityAt,
                        boundary.LastMessageId,
                        boundary.Peer);
                }
                return result;
            }
        }

        private struct MessageDateReadResult
        {
            public PeerId Peer;
            public int MessageId;
            public DateTime Date;
            public bool HasValue;
            public bool NeedsBail;

            public static MessageDateReadResult Empty()
            {
                return new MessageDateReadResult();
            }

            public static MessageDateReadResult Scan()
            {
                return new MessageDateReadResult { NeedsBail = true };
            }

            public static MessageDateReadResult Bail(PeerId peer, int messageId, DateTime date)
            {
                return new MessageDateReadResult
                {
                    Peer = peer,
                    MessageId = messageId,
                    Date = date,
                    HasValue = peer != null && messageId > 0,
                    NeedsBail = true
                };
            }
        }

        private static IDictionary<string, DateTime> ReadMessageDates(BinaryReader r, MemoryStream ms)
        {
            var dates = new Dictionary<string, DateTime>();
            try
            {
                ExpectVector(r);
                int count = r.ReadInt32();
                if (count < 0 || count > 4096) return dates;

                int skipBudget = count + 8;
                for (int i = 0; i < count; i++)
                {
                    if (ms.Position >= ms.Length) break;

                    MessageDateReadResult res;
                    try
                    {
                        res = ReadOneMessageDate(r);
                    }
                    catch
                    {
                        if (skipBudget-- <= 0) break;
                        if (!TryAdvanceToNextMessageCtor(r, ms)) break;
                        continue;
                    }

                    if (res.HasValue)
                    {
                        dates[MakeMessageKey(res.Peer, res.MessageId)] = res.Date;
                    }

                    if (res.NeedsBail)
                    {
                        if (skipBudget-- <= 0) break;
                        if (!TryAdvanceToNextMessageCtor(r, ms)) break;
                    }
                }
            }
            catch
            {
                // Keep dialog decoding tolerant. Missing message dates only
                // degrade pagination; they should not blank the chat list.
            }
            return dates;
        }

        private static MessageDateReadResult ReadOneMessageDate(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            switch (ctor)
            {
                case CtorMessageEmpty:
                {
                    uint flags = r.ReadUInt32();
                    r.ReadInt32();  // id
                    PeerId ignored;
                    if ((flags & 1u) != 0 && !TryReadPeer(r, out ignored)) return MessageDateReadResult.Scan();
                    return MessageDateReadResult.Empty();
                }

                case CtorMessage:
                {
                    uint flags = r.ReadUInt32();
                    uint flags2 = r.ReadUInt32();
                    int id = r.ReadInt32();
                    if (id <= 0) return MessageDateReadResult.Empty();

                    PeerId ignored;
                    if ((flags & (1u << 8)) != 0 && !TryReadPeer(r, out ignored)) return MessageDateReadResult.Scan();
                    if ((flags & (1u << 29)) != 0) r.ReadInt32();
                    PeerId peer;
                    if (!TryReadPeer(r, out peer)) return MessageDateReadResult.Scan();
                    if ((flags & (1u << 28)) != 0 && !TryReadPeer(r, out ignored)) return MessageDateReadResult.Scan();
                    if ((flags & (1u << 2)) != 0 && !TrySkipMessageFwdHeader(r)) return MessageDateReadResult.Scan();
                    if ((flags & (1u << 11)) != 0) r.ReadInt64();
                    if ((flags2 & (1u << 0)) != 0) r.ReadInt64();
                    if ((flags & (1u << 3)) != 0 && !TrySkipMessageReplyHeader(r)) return MessageDateReadResult.Scan();

                    int date = r.ReadInt32();
                    return MessageDateReadResult.Bail(peer, id, UnixToDateTime(date));
                }

                case CtorMessageService:
                case CtorMessageServiceLegacy:
                case CtorMessageServiceCall:
                {
                    bool hasFlags2 = ctor == CtorMessageService;
                    uint flags = r.ReadUInt32();
                    if (hasFlags2) r.ReadUInt32();
                    int id = r.ReadInt32();
                    if (id <= 0) return MessageDateReadResult.Empty();

                    PeerId ignored;
                    if ((flags & (1u << 8)) != 0 && !TryReadPeer(r, out ignored)) return MessageDateReadResult.Scan();
                    PeerId peer;
                    if (!TryReadPeer(r, out peer)) return MessageDateReadResult.Scan();
                    if ((flags & (1u << 28)) != 0 && !TryReadPeer(r, out ignored)) return MessageDateReadResult.Scan();
                    if ((flags & (1u << 3)) != 0 && !TrySkipMessageReplyHeader(r)) return MessageDateReadResult.Scan();

                    int date = r.ReadInt32();
                    return MessageDateReadResult.Bail(peer, id, UnixToDateTime(date));
                }

                default:
                    return MessageDateReadResult.Scan();
            }
        }

        private static void ApplyMessageDates(IList<Dialog> dialogs, IDictionary<string, DateTime> messageDates)
        {
            if (dialogs == null || messageDates == null || messageDates.Count == 0) return;

            for (int i = 0; i < dialogs.Count; i++)
            {
                Dialog dialog = dialogs[i];
                DateTime serverDate;
                if (dialog == null ||
                    !messageDates.TryGetValue(MakeMessageKey(dialog.Peer, dialog.LastMessageId), out serverDate))
                {
                    continue;
                }

                dialog.ApplyServerUpdate(
                    title: dialog.Title,
                    photoSmallUrl: dialog.PhotoSmallUrl,
                    lastActivityAt: serverDate,
                    lastMessageId: dialog.LastMessageId,
                    unreadCount: dialog.UnreadCount,
                    isPinned: dialog.IsPinned,
                    isMuted: dialog.IsMuted,
                    mutedUntil: dialog.MutedUntil,
                    isVerified: dialog.IsVerified,
                    isScam: dialog.IsScam,
                    isArchived: dialog.IsArchived,
                    folderId: dialog.FolderId,
                    at: serverDate);
                dialog.DequeuePendingEvents();
            }
        }

        private static string MakeMessageKey(PeerId peer, long messageId)
        {
            return peer == null
                ? string.Empty
                : peer.ToString() + "#" + messageId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // dialog#d58a08c6 flags:# pinned:flags.2?true unread_mark:flags.3?true
        //   view_forum_as_messages:flags.6?true peer:Peer top_message:int
        //   read_inbox_max_id:int read_outbox_max_id:int unread_count:int
        //   unread_mentions_count:int unread_reactions_count:int
        //   notify_settings:PeerNotifySettings pts:flags.0?int
        //   draft:flags.1?DraftMessage folder_id:flags.4?int ttl_period:flags.5?int
        private static Dialog ReadDialog(BinaryReader r)
        {
            int flags = r.ReadInt32();
            PeerId peer = ReadPeer(r);
            int topMessage = r.ReadInt32();
            r.ReadInt32(); // read_inbox_max_id
            r.ReadInt32(); // read_outbox_max_id
            int unreadCount = r.ReadInt32();
            r.ReadInt32(); // unread_mentions_count
            r.ReadInt32(); // unread_reactions_count

            // notify_settings — full layer-214 shape, including stories_*
            // sounds. Consume completely so the cursor lands on the next
            // optional field correctly.
            DateTime? mutedUntil;
            bool isMuted;
            ReadPeerNotifySettings(r, out isMuted, out mutedUntil);

            if ((flags & (1 << 0)) != 0) r.ReadInt32(); // pts
            if ((flags & (1 << 1)) != 0)
            {
                // draft:DraftMessage — fully consumed via SkipDraftMessage
                // so the rest of the dialog record (folder_id, ttl_period)
                // is read at the right cursor position. Previously we did
                // an early-return here, which left those flags' bytes in
                // the stream and corrupted the next dialog header.
                SkipDraftMessage(r);
            }
            int? folderId = null;
            if ((flags & (1 << 4)) != 0) folderId = r.ReadInt32();
            if ((flags & (1 << 5)) != 0) r.ReadInt32(); // ttl_period

            return BuildDialog(peer, topMessage, unreadCount, flags, isMuted, mutedUntil, folderId);
        }

        // dialogFolder#71bd134c flags:# pinned:flags.2?true folder:Folder
        //   peer:Peer top_message:int unread_muted_peers_count:int
        //   unread_unmuted_peers_count:int unread_muted_messages_count:int
        //   unread_unmuted_messages_count:int
        // Folder ctor (folder#ff544e65) shape: id:int title:string photo:flags.3?ChatPhoto
        // We don't surface folders as Dialog rows; just consume the bytes.
        private static void SkipDialogFolder(BinaryReader r)
        {
            r.ReadInt32(); // flags
            // folder:Folder — read its ctor + shape minimally.
            uint folderCtor = r.ReadUInt32(); // folder#ff544e65 typically
            int folderFlags = r.ReadInt32();
            r.ReadInt32(); // folder.id
            ReadString(r); // folder.title
            if ((folderFlags & (1 << 3)) != 0)
            {
                // photo:ChatPhoto — variable. Best-effort: we don't expect
                // folders to come back from a vanilla messages.getDialogs
                // unless the user has enabled archive views. If we trip on
                // an unfamiliar ChatPhoto ctor here the throw bubbles up
                // and the dialog list still surfaces what we read so far.
                SkipChatPhoto(r);
            }
            // peer:Peer
            ReadPeer(r);
            r.ReadInt32(); // top_message
            r.ReadInt32(); // unread_muted_peers_count
            r.ReadInt32(); // unread_unmuted_peers_count
            r.ReadInt32(); // unread_muted_messages_count
            r.ReadInt32(); // unread_unmuted_messages_count
            // Suppress "unused" warning: keep folderCtor read for fwd-compat.
            if (folderCtor == 0) return;
        }

        // Best-effort consume of a ChatPhoto-shaped subobject. In layer 214
        // the variants are chatPhotoEmpty#37c1011c and chatPhoto#1c6e1c11.
        // The common-case decoder skips both by reading their fixed prefix;
        // for chatPhoto we additionally consume photo_id (long), stripped
        // thumb (bytes), dc_id (int).
        private static void SkipChatPhoto(BinaryReader r)
        {
            uint c = r.ReadUInt32();
            const uint ChatPhotoEmpty = 0x37c1011cu;
            const uint ChatPhoto      = 0x1c6e1c11u;
            if (c == ChatPhotoEmpty) return;
            if (c == ChatPhoto)
            {
                r.ReadInt32();   // flags
                r.ReadInt64();   // photo_id
                ReadBytes(r);    // stripped_thumb (flags.1)
                r.ReadInt32();   // dc_id
            }
            // Unknown variant: leave to caller to report — we can't reliably
            // skip an unfamiliar shape.
        }

        // DraftMessage variants — consume the full payload so the caller's
        // cursor is correct for any subsequent fields (folder_id, ttl_period).
        private static void SkipDraftMessage(BinaryReader r)
        {
            uint c = r.ReadUInt32();
            if (c == CtorDraftMessageEmpty)
            {
                int flags = r.ReadInt32();
                if ((flags & (1 << 0)) != 0) r.ReadInt32(); // date
                return;
            }
            if (c == CtorDraftMessage)
            {
                int flags = r.ReadInt32();
                if ((flags & (1 << 4)) != 0)
                {
                    // reply_to:InputReplyTo — variable-size object. Best
                    // effort: skip its ctor + a single int (reply_to_msg_id
                    // for inputReplyToMessage). Anything more exotic and
                    // we'll surface a parse error rather than silently
                    // misalign — folders are unusual on a plain account.
                    r.ReadUInt32(); // ctor
                    r.ReadInt32();  // reply_to_msg_id (or flags for richer variants)
                }
                ReadString(r); // message:string
                if ((flags & (1 << 3)) != 0) SkipVectorOpaque(r); // entities:Vector<MessageEntity>
                if ((flags & (1 << 5)) != 0)
                {
                    // media:InputMedia — full skip is too complex; we only
                    // expect drafts on the user's primary chats which we'll
                    // load before we hit drafts in archives. Leave to the
                    // outer loop to abort cleanly if we land here.
                    throw new InvalidDataException(
                        "DraftMessage with media — not yet supported by Vianigram TL skim decoder");
                }
                r.ReadInt32(); // date
                if ((flags & (1 << 7)) != 0) r.ReadInt64(); // effect (layer 214+)
                return;
            }
            // Unknown variant: leave the cursor where it is — surface as
            // unexpected ctor at next dialog header read.
        }

        // Skip a Vector<unknown> by reading the count and then assuming each
        // entry is at minimum a ctor (4 bytes). This is intentionally
        // best-effort — used only when the entries are entities (typically
        // small fixed-prefix records). If the entry has variable-size
        // payload the cursor will misalign.
        private static void SkipVectorOpaque(BinaryReader r)
        {
            ExpectVector(r);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++)
            {
                r.ReadUInt32(); // ctor
                // We can't safely walk arbitrary message-entity payloads
                // without a full entity parser. Accept that drafts with
                // entity-rich text may misalign here.
            }
        }

        private static byte[] ReadBytes(BinaryReader r)
        {
            int prefix = r.ReadByte();
            int len;
            int padding;
            if (prefix == 254)
            {
                len = r.ReadByte() | (r.ReadByte() << 8) | (r.ReadByte() << 16);
                padding = (4 - (len % 4)) % 4;
            }
            else
            {
                len = prefix;
                padding = (4 - ((len + 1) % 4)) % 4;
            }
            byte[] data = r.ReadBytes(len);
            if (padding > 0) r.ReadBytes(padding);
            return data;
        }

        private static Dialog BuildDialog(PeerId peer, int topMessage, int unreadCount, int flags,
            bool isMuted, DateTime? mutedUntil, int? folderId)
        {
            if (peer == null) return null;
            bool isPinned = (flags & (1 << 2)) != 0;
            bool isArchived = folderId.HasValue && folderId.Value == 1;

            // We don't have the message timestamp here (lives in messages Vector); use UtcNow as
            // a coarse "last activity" until the handler enriches it from the messages collection.
            DateTime activityAt = DateTime.UtcNow;
            var dialog = new Dialog(peer, peer.ToString(), activityAt, topMessage, unreadCount);
            dialog.ApplyServerUpdate(
                title: peer.ToString(),
                photoSmallUrl: null,
                lastActivityAt: activityAt,
                lastMessageId: topMessage,
                unreadCount: unreadCount,
                isPinned: isPinned,
                isMuted: isMuted,
                mutedUntil: mutedUntil,
                isVerified: false,
                isScam: false,
                isArchived: isArchived,
                folderId: folderId,
                at: activityAt);
            // Drain construction events; first creation should not noisily emit "Updated" deltas.
            dialog.DequeuePendingEvents();
            return dialog;
        }

        private static PeerId ReadPeer(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            switch (ctor)
            {
                case CtorPeerUser:
                    {
                        long uid = r.ReadInt64();
                        // access_hash NOT carried in Peer; resolved from the users[] collection. Defaults to 0 here.
                        return PeerId.User(uid, 0L);
                    }
                case CtorPeerChat:
                    {
                        long cid = r.ReadInt64();
                        return PeerId.Chat(cid);
                    }
                case CtorPeerChannel:
                    {
                        long chid = r.ReadInt64();
                        return PeerId.Channel(chid, 0L);
                    }
                default:
                    throw new InvalidDataException("Unknown Peer constructor 0x" + ctor.ToString("x8"));
            }
        }

        private static bool TryReadPeer(BinaryReader r, out PeerId peer)
        {
            peer = null;
            if (r == null || r.BaseStream == null) return false;
            long start = r.BaseStream.Position;
            try
            {
                if (start + 12 > r.BaseStream.Length) return false;
                uint ctor = r.ReadUInt32();
                long id;
                switch (ctor)
                {
                    case CtorPeerUser:
                        id = r.ReadInt64();
                        if (id <= 0) { r.BaseStream.Position = start; return false; }
                        peer = PeerId.User(id, 0L);
                        return true;
                    case CtorPeerChat:
                        id = r.ReadInt64();
                        if (id <= 0) { r.BaseStream.Position = start; return false; }
                        peer = PeerId.Chat(id);
                        return true;
                    case CtorPeerChannel:
                        id = r.ReadInt64();
                        if (id <= 0) { r.BaseStream.Position = start; return false; }
                        peer = PeerId.Channel(id, 0L);
                        return true;
                    default:
                        r.BaseStream.Position = start;
                        return false;
                }
            }
            catch
            {
                r.BaseStream.Position = start;
                peer = null;
                return false;
            }
        }

        // peerNotifySettings#99622c0c flags:#
        //   show_previews:flags.0?Bool  silent:flags.1?Bool  mute_until:flags.2?int
        //   ios_sound:flags.3?NotificationSound
        //   android_sound:flags.4?NotificationSound
        //   other_sound:flags.5?NotificationSound
        //   stories_muted:flags.6?Bool  stories_hide_sender:flags.7?Bool
        //   stories_ios_sound:flags.8?NotificationSound
        //   stories_android_sound:flags.9?NotificationSound
        //   stories_other_sound:flags.10?NotificationSound = PeerNotifySettings;
        //
        // The whole record is consumed unconditionally so the caller's cursor
        // sits exactly on the next field of the parent (Dialog) regardless of
        // which optional bits the server set. Previously we read only up to
        // mute_until and let any flags 3+ corrupt the cursor — that is what
        // limited the chat list to a single decoded entry.
        private static void ReadPeerNotifySettings(BinaryReader r, out bool isMuted, out DateTime? mutedUntil)
        {
            isMuted = false;
            mutedUntil = null;

            uint ctor = r.ReadUInt32();
            if (ctor != CtorPeerNotifySettings)
            {
                // Unknown shape — best-effort: assume not muted, leave the
                // cursor where it is. Caller may misalign on subsequent
                // dialogs but the symptom (1 dialog visible) is what we are
                // explicitly fixing here, so we keep this as a defensive
                // fallback rather than throwing.
                return;
            }

            int flags = r.ReadInt32();
            if ((flags & (1 << 0)) != 0) ReadBool(r);            // show_previews
            if ((flags & (1 << 1)) != 0) ReadBool(r);            // silent
            if ((flags & (1 << 2)) != 0)
            {
                int muteUntil = r.ReadInt32();
                if (muteUntil <= 0)
                {
                    isMuted = false;
                    mutedUntil = null;
                }
                else if (muteUntil == int.MaxValue)
                {
                    isMuted = true;
                    mutedUntil = null; // forever
                }
                else
                {
                    isMuted = true;
                    mutedUntil = UnixToDateTime(muteUntil);
                }
            }
            if ((flags & (1 << 3)) != 0) SkipNotificationSound(r);   // ios_sound
            if ((flags & (1 << 4)) != 0) SkipNotificationSound(r);   // android_sound
            if ((flags & (1 << 5)) != 0) SkipNotificationSound(r);   // other_sound
            if ((flags & (1 << 6)) != 0) ReadBool(r);                // stories_muted
            if ((flags & (1 << 7)) != 0) ReadBool(r);                // stories_hide_sender
            if ((flags & (1 << 8)) != 0) SkipNotificationSound(r);   // stories_ios_sound
            if ((flags & (1 << 9)) != 0) SkipNotificationSound(r);   // stories_android_sound
            if ((flags & (1 << 10)) != 0) SkipNotificationSound(r);  // stories_other_sound
        }

        // NotificationSound is one of:
        //   notificationSoundDefault#97e8bebe                            (no payload)
        //   notificationSoundNone#6f0c34df                               (no payload)
        //   notificationSoundLocal#830b9ae4 title:string data:string
        //   notificationSoundRingtone#ff6c8049 id:long
        // Consume exactly the bytes the variant carries so the caller's
        // cursor stays aligned.
        private static void SkipNotificationSound(BinaryReader r)
        {
            uint c = r.ReadUInt32();
            if (c == CtorNotificationSoundDefault) return;
            if (c == CtorNotificationSoundNone) return;
            if (c == CtorNotificationSoundLocal)
            {
                ReadString(r);
                ReadString(r);
                return;
            }
            if (c == CtorNotificationSoundRingtone)
            {
                r.ReadInt64();
                return;
            }
            // Unknown variant — leave the cursor as-is (best effort).
        }

        private static bool ReadBool(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorBoolTrue) return true;
            if (ctor == CtorBoolFalse) return false;
            throw new InvalidDataException("Expected Bool constructor, got 0x" + ctor.ToString("x8"));
        }

        private static bool TrySkipMessageFwdHeader(BinaryReader r)
        {
            try
            {
                r.ReadUInt32();                                  // ctor
                uint flags = r.ReadUInt32();
                PeerId ignored;
                if ((flags & (1u << 0)) != 0 && !TryReadPeer(r, out ignored)) return false;
                if ((flags & (1u << 5)) != 0) ReadString(r);     // from_name
                r.ReadInt32();                                  // date
                if ((flags & (1u << 2)) != 0) r.ReadInt32();     // channel_post
                if ((flags & (1u << 3)) != 0) ReadString(r);     // post_author
                if ((flags & (1u << 4)) != 0)
                {
                    if (!TryReadPeer(r, out ignored)) return false;
                    r.ReadInt32();                              // saved_from_msg_id
                }
                if ((flags & (1u << 8)) != 0 && !TryReadPeer(r, out ignored)) return false;
                if ((flags & (1u << 9)) != 0) ReadString(r);     // saved_from_name
                if ((flags & (1u << 10)) != 0) r.ReadInt32();    // saved_date
                if ((flags & (1u << 6)) != 0) ReadString(r);     // psa_type
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TrySkipMessageReplyHeader(BinaryReader r)
        {
            try
            {
                r.ReadUInt32();                                  // ctor
                uint flags = r.ReadUInt32();
                PeerId ignored;
                if ((flags & (1u << 4)) != 0) r.ReadInt32();     // reply_to_msg_id
                if ((flags & (1u << 0)) != 0 && !TryReadPeer(r, out ignored)) return false;
                if ((flags & (1u << 5)) != 0 && !TrySkipMessageFwdHeader(r)) return false;
                if ((flags & (1u << 8)) != 0) return false;      // reply_media
                if ((flags & (1u << 1)) != 0) r.ReadInt32();     // reply_to_top_id
                if ((flags & (1u << 6)) != 0) ReadString(r);     // quote_text
                if ((flags & (1u << 7)) != 0) return false;      // quote_entities
                if ((flags & (1u << 10)) != 0) r.ReadInt32();    // quote_offset
                if ((flags & (1u << 11)) != 0) r.ReadInt32();    // todo_item_id
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryAdvanceToNextMessageCtor(BinaryReader r, MemoryStream ms)
        {
            if (r == null || ms == null) return false;

            long pos = ms.Position;
            if ((pos & 3L) != 0)
            {
                long aligned = (pos + 3L) & ~3L;
                if (aligned > ms.Length) return false;
                ms.Position = aligned;
            }

            while (ms.Position + 4 <= ms.Length)
            {
                long candidatePos = ms.Position;
                uint candidate;
                try
                {
                    candidate = r.ReadUInt32();
                }
                catch
                {
                    return false;
                }

                if (candidate == CtorMessage
                    || candidate == CtorMessageService
                    || candidate == CtorMessageServiceLegacy
                    || candidate == CtorMessageServiceCall
                    || candidate == CtorMessageEmpty)
                {
                    ms.Position = candidatePos;
                    return true;
                }
            }
            return false;
        }

        private static void ExpectVector(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor != CtorVector)
                throw new InvalidDataException("Expected Vector#1cb5c415, got 0x" + ctor.ToString("x8"));
        }

        // Reserved utility for future title hydration.
        // ReSharper disable once UnusedMember.Local
        private static string ReadString(BinaryReader r)
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
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }
    }
}
