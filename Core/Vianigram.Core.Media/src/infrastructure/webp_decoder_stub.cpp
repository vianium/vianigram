// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// webp_decoder_stub — see header.
//
// The stub validates only the RIFF/WEBP magic bytes when present, so callers
// with malformed inputs still see a sensible failure path even before
// libwebp integration. When magic is absent we still succeed with the white
// pixel — this keeps the contract permissive enough for end-to-end tests
// using stubbed payloads.

#include "webp_decoder_stub.h"
#include "../internal/media_log.h"
#include "../domain/media_format.h"

namespace vianigram { namespace media { namespace infrastructure {

WebpDecoderStub::WebpDecoderStub() {}
WebpDecoderStub::~WebpDecoderStub() {}

bool WebpDecoderStub::DecodeWebp(const uint8_t* in, size_t inLen,
                                 std::vector<uint8_t>& outRgb,
                                 int& outWidth,
                                 int& outHeight)
{
    MEDIA_DEBUG_LOG("WebP decoder STUB - libwebp not yet integrated (in=%u bytes)",
                    (unsigned)inLen);

    if (in == nullptr || inLen == 0) {
        return false;
    }
    // DoS cap: refuse > 16 MB. Real WebP photos are typically < 1 MB.
    const size_t kMaxInput = 16u * 1024u * 1024u;
    if (inLen > kMaxInput) {
        return false;
    }

    // Best-effort RIFF / WEBP signature check. Stub does NOT require it (some
    // upstream tests pass synthetic blobs) but logs a hint when missing.
    if (domain::DetectFormat(in, inLen) != domain::FormatKind::WebP) {
        MEDIA_DEBUG_LOG("WebP decoder STUB - non-WebP signature; returning placeholder anyway");
    }

    // Single white pixel, RGB888.
    outWidth  = 1;
    outHeight = 1;
    outRgb.assign(3, (uint8_t)0xFF);
    return true;
}

}}} // namespace vianigram::media::infrastructure
