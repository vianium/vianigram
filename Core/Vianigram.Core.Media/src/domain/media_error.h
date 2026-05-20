// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// media_error — typed error value for the Vianigram.Core.Media bounded context.
//
// Mirrors the shape of vianigram::mtproto::RpcError / Vianium TLS errors:
// every native domain operation that can fail returns either Result-like
// success or a MediaError carrying enough detail to log without leaking
// content. The WinMD shim collapses MediaError to (Success, ErrorMessage)
// in MediaDecodeResult; native callers see the structured form.

#include <cstdint>
#include <string>

namespace vianigram { namespace media { namespace domain {

// Coarse classification of why a decode failed. Stable across builds so
// telemetry can group on Kind.
enum class MediaErrorKind : uint8_t {
    None              = 0,
    InvalidArgument   = 1,   // null buffer, zero length, dimension out of range
    UnsupportedFormat = 2,   // magic bytes do not match expected format
    CorruptInput      = 3,   // header parsed, body refused (CRC, truncation)
    DimensionTooLarge = 4,   // exceeds max_image_dimensions_policy (4096 px)
    InternalError     = 5,   // unexpected exception inside decoder
    NotImplemented    = 6,   // stub path hit; real codec not yet integrated
};

struct MediaError {
    MediaErrorKind Kind;
    int            Code;     // implementation-specific sub-code (libopus error,
                             // libwebp VP8StatusCode, etc.); 0 when none.
    std::string    Message;  // ASCII; safe for OutputDebugString. No payload bytes.

    MediaError() : Kind(MediaErrorKind::None), Code(0) {}

    static MediaError Ok() {
        MediaError e; e.Kind = MediaErrorKind::None; return e;
    }

    static MediaError Of(MediaErrorKind k, int code, const char* msg) {
        MediaError e;
        e.Kind = k;
        e.Code = code;
        e.Message = (msg ? msg : "");
        return e;
    }

    bool IsOk() const { return Kind == MediaErrorKind::None; }
};

// Stable string label for telemetry / logs. Never localised.
inline const char* MediaErrorKindLabel(MediaErrorKind k) {
    switch (k) {
        case MediaErrorKind::None:              return "ok";
        case MediaErrorKind::InvalidArgument:   return "invalid_argument";
        case MediaErrorKind::UnsupportedFormat: return "unsupported_format";
        case MediaErrorKind::CorruptInput:      return "corrupt_input";
        case MediaErrorKind::DimensionTooLarge: return "dimension_too_large";
        case MediaErrorKind::InternalError:     return "internal_error";
        case MediaErrorKind::NotImplemented:    return "not_implemented";
    }
    return "unknown";
}

}}} // namespace vianigram::media::domain
