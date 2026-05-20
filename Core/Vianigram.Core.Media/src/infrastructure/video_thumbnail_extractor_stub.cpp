// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// video_thumbnail_extractor_stub — see header.

#include "video_thumbnail_extractor_stub.h"
#include "../internal/media_log.h"

namespace vianigram { namespace media { namespace infrastructure {

VideoThumbnailExtractorStub::VideoThumbnailExtractorStub() {}
VideoThumbnailExtractorStub::~VideoThumbnailExtractorStub() {}

bool VideoThumbnailExtractorStub::ExtractFirstKeyframe(const uint8_t* in, size_t inLen,
                                                      std::vector<uint8_t>& outRgb,
                                                      int& outWidth,
                                                      int& outHeight)
{
    MEDIA_DEBUG_LOG("VideoThumbnail STUB - Windows.Media.Editing not yet wired (in=%u bytes)",
                    (unsigned)inLen);

    if (in == nullptr || inLen == 0) {
        return false;
    }

    outWidth  = 16;
    outHeight = 16;
    const size_t bytes = 16u * 16u * 3u; // 768
    outRgb.assign(bytes, (uint8_t)0x80); // mid-grey
    return true;
}

}}} // namespace vianigram::media::infrastructure
