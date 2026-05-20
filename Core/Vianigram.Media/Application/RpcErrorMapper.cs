// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System.Globalization;
using Vianigram.Media.Domain;

namespace Vianigram.Media.Application
{
    /// <summary>
    /// Maps raw MTProto RPC error strings into typed <see cref="MediaError"/>
    /// values. Anchors the rule that handlers never see wire-level error
    /// strings (M2): adapters call <see cref="Map"/> at the boundary and emit
    /// the typed error.
    ///
    /// <para><b>FLOOD_WAIT_X parsing:</b> Telegram sends <c>FLOOD_WAIT_42</c>
    /// where 42 is seconds. We extract the suffix as an integer and preserve
    /// it on the returned <see cref="MediaError"/>; the chunk retry loop
    /// reads <see cref="MediaError.FloodWaitSeconds"/> directly. If the
    /// suffix fails to parse we fall back to 1 second so the loop still makes
    /// progress instead of hanging.</para>
    ///
    /// <para>Other recognised codes:
    /// <list type="bullet">
    ///   <item><description><c>FILE_REFERENCE_EXPIRED</c>,
    ///         <c>FILE_REFERENCE_INVALID</c> — surfaces as <c>FileNotFound</c>
    ///         so the caller can re-resolve via <c>messages.getMessages</c>.</description></item>
    ///   <item><description><c>FILE_PART_*</c> family — surfaces as
    ///         <c>ProtocolError</c>; usually means we sent a wrong size or
    ///         missed a part.</description></item>
    ///   <item><description><c>OFFSET_INVALID</c>, <c>LIMIT_INVALID</c> —
    ///         <c>InvalidArgument</c>; programming bug on our side.</description></item>
    ///   <item><description><c>CDN_*</c> — <c>NetworkError</c> for now;
    ///         CDN orchestration is not yet implemented.</description></item>
    /// </list></para>
    /// </summary>
    public static class RpcErrorMapper
    {
        public const string FloodWaitPrefix = "FLOOD_WAIT_";
        public const string FileRefExpired = "FILE_REFERENCE_EXPIRED";
        public const string FileRefInvalid = "FILE_REFERENCE_INVALID";
        public const string OffsetInvalid = "OFFSET_INVALID";
        public const string LimitInvalid = "LIMIT_INVALID";
        public const string FilePartInvalid = "FILE_PART_INVALID";
        public const string FilePartsInvalid = "FILE_PARTS_INVALID";
        public const string FilePartTooBig = "FILE_PART_TOO_BIG";
        public const string FilePartEmpty = "FILE_PART_EMPTY";
        public const string FilePartSizeChanged = "FILE_PART_SIZE_CHANGED";

        /// <summary>
        /// Translate a raw error string into a typed <see cref="MediaError"/>.
        /// <paramref name="raw"/> may be <c>null</c>; in that case we return a
        /// generic NetworkError.
        /// </summary>
        public static MediaError Map(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return MediaError.NetworkError("empty rpc error");

            // FLOOD_WAIT_X → preserve seconds.
            if (raw.Length > FloodWaitPrefix.Length && raw.StartsWith(FloodWaitPrefix))
            {
                int seconds;
                string tail = raw.Substring(FloodWaitPrefix.Length);
                if (!int.TryParse(tail, NumberStyles.Integer, CultureInfo.InvariantCulture, out seconds) || seconds < 0)
                {
                    seconds = 1;
                }
                return MediaError.FloodWait(seconds);
            }

            switch (raw)
            {
                case FileRefExpired:
                case FileRefInvalid:
                    return MediaError.FileNotFound(raw);

                case OffsetInvalid:
                case LimitInvalid:
                    return MediaError.InvalidArgument(raw);

                case FilePartInvalid:
                case FilePartsInvalid:
                case FilePartEmpty:
                case FilePartSizeChanged:
                    return MediaError.ProtocolError(raw);

                case FilePartTooBig:
                    return MediaError.ChunkTooLarge(raw);
            }

            if (raw.StartsWith("CDN_"))
                return MediaError.NetworkError(raw);

            return MediaError.ProtocolError(raw);
        }
    }
}
