// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// IImageDecoder — outbound port for image decoders.
//
// webp_decoder_stub.cpp is the current adapter; a future webp_decoder_adapter.cpp
// backed by vendored libwebp (BSD 3-clause) will replace it.
//
// Contract:
//   * `in` / `inLen` describe a complete WebP file (RIFF container).
//   * On success: `outRgb` holds outWidth * outHeight * 3 bytes of packed
//     8-bit RGB (no padding, no alpha; alpha is currently dropped — a future
//     revision will widen the contract to RGBA and propagate `has_alpha`).
//   * Maximum accepted dimensions: 4096 x 4096 (max_image_dimensions_policy).
//     Larger inputs return false to prevent DoS via giant decode buffers.
//
// Implementations MUST be:
//   * Reentrant — multiple concurrent decodes are expected (chat list scrolls
//     fan out N decodes in parallel).
//   * Memory-bounded by image dimensions. Reject before allocating output if
//     dimensions exceed cap.

#include <cstdint>
#include <vector>

namespace vianigram { namespace media { namespace ports {

class IImageDecoder {
public:
    virtual ~IImageDecoder() {}

    // Decode a WebP container into packed RGB888.
    // Returns true on success; outRgb / outWidth / outHeight are filled.
    // Returns false on error; out parameters are unspecified.
    virtual bool DecodeWebp(const uint8_t* in, size_t inLen,
                            std::vector<uint8_t>& outRgb,
                            int& outWidth,
                            int& outHeight) = 0;
};

}}} // namespace vianigram::media::ports
