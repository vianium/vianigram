// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// webp_decoder_stub — placeholder for the WebP image decoder.
//
// Returns a 1x1 white pixel regardless of input. Lets upper layers wire
// MediaDecoder::DecodeImageAsync end-to-end without blocking on the libwebp
// vendoring effort.
//
// Integration plan (docs/native-port/04-media.md §3):
//   1. Vendor libwebp/decode + libwebp/dsp + libwebp/utils from the official
//      release tag, pinned in THIRD_PARTY_NOTICES.md.
//   2. Replace with webp_decoder_adapter.{h,cpp} calling WebPDecodeRGBA().
//   3. Add VP8, VP8L, VP8X reference test vectors to media_validation.cpp.
//   4. Widen IImageDecoder contract to RGBA + has_alpha + frame count.

#include "../ports/i_image_decoder.h"

namespace vianigram { namespace media { namespace infrastructure {

class WebpDecoderStub : public ports::IImageDecoder {
public:
    WebpDecoderStub();
    virtual ~WebpDecoderStub();

    virtual bool DecodeWebp(const uint8_t* in, size_t inLen,
                            std::vector<uint8_t>& outRgb,
                            int& outWidth,
                            int& outHeight) override;
};

}}} // namespace vianigram::media::infrastructure
