// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.IO;
using System.Text;
using Vianigram.Media.Domain.ValueObjects;

namespace Vianigram.Media.Infrastructure
{
    /// <summary>
    /// TL serializers for the <c>upload.*</c> methods used by this bounded
    /// context. The native production encoder lives in
    /// <c>Vianigram.Core.Tl</c>; this is the managed path that lets the
    /// wire-up and unit-tests run without taking a hard dependency on the
    /// WinMD.
    ///
    /// <para>Constructor IDs (TL layer 214):</para>
    /// <list type="bullet">
    ///   <item><description><c>upload.getFile         0xbe5335be</c></description></item>
    ///   <item><description><c>upload.saveFilePart    0xb304a621</c></description></item>
    ///   <item><description><c>upload.saveBigFilePart 0xde7b673d</c></description></item>
    /// </list>
    ///
    /// <para>Input-file-location ctors:</para>
    /// <list type="bullet">
    ///   <item><description><c>inputDocumentFileLocation 0xbad07584</c></description></item>
    ///   <item><description><c>inputPhotoFileLocation    0x40181ffe</c></description></item>
    ///   <item><description><c>inputFileLocation         0xdfdaabe1 (legacy)</c></description></item>
    /// </list>
    /// </summary>
    internal static class TlEncoder
    {
        public const uint CtorUploadGetFile = 0xbe5335beu;
        public const uint CtorUploadSaveFilePart = 0xb304a621u;
        public const uint CtorUploadSaveBigFilePart = 0xde7b673du;

        public const uint CtorInputDocumentFileLocation = 0xbad07584u;
        public const uint CtorInputPhotoFileLocation = 0x40181ffeu;
        public const uint CtorInputFileLocationLegacy = 0xdfdaabe1u;
        // Peer-photo download.
        // inputPeerPhotoFileLocation#37257e99 flags:# big:flags.0?true
        //   peer:InputPeer photo_id:long
        public const uint CtorInputPeerPhotoFileLocation = 0x37257e99u;
        // InputPeer sub-types we care about for peer photos.
        public const uint CtorInputPeerEmpty = 0x7f3b18eau;
        public const uint CtorInputPeerSelf = 0x7da07ec9u;
        public const uint CtorInputPeerChat = 0x35a95cb9u;
        public const uint CtorInputPeerUser = 0x7b8e7de6u;
        public const uint CtorInputPeerChannel = 0x20adaef8u;

        // ---------- Downloads ----------

        /// <summary>
        /// Encode <c>upload.getFile</c> for one chunk. Telegram requires
        /// <paramref name="offset"/> to be a multiple of 4 KiB and
        /// <paramref name="limit"/> to divide 1 MiB; the caller (handler) is
        /// responsible for that alignment.
        /// </summary>
        public static byte[] EncodeGetFile(FileLocation location, long offset, int limit, bool precise = false, bool cdnSupported = false)
        {
            if (location == null) throw new ArgumentNullException("location");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorUploadGetFile);

                uint flags = 0;
                if (precise) flags |= 1u;       // bit 0
                if (cdnSupported) flags |= 1u << 1; // bit 1
                w.Write(flags);

                WriteInputFileLocation(w, location);

                w.Write(offset);
                w.Write(limit);

                w.Flush();
                return ms.ToArray();
            }
        }

        // ---------- Uploads ----------

        public static byte[] EncodeSaveFilePart(long fileId, int filePart, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorUploadSaveFilePart);
                w.Write(fileId);
                w.Write(filePart);
                WriteBytes(w, bytes);

                w.Flush();
                return ms.ToArray();
            }
        }

        public static byte[] EncodeSaveBigFilePart(long fileId, int filePart, int fileTotalParts, byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException("bytes");
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms, Encoding.UTF8))
            {
                w.Write(CtorUploadSaveBigFilePart);
                w.Write(fileId);
                w.Write(filePart);
                w.Write(fileTotalParts);
                WriteBytes(w, bytes);

                w.Flush();
                return ms.ToArray();
            }
        }

        // ---------- Helpers ----------

        private static void WriteInputFileLocation(BinaryWriter w, FileLocation loc)
        {
            switch (loc.Kind)
            {
                case FileLocationKind.Document:
                    w.Write(CtorInputDocumentFileLocation);
                    w.Write(loc.Id);
                    w.Write(loc.AccessHash);
                    WriteBytes(w, loc.FileReference);
                    WriteString(w, loc.ThumbSize);
                    break;
                case FileLocationKind.Photo:
                    w.Write(CtorInputPhotoFileLocation);
                    w.Write(loc.Id);
                    w.Write(loc.AccessHash);
                    WriteBytes(w, loc.FileReference);
                    WriteString(w, loc.ThumbSize);
                    break;
                case FileLocationKind.Legacy:
                    w.Write(CtorInputFileLocationLegacy);
                    w.Write(loc.VolumeId);
                    w.Write(loc.LocalId);
                    w.Write(loc.Secret);
                    WriteBytes(w, loc.FileReference);
                    break;
                case FileLocationKind.PeerPhoto:
                    // inputPeerPhotoFileLocation#37257e99 flags:#
                    //   big:flags.0?true peer:InputPeer photo_id:long
                    w.Write(CtorInputPeerPhotoFileLocation);
                    uint peerPhotoFlags = 0;
                    if (loc.Big) peerPhotoFlags |= 1u; // bit 0 = big
                    w.Write(peerPhotoFlags);
                    WriteInputPeer(w, loc.PeerKind, loc.PeerId, loc.PeerAccessHash);
                    w.Write(loc.Id); // photo_id
                    break;
                default:
                    throw new ArgumentException("unsupported FileLocation kind");
            }
        }

        private static void WriteInputPeer(BinaryWriter w, PeerPhotoKind kind, long peerId, long peerAccessHash)
        {
            switch (kind)
            {
                case PeerPhotoKind.User:
                    // inputPeerUser#7b8e7de6 user_id:long access_hash:long
                    w.Write(CtorInputPeerUser);
                    w.Write(peerId);
                    w.Write(peerAccessHash);
                    break;
                case PeerPhotoKind.Chat:
                    // inputPeerChat#35a95cb9 chat_id:long
                    w.Write(CtorInputPeerChat);
                    w.Write(peerId);
                    break;
                case PeerPhotoKind.Channel:
                    // inputPeerChannel#20adaef8 channel_id:long access_hash:long
                    w.Write(CtorInputPeerChannel);
                    w.Write(peerId);
                    w.Write(peerAccessHash);
                    break;
                default:
                    // inputPeerEmpty#7f3b18ea — defensive fallback so the
                    // server NACKs cleanly instead of decoding garbage.
                    w.Write(CtorInputPeerEmpty);
                    break;
            }
        }

        private static void WriteBytes(BinaryWriter w, byte[] bytes)
        {
            int len = bytes == null ? 0 : bytes.Length;
            int padding;

            if (len <= 253)
            {
                w.Write((byte)len);
                if (len > 0) w.Write(bytes);
                padding = (4 - ((len + 1) % 4)) % 4;
            }
            else
            {
                w.Write((byte)254);
                w.Write((byte)(len & 0xff));
                w.Write((byte)((len >> 8) & 0xff));
                w.Write((byte)((len >> 16) & 0xff));
                w.Write(bytes);
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
