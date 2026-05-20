// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Kernel.Result;

namespace Vianigram.Media.Domain
{
    /// <summary>
    /// Errors emitted from the Media bounded context. Wraps a Kernel
    /// <see cref="Error"/> with a typed code so the API surface stays
    /// non-throwing across long-running parallel chunk pipelines.
    ///
    /// FloodWait carries the requested seconds payload exactly as parsed from
    /// the MTProto error string (FLOOD_WAIT_X). RpcErrorMapper is the only
    /// place that constructs FloodWait from a wire string.
    /// </summary>
    public sealed class MediaError
    {
        public MediaError(MediaErrorCode code, string message, int floodWaitSeconds = 0, Error inner = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            FloodWaitSeconds = floodWaitSeconds;
            Inner = inner;
        }

        public MediaErrorCode Code { get; private set; }
        public string Message { get; private set; }

        /// <summary>
        /// Number of seconds the server asked us to wait before retrying. Only
        /// meaningful when <see cref="Code"/> is <see cref="MediaErrorCode.FloodWait"/>.
        /// </summary>
        public int FloodWaitSeconds { get; private set; }

        public Error Inner { get; private set; }

        public static MediaError InvalidArgument(string message)
        {
            return new MediaError(MediaErrorCode.InvalidArgument, message);
        }

        public static MediaError NetworkError(string message, Error inner = null)
        {
            return new MediaError(MediaErrorCode.NetworkError, message, 0, inner);
        }

        public static MediaError FloodWait(int seconds)
        {
            return new MediaError(MediaErrorCode.FloodWait, "FLOOD_WAIT_" + seconds, seconds);
        }

        public static MediaError FileNotFound(string message)
        {
            return new MediaError(MediaErrorCode.FileNotFound, message);
        }

        public static MediaError OutOfDiskSpace(string message)
        {
            return new MediaError(MediaErrorCode.OutOfDiskSpace, message);
        }

        public static MediaError ChecksumMismatch(string message)
        {
            return new MediaError(MediaErrorCode.ChecksumMismatch, message);
        }

        public static MediaError Cancelled(string message)
        {
            return new MediaError(MediaErrorCode.Cancelled, message);
        }

        public static MediaError ChunkTooLarge(string message)
        {
            return new MediaError(MediaErrorCode.ChunkTooLarge, message);
        }

        public static MediaError ProtocolError(string message, Error inner = null)
        {
            return new MediaError(MediaErrorCode.ProtocolError, message, 0, inner);
        }

        public static MediaError InvalidState(string message)
        {
            return new MediaError(MediaErrorCode.InvalidState, message);
        }

        public override string ToString()
        {
            if (Code == MediaErrorCode.FloodWait)
                return "media.FloodWait: wait " + FloodWaitSeconds + "s";
            return "media." + Code + ": " + Message;
        }
    }

    public enum MediaErrorCode
    {
        Unknown = 0,
        InvalidArgument = 1,
        NetworkError = 2,
        FloodWait = 3,
        FileNotFound = 4,
        OutOfDiskSpace = 5,
        ChecksumMismatch = 6,
        Cancelled = 7,
        ChunkTooLarge = 8,
        ProtocolError = 9,
        InvalidState = 10
    }
}
