// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// video_thumbnail_extractor_stub — placeholder for MP4 keyframe extraction.
// Returns a small 16x16 grey RGB block regardless of input.
//
// Integration plan (docs/native-port/04-media.md §6, Option A):
//   * Wrap Windows::Media::Editing::MediaComposition::CreateFromUriAsync +
//     GetThumbnailAsync from a WinRT shim.
//   * Output remains an RGB888 buffer that managed callers can hand to
//     ImageScaler / display directly.
//   * Optionally vendor a minimal H.264 keyframe-only decoder if telemetry
//     shows server-provided thumbnails are not always available.
//
// The stub deliberately returns a non-trivial (16x16) image so layout code
// downstream can be exercised without trip-wiring on degenerate sizes.

#include <cstdint>
#include <vector>

namespace vianigram { namespace media { namespace infrastructure {

class VideoThumbnailExtractorStub {
public:
    VideoThumbnailExtractorStub();
    ~VideoThumbnailExtractorStub();

    // Returns true on success and fills outRgb / outWidth / outHeight with a
    // 16x16 grey placeholder. Returns false only on null/empty input.
    bool ExtractFirstKeyframe(const uint8_t* in, size_t inLen,
                              std::vector<uint8_t>& outRgb,
                              int& outWidth,
                              int& outHeight);
};

}}} // namespace vianigram::media::infrastructure
