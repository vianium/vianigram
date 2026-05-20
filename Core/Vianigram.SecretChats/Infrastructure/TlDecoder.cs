// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;

namespace Vianigram.SecretChats.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL deserializer for the secret-chat responses
    /// and updates this context consumes. Mirrors
    /// <see cref="TlEncoder"/>'s scope: the generated header
    /// (<c>tl_layer_214.h</c>) does not yet cover these constructors; we
    /// decode manually and a future revision will switch to the codegen path.
    ///
    /// Outer EncryptedChat constructors:
    ///   * encryptedChatEmpty#ab7ec0a0       id:int = EncryptedChat;
    ///   * encryptedChatWaiting#66b25953     id:int access_hash:long date:int admin_id:long participant_id:long = EncryptedChat;
    ///   * encryptedChatRequested (modern)   #48f1d94c flags:# folder_id:flags.0?int id:int access_hash:long date:int admin_id:long participant_id:long g_a:bytes = EncryptedChat;
    ///   * encryptedChat#61f0d4c7            id:int access_hash:long date:int admin_id:long participant_id:long g_a_or_b:bytes key_fingerprint:long = EncryptedChat;
    ///   * encryptedChatDiscarded            #1e1c7c45 flags:# history_deleted:flags.0?true id:int = EncryptedChat;
    ///
    /// EncryptedMessage constructors:
    ///   * encryptedMessage#ed18c118        random_id:long chat_id:int date:int bytes:bytes file:EncryptedFile = EncryptedMessage;
    ///   * encryptedMessageService#23734b06 random_id:long chat_id:int date:int bytes:bytes = EncryptedMessage;
    ///
    /// messages.SentEncryptedMessage:
    ///   * messages.sentEncryptedMessage#560f8935 date:int = messages.SentEncryptedMessage;
    ///   * messages.sentEncryptedFile#9baca40 date:int file:EncryptedFile = messages.SentEncryptedMessage;
    ///
    /// messages.DhConfig:
    ///   * messages.dhConfigNotModified#c0e24635 random:bytes = messages.DhConfig;
    ///   * messages.dhConfig#2c221edd g:int p:bytes version:int random:bytes = messages.DhConfig;
    ///
    /// Inner envelope (decoded after AES-IGE decrypt):
    ///   * decryptedMessage#73e0a6c0 random_id:long ttl:int message:string media:DecryptedMessageMedia = DecryptedMessage;
    /// </summary>
    internal static class TlDecoder
    {
        // ---- TL constructor ids ---------------------------------------------
        public const uint CtorEncryptedChatEmpty = 0xab7ec0a0;
        public const uint CtorEncryptedChatWaiting = 0x66b25953;
        public const uint CtorEncryptedChatRequested = 0x48f1d94c;
        public const uint CtorEncryptedChat = 0x61f0d4c7;
        public const uint CtorEncryptedChatDiscarded = 0x1e1c7c45;

        public const uint CtorEncryptedMessage = 0xed18c118;
        public const uint CtorEncryptedMessageService = 0x23734b06;

        public const uint CtorSentEncryptedMessage = 0x560f8935;
        public const uint CtorSentEncryptedFile = 0x09baca40;

        public const uint CtorDhConfigNotModified = 0xc0e24635;
        public const uint CtorDhConfig = 0x2c221edd;

        public const uint CtorEncryptedFileEmpty = 0xc21f497e;
        public const uint CtorEncryptedFile = 0xa8008cd8;

        public const uint CtorDecryptedMessage = 0x73e0a6c0;

        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        // ---- result containers ---------------------------------------------

        /// <summary>Distilled view of every <c>EncryptedChat</c> shape.</summary>
        public sealed class DecodedEncryptedChat
        {
            public enum ShapeKind { Empty, Waiting, Requested, Established, Discarded }
            public ShapeKind Shape { get; set; }
            public int ChatId { get; set; }
            public long AccessHash { get; set; }
            public int Date { get; set; }
            public long AdminId { get; set; }
            public long ParticipantId { get; set; }
            /// <summary><c>g_a</c> (Requested) or <c>g_a_or_b</c> (Established). Null on other shapes.</summary>
            public byte[] DhPublicValue { get; set; }
            /// <summary>Established only — peer-asserted key fingerprint.</summary>
            public long KeyFingerprint { get; set; }
            /// <summary>Discarded only — true when peer requested a history wipe.</summary>
            public bool HistoryDeleted { get; set; }
        }

        public sealed class DecodedEncryptedMessage
        {
            public bool IsService { get; set; }
            public long RandomId { get; set; }
            public int ChatId { get; set; }
            public int Date { get; set; }
            public byte[] Bytes { get; set; }
            /// <summary>encryptedFile reference; null for service / no-file messages.</summary>
            public DecodedEncryptedFile File { get; set; }
        }

        public sealed class DecodedEncryptedFile
        {
            public bool IsEmpty { get; set; }
            public long Id { get; set; }
            public long AccessHash { get; set; }
            public int Size { get; set; }
            public int DcId { get; set; }
            public int KeyFingerprint { get; set; }
        }

        public sealed class DecodedSentEncryptedMessage
        {
            public int Date { get; set; }
            public DecodedEncryptedFile File { get; set; }
        }

        public sealed class DecodedDhConfig
        {
            public bool NotModified { get; set; }
            public int G { get; set; }
            public byte[] P { get; set; }
            public int Version { get; set; }
            public byte[] Random { get; set; }
        }

        public sealed class DecodedDecryptedMessage
        {
            public long RandomId { get; set; }
            public int Ttl { get; set; }
            public string Message { get; set; }
            // media decoded as opaque bytes for now — typed DecryptedMessageMedia is planned.
        }

        // ---- top-level decoders --------------------------------------------

        public static DecodedEncryptedChat DecodeEncryptedChat(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                return ReadEncryptedChat(r);
            }
        }

        public static DecodedEncryptedMessage DecodeEncryptedMessage(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedEncryptedMessage();
                if (ctor == CtorEncryptedMessage)
                {
                    result.IsService = false;
                    result.RandomId = r.ReadInt64();
                    result.ChatId = r.ReadInt32();
                    result.Date = r.ReadInt32();
                    result.Bytes = ReadBytes(r);
                    result.File = ReadEncryptedFile(r);
                    return result;
                }
                if (ctor == CtorEncryptedMessageService)
                {
                    result.IsService = true;
                    result.RandomId = r.ReadInt64();
                    result.ChatId = r.ReadInt32();
                    result.Date = r.ReadInt32();
                    result.Bytes = ReadBytes(r);
                    return result;
                }
                throw new InvalidDataException("Unexpected EncryptedMessage constructor: 0x" + ctor.ToString("x8"));
            }
        }

        public static DecodedSentEncryptedMessage DecodeSentEncryptedMessage(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedSentEncryptedMessage();
                if (ctor == CtorSentEncryptedMessage)
                {
                    result.Date = r.ReadInt32();
                    return result;
                }
                if (ctor == CtorSentEncryptedFile)
                {
                    result.Date = r.ReadInt32();
                    result.File = ReadEncryptedFile(r);
                    return result;
                }
                throw new InvalidDataException("Unexpected messages.SentEncryptedMessage constructor: 0x" + ctor.ToString("x8"));
            }
        }

        public static DecodedDhConfig DecodeDhConfig(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty TL payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                var result = new DecodedDhConfig();
                if (ctor == CtorDhConfigNotModified)
                {
                    result.NotModified = true;
                    result.Random = ReadBytes(r);
                    return result;
                }
                if (ctor == CtorDhConfig)
                {
                    result.NotModified = false;
                    result.G = r.ReadInt32();
                    result.P = ReadBytes(r);
                    result.Version = r.ReadInt32();
                    result.Random = ReadBytes(r);
                    return result;
                }
                throw new InvalidDataException("Unexpected messages.DhConfig constructor: 0x" + ctor.ToString("x8"));
            }
        }

        /// <summary>
        /// Decode the inner envelope after AES-IGE decryption. Currently only
        /// recognizes <c>decryptedMessage#73e0a6c0</c>; service messages
        /// (read receipts, TTL changes, rekey actions) are planned.
        /// </summary>
        public static DecodedDecryptedMessage DecodeDecryptedMessage(byte[] payload)
        {
            if (payload == null || payload.Length < 4) throw new InvalidDataException("empty inner payload");
            using (var ms = new MemoryStream(payload, false))
            using (var r = new BinaryReader(ms))
            {
                uint ctor = r.ReadUInt32();
                if (ctor != CtorDecryptedMessage)
                    throw new InvalidDataException("This decoder only supports decryptedMessage#73e0a6c0; got 0x" + ctor.ToString("x8"));
                var result = new DecodedDecryptedMessage();
                result.RandomId = r.ReadInt64();
                result.Ttl = r.ReadInt32();
                result.Message = ReadString(r);
                // media: not consumed yet. The TLs we encode write
                // decryptedMessageMediaEmpty so this is safe; if a real
                // message arrives with media we just stop short.
                return result;
            }
        }

        // ---- shared readers -------------------------------------------------

        private static DecodedEncryptedChat ReadEncryptedChat(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            var c = new DecodedEncryptedChat();

            if (ctor == CtorEncryptedChatEmpty)
            {
                c.Shape = DecodedEncryptedChat.ShapeKind.Empty;
                c.ChatId = r.ReadInt32();
                return c;
            }

            if (ctor == CtorEncryptedChatWaiting)
            {
                c.Shape = DecodedEncryptedChat.ShapeKind.Waiting;
                c.ChatId = r.ReadInt32();
                c.AccessHash = r.ReadInt64();
                c.Date = r.ReadInt32();
                c.AdminId = r.ReadInt64();
                c.ParticipantId = r.ReadInt64();
                return c;
            }

            if (ctor == CtorEncryptedChatRequested)
            {
                c.Shape = DecodedEncryptedChat.ShapeKind.Requested;
                int flags = r.ReadInt32();
                if ((flags & (1 << 0)) != 0) r.ReadInt32(); // folder_id
                c.ChatId = r.ReadInt32();
                c.AccessHash = r.ReadInt64();
                c.Date = r.ReadInt32();
                c.AdminId = r.ReadInt64();
                c.ParticipantId = r.ReadInt64();
                c.DhPublicValue = ReadBytes(r);
                return c;
            }

            if (ctor == CtorEncryptedChat)
            {
                c.Shape = DecodedEncryptedChat.ShapeKind.Established;
                c.ChatId = r.ReadInt32();
                c.AccessHash = r.ReadInt64();
                c.Date = r.ReadInt32();
                c.AdminId = r.ReadInt64();
                c.ParticipantId = r.ReadInt64();
                c.DhPublicValue = ReadBytes(r);
                c.KeyFingerprint = r.ReadInt64();
                return c;
            }

            if (ctor == CtorEncryptedChatDiscarded)
            {
                c.Shape = DecodedEncryptedChat.ShapeKind.Discarded;
                int flags = r.ReadInt32();
                c.HistoryDeleted = (flags & (1 << 0)) != 0;
                c.ChatId = r.ReadInt32();
                return c;
            }

            throw new InvalidDataException("Unexpected EncryptedChat constructor: 0x" + ctor.ToString("x8"));
        }

        private static DecodedEncryptedFile ReadEncryptedFile(BinaryReader r)
        {
            uint ctor = r.ReadUInt32();
            if (ctor == CtorEncryptedFileEmpty)
            {
                return new DecodedEncryptedFile { IsEmpty = true };
            }
            if (ctor == CtorEncryptedFile)
            {
                var f = new DecodedEncryptedFile { IsEmpty = false };
                f.Id = r.ReadInt64();
                f.AccessHash = r.ReadInt64();
                f.Size = r.ReadInt32();
                f.DcId = r.ReadInt32();
                f.KeyFingerprint = r.ReadInt32();
                return f;
            }
            throw new InvalidDataException("Unexpected EncryptedFile constructor: 0x" + ctor.ToString("x8"));
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
