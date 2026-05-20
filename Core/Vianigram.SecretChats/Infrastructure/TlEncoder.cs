// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;

namespace Vianigram.SecretChats.Infrastructure
{
    /// <summary>
    /// Hand-written, MINIMAL TL serializer for the secret-chat RPC shapes
    /// this context issues. Mirrors the per-context approach used by
    /// <c>Vianigram.Contacts</c> / <c>Vianigram.Chats</c>. We encode here
    /// rather than route through <c>Vianigram.Core.Tl</c> because the
    /// layer 214 generated header does not yet include the secret-chat
    /// constructors — those land alongside the crypto-port implementation.
    ///
    /// Outer constructors (sent on the wire):
    ///   * messages.requestEncryption#f64daf43        user_id:InputUser random_id:int g_a:bytes = EncryptedChat;
    ///   * messages.acceptEncryption#3dbc0415         peer:InputEncryptedChat g_b:bytes key_fingerprint:long = EncryptedChat;
    ///   * messages.discardEncryption#f393aea0        flags:# delete_history:flags.0?true chat_id:int = Bool;
    ///   * messages.sendEncrypted#44fa7a15            flags:# silent:flags.0?true peer:InputEncryptedChat random_id:long data:bytes = messages.SentEncryptedMessage;
    ///   * messages.sendEncryptedService#32d439a4     peer:InputEncryptedChat random_id:long data:bytes = messages.SentEncryptedMessage;
    ///   * messages.getDhConfig#26cf8950              version:int random_length:int = messages.DhConfig;
    ///
    /// Inner envelope (encrypted INSIDE the <c>data:bytes</c> of sendEncrypted —
    /// the TL we feed to AES-IGE):
    ///   * decryptedMessage#73e0a6c0 (layer 73)       random_id:long ttl:int message:string media:DecryptedMessageMedia = DecryptedMessage;
    ///   * decryptedMessageMediaEmpty#089f5c4a        = DecryptedMessageMedia;
    ///
    /// All multi-byte integers are little-endian (TL convention). Strings
    /// use the standard TL byte-string framing (1- or 4-byte length prefix
    /// + padding to 4-byte alignment).
    /// </summary>
    internal static class TlEncoder
    {
        // ---- outer (server-bound) RPC constructor ids -----------------------
        public const uint CtorRequestEncryption = 0xf64daf43;
        public const uint CtorAcceptEncryption = 0x3dbc0415;
        public const uint CtorDiscardEncryption = 0xf393aea0;
        public const uint CtorSendEncrypted = 0x44fa7a15;
        public const uint CtorSendEncryptedService = 0x32d439a4;
        public const uint CtorGetDhConfig = 0x26cf8950;

        // ---- supporting TL constructors ------------------------------------
        public const uint CtorInputUserEmpty = 0xb98886cf;
        public const uint CtorInputUser = 0xf21158c6;
        public const uint CtorInputEncryptedChat = 0xf141b5e1;
        public const uint CtorBoolFalse = 0xbc799737;
        public const uint CtorBoolTrue = 0x997275b5;

        // ---- inner (encrypted-payload) constructor ids ---------------------
        public const uint CtorDecryptedMessage = 0x73e0a6c0;             // layer 73 shape
        public const uint CtorDecryptedMessageMediaEmpty = 0x089f5c4a;
        public const uint CtorDecryptedMessageService = 0x1be31789;      // layer 17+: random_id:long action:DecryptedMessageAction
        public const uint CtorDecryptedMessageActionSetMessageTTL = 0xa1733aec; // ttl_seconds:int

        // -------------------------------------------------------------------------
        // messages.requestEncryption#f64daf43  user_id:InputUser random_id:int g_a:bytes = EncryptedChat;
        // -------------------------------------------------------------------------
        public static byte[] EncodeRequestEncryption(long userId, long accessHash, int randomId, byte[] gA)
        {
            if (gA == null) throw new ArgumentNullException("gA");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorRequestEncryption);
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
                WriteBytes(w, gA);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.acceptEncryption#3dbc0415  peer:InputEncryptedChat g_b:bytes key_fingerprint:long = EncryptedChat;
        // -------------------------------------------------------------------------
        public static byte[] EncodeAcceptEncryption(int chatId, long accessHash, byte[] gB, long keyFingerprint)
        {
            if (gB == null) throw new ArgumentNullException("gB");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorAcceptEncryption);
                WriteInputEncryptedChat(w, chatId, accessHash);
                WriteBytes(w, gB);
                w.Write(keyFingerprint);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.discardEncryption#f393aea0  flags:# delete_history:flags.0?true chat_id:int = Bool;
        // -------------------------------------------------------------------------
        public static byte[] EncodeDiscardEncryption(int chatId, bool deleteHistory)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorDiscardEncryption);
                int flags = 0;
                if (deleteHistory) flags |= 1 << 0;
                w.Write(flags);
                w.Write(chatId);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.sendEncrypted#44fa7a15
        //   flags:# silent:flags.0?true peer:InputEncryptedChat random_id:long data:bytes = messages.SentEncryptedMessage;
        // -------------------------------------------------------------------------
        public static byte[] EncodeSendEncrypted(int chatId, long accessHash, long randomId, byte[] encryptedData, bool silent)
        {
            if (encryptedData == null) throw new ArgumentNullException("encryptedData");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorSendEncrypted);
                int flags = 0;
                if (silent) flags |= 1 << 0;
                w.Write(flags);
                WriteInputEncryptedChat(w, chatId, accessHash);
                w.Write(randomId);
                WriteBytes(w, encryptedData);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.sendEncryptedService#32d439a4
        //   peer:InputEncryptedChat random_id:long data:bytes = messages.SentEncryptedMessage;
        // -------------------------------------------------------------------------
        public static byte[] EncodeSendEncryptedService(int chatId, long accessHash, long randomId, byte[] encryptedData)
        {
            if (encryptedData == null) throw new ArgumentNullException("encryptedData");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorSendEncryptedService);
                WriteInputEncryptedChat(w, chatId, accessHash);
                w.Write(randomId);
                WriteBytes(w, encryptedData);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // messages.getDhConfig#26cf8950 version:int random_length:int = messages.DhConfig;
        // -------------------------------------------------------------------------
        public static byte[] EncodeGetDhConfig(int version, int randomLength)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorGetDhConfig);
                w.Write(version);
                w.Write(randomLength);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // INNER ENVELOPE — encoded into the bytes that get AES-IGE-encrypted and
        // shipped as the data:bytes field of messages.sendEncrypted.
        //
        // decryptedMessage#73e0a6c0  random_id:long ttl:int message:string media:DecryptedMessageMedia = DecryptedMessage;
        // -------------------------------------------------------------------------
        public static byte[] EncodeDecryptedMessage(long randomId, int ttlSeconds, string message)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorDecryptedMessage);
                w.Write(randomId);
                w.Write(ttlSeconds);
                WriteString(w, message ?? string.Empty);
                w.Write(CtorDecryptedMessageMediaEmpty);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // INNER SERVICE ENVELOPE — wraps a DecryptedMessageAction (e.g. SetMessageTTL)
        //
        // decryptedMessageService#1be31789  random_id:long action:DecryptedMessageAction = DecryptedMessage;
        // decryptedMessageActionSetMessageTTL#a1733aec  ttl_seconds:int = DecryptedMessageAction;
        // -------------------------------------------------------------------------
        public static byte[] EncodeDecryptedMessageServiceSetTtl(long randomId, int ttlSeconds)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(CtorDecryptedMessageService);
                w.Write(randomId);
                w.Write(CtorDecryptedMessageActionSetMessageTTL);
                w.Write(ttlSeconds);
                return ms.ToArray();
            }
        }

        // -------------------------------------------------------------------------
        // helpers
        // -------------------------------------------------------------------------
        private static void WriteInputEncryptedChat(BinaryWriter w, int chatId, long accessHash)
        {
            w.Write(CtorInputEncryptedChat);
            w.Write(chatId);
            w.Write(accessHash);
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
            byte[] bytes = string.IsNullOrEmpty(s) ? new byte[0] : Encoding.UTF8.GetBytes(s);
            WriteBytes(w, bytes);
        }
    }
}
