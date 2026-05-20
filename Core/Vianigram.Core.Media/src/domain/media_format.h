// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// media_format — value object enumerating the supported native media formats
// that Vianigram.Core.Media decodes.
//
// This enum is internal (vianigram::media::domain::FormatKind). The public
// WinMD enum (Vianigram::Media::MediaFormat) lives in api/v1/media_api.h and
// is kept structurally aligned for trivial mapping in the shim layer.
//
// Current surface:
//   Opus  — voice notes (16 kHz mono Opus packets)
//   WebP  — stickers and small images (VP8 / VP8L / VP8X)
//   Jpeg  — Telegram photos (delegated to WinRT BitmapDecoder, see 04-media.md §4)
//   Png   — auxiliary images (delegated to WinRT BitmapDecoder)
//
// Planned: Mp4 (video thumb), Tgs (animated stickers), StrippedThumb.

#include <cstdint>

namespace vianigram { namespace media { namespace domain {

enum class FormatKind : uint8_t {
    Unknown = 0,
    Opus    = 1,
    WebP    = 2,
    Jpeg    = 3,
    Png     = 4,
};

// Magic-byte detection helper. Returns FormatKind::Unknown when the byte
// stream does not match a known signature. Cheap; safe to call before
// dispatching to a decoder.
inline FormatKind DetectFormat(const uint8_t* data, size_t len) {
    if (!data) return FormatKind::Unknown;
    if (len >= 12 &&
        data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F' &&
        data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P') {
        return FormatKind::WebP;
    }
    if (len >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) {
        return FormatKind::Jpeg;
    }
    if (len >= 8 &&
        data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G' &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A) {
        return FormatKind::Png;
    }
    // Opus packets do not carry a self-describing container; voice notes are
    // raw concatenated Opus frames. Detection is by route, not by signature.
    return FormatKind::Unknown;
}

}}} // namespace vianigram::media::domain
