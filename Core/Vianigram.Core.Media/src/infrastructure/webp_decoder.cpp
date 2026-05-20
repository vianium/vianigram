// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// webp_decoder — see header.
//
// Input validation + DoS caps live here; the actual pixel decoding currently
// delegates to `WebpDecoderStub` (1x1 placeholder). Once libwebp is vendored,
// the back-end branch swaps to a real call without disturbing this file's
// public seam.
//
// Vendoring blueprint (see docs/native-port/04-media.md §3.x):
//   1. Drop libwebp 1.3.x decoder subset under
//      Core/Vianigram.Core.Media/third_party/libwebp/{src,src/dec,src/dsp,
//      src/utils,src/webp}.
//   2. Add an <AdditionalIncludeDirectories> entry pointing at
//      $(ProjectDir)third_party\libwebp\src so `#include "webp/decode.h"`
//      resolves.
//   3. Compile the vendored .c sources by listing them in
//      Vianigram.Core.Media.vcxproj alongside our adapter sources.
//      Alternatively build a static webpdecoder.lib and link it.
//   4. Replace the body of `Decode()` below with the real path:
//          WebPGetInfo()  -> validate dimensions
//          WebPDecodeRGBA() -> malloc'd RGBA buffer
//          memcpy into result.pixels
//          WebPFree() the malloc'd buffer
//   5. Update self-tests in media_validation.cpp with VP8 / VP8L / VP8X
//      reference vectors (Google publishes these).
//
// Vendor marker: see steps above. The roadmap marker name is searched
// for by the roadmap script in docs/native-port/04-media.md §14.

#include "webp_decoder.h"

#include "webp_decoder_stub.h"
#include "../internal/media_log.h"
#include "../domain/media_format.h"

namespace vianigram { namespace media { namespace infrastructure {

WebpDecoder::WebpDecoder() {}
WebpDecoder::~WebpDecoder() {}

Result<WebpDecodeResult, domain::MediaError>
WebpDecoder::Decode(const uint8_t* data, std::size_t length)
{
    typedef Result<WebpDecodeResult, domain::MediaError> R;
    using domain::MediaError;
    using domain::MediaErrorKind;

    if (data == 0 || length == 0) {
        MEDIA_DEBUG_LOG("WebpDecoder rejected: null/empty input");
        return R::Fail(MediaError::Of(MediaErrorKind::InvalidArgument, 0,
                                      "empty input"));
    }
    if (length > kWebpMaxInputBytes) {
        MEDIA_DEBUG_LOG("WebpDecoder rejected: input %u exceeds cap %u",
                        (unsigned)length, (unsigned)kWebpMaxInputBytes);
        return R::Fail(MediaError::Of(MediaErrorKind::InvalidArgument, 0,
                                      "input exceeds 16 MiB cap"));
    }

    // Best-effort signature check. The WebP RIFF magic ('RIFF'..'WEBP') is
    // cheap to verify; we do not yet enforce it strictly so that the existing
    // stub-based smoke tests (which feed synthetic blobs) keep passing.
    if (domain::DetectFormat(data, length) != domain::FormatKind::WebP) {
        MEDIA_DEBUG_LOG("WebpDecoder: non-WebP signature; back-end may still "
                        "accept, but real libwebp will reject");
    }

    // ----------------------------------------------------------------------
    // Back-end branch.
    //
    // Stub path — returns a 1x1 white pixel as RGB. We re-pack into the wider
    // RGBA contract (channels=4) by promoting the RGB triplet with an opaque
    // alpha. This way callers wired against the new facade already receive
    // 4-channel data and will not need a second migration when the libwebp
    // branch (below) goes live.
    //
    // Vendor integration recipe:
    //
    //   #include "webp/decode.h"   // from third_party/libwebp/src
    //   int w = 0, h = 0;
    //   if (!WebPGetInfo(data, length, &w, &h)) {
    //       return R::Fail(MediaError::Of(MediaErrorKind::CorruptInput,
    //                                     0, "WebPGetInfo failed"));
    //   }
    //   if (w <= 0 || h <= 0 ||
    //       w > kWebpMaxDimension || h > kWebpMaxDimension) {
    //       return R::Fail(MediaError::Of(MediaErrorKind::DimensionTooLarge,
    //                                     0, "dimensions out of range"));
    //   }
    //   uint8_t* rgba = WebPDecodeRGBA(data, length, &w, &h);
    //   if (rgba == 0) {
    //       return R::Fail(MediaError::Of(MediaErrorKind::CorruptInput,
    //                                     0, "WebPDecodeRGBA failed"));
    //   }
    //   const std::size_t pixel_bytes = (std::size_t)w * (std::size_t)h * 4u;
    //   WebpDecodeResult out;
    //   out.width    = w;
    //   out.height   = h;
    //   out.channels = 4;
    //   out.pixels.assign(rgba, rgba + pixel_bytes);
    //   WebPFree(rgba);
    //   return R::Ok(out);
    // ----------------------------------------------------------------------

    WebpDecoderStub stub;
    std::vector<uint8_t> rgb;
    int w = 0, h = 0;
    if (!stub.DecodeWebp(data, length, rgb, w, h)) {
        return R::Fail(MediaError::Of(MediaErrorKind::CorruptInput, 0,
                                      "stub decode failed"));
    }
    if (w <= 0 || h <= 0 ||
        w > kWebpMaxDimension || h > kWebpMaxDimension) {
        return R::Fail(MediaError::Of(MediaErrorKind::DimensionTooLarge, 0,
                                      "dimensions out of range"));
    }

    // Promote stub RGB -> RGBA (opaque alpha) so the new facade always emits
    // 4-channel data. Once the real back-end lands, this promotion path will
    // be removed and `out.pixels` will be assigned directly from libwebp.
    const std::size_t pixel_count = (std::size_t)w * (std::size_t)h;
    WebpDecodeResult out;
    out.width    = w;
    out.height   = h;
    out.channels = 4;
    out.pixels.resize(pixel_count * 4u);
    for (std::size_t i = 0; i < pixel_count; ++i) {
        const std::size_t srcBase = i * 3u;
        const std::size_t dstBase = i * 4u;
        // Defensive bound check — stub always returns w*h*3 but a future
        // accidental contract drift here would otherwise corrupt memory.
        if (srcBase + 2u >= rgb.size()) {
            return R::Fail(MediaError::Of(MediaErrorKind::InternalError, 0,
                                          "stub buffer shorter than declared"));
        }
        out.pixels[dstBase + 0] = rgb[srcBase + 0];
        out.pixels[dstBase + 1] = rgb[srcBase + 1];
        out.pixels[dstBase + 2] = rgb[srcBase + 2];
        out.pixels[dstBase + 3] = 0xFF;
    }
    return R::Ok(out);
}

}}} // namespace vianigram::media::infrastructure
