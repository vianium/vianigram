// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// bilinear_image_scaler — scalar bilinear scaler implementation.
//
// Scalar 4-tap bilinear interpolation for RGB888. Adequate visual quality for
// chat-list thumbnails and sticker previews. Performance target on
// Snapdragon S4 (WP8.1 reference device): 1024x1024 → 320x320 in < 80 ms
// with this scalar path; a NEON variant would bring it to < 30 ms.
//
// NEON / SSE2 plan (deferred):
//   * arm_neon: process 4 destination pixels per iteration, vectorise the
//     horizontal interpolation pass; vectorise the four-component blend.
//   * sse2: same shape with __m128i unpacking from RGB888.
//   * Both variants must agree with this scalar path within ±1 per channel
//     (rounding); the self-test ImageScalerNeonVsScalar will enforce it.
//
// Algorithm notes:
//   * Coordinate mapping uses the "centre of pixel" convention so corner
//     pixels are reproduced exactly when outW == inW and outH == inH.
//   * Out-of-range source coordinates are clamped to [0, in-1]; this only
//     happens for the trailing column/row when the scale ratio doesn't
//     divide evenly.
//   * Fixed-point arithmetic is NOT used here for clarity; future SIMD paths
//     will move to Q16 fixed-point internally.

#include "../ports/i_image_scaler.h"

namespace vianigram { namespace media { namespace infrastructure {

class BilinearImageScaler : public ports::IImageScaler {
public:
    BilinearImageScaler();
    virtual ~BilinearImageScaler();

    virtual bool Scale(const uint8_t* inRgb, int inW, int inH,
                       int outW, int outH,
                       std::vector<uint8_t>& outRgb) override;
};

}}} // namespace vianigram::media::infrastructure
