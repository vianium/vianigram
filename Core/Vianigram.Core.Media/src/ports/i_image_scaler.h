// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// IImageScaler — outbound port for image scaling (resampling).
//
// bilinear_image_scaler.cpp provides the scalar bilinear implementation.
// Planned NEON / SSE2 paths are gated by platform; the scalar path stays as
// the fallback and the self-test reference.
//
// Contract:
//   * Input: `inRgb` is `inW * inH * 3` bytes packed RGB888 (no padding).
//   * Output: `outRgb` is resized to `outW * outH * 3` bytes RGB888.
//   * Both source and destination dimensions must be > 0 and ≤ 4096.
//   * The scaler does NOT preserve aspect ratio internally; callers compute
//     target dimensions and pass them in. Aspect-preserving wrappers belong
//     in the application layer.
//
// Algorithm: bilinear interpolation (4-tap). Acceptable visual quality for
// chat list thumbnails; sticker scales should prefer a lanczos path when
// added.

#include <cstdint>
#include <vector>

namespace vianigram { namespace media { namespace ports {

class IImageScaler {
public:
    virtual ~IImageScaler() {}

    // Scale `inRgb` (RGB888, inW x inH) to `outRgb` (RGB888, outW x outH).
    // Returns true on success; `outRgb` is resized to the exact output size.
    // Returns false on invalid arguments or dimensions out of range.
    virtual bool Scale(const uint8_t* inRgb, int inW, int inH,
                       int outW, int outH,
                       std::vector<uint8_t>& outRgb) = 0;
};

}}} // namespace vianigram::media::ports
