// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// webp_decoder — facade over the WebP decoding back-end.
//
// Current: the implementation transparently delegates to `WebpDecoderStub`,
// which returns a 1x1 white pixel and is used to keep the upper-layer pipeline
// compilable while the real decoder is out-of-tree.
//
// Planned: replace the stub branch in webp_decoder.cpp with a libwebp-backed
// implementation. The vendoring blueprint is documented in
// docs/native-port/04-media.md §3.x ("Vendoring libwebp"). The header is the
// stable seam; callers (api/v1/media_api.cpp, application use cases) talk to
// `WebpDecoder` and never to `WebpDecoderStub` directly.
//
// Why a separate facade and not a swap-in-place of the stub:
//   * The stub returns RGB (3 channels). Real WebP is RGBA (4) more often than
//     not (stickers are alpha-channel images). The facade speaks the wider
//     contract (channels in {3,4}) so callers do not need a second migration
//     when libwebp lands.
//   * The facade owns DoS caps (16 MiB max input, dimension caps) so they
//     remain enforced regardless of which back-end is wired up.
//   * The Result<T,MediaError> shape gives callers structured failure data;
//     the legacy IImageDecoder bool return is kept for the existing shim path.
//
// Vendor source: libwebp 1.3.x from
// https://chromium.googlesource.com/webm/libwebp. Place the pinned source
// under Core/Vianigram.Core.Media/third_party/libwebp/ and switch the
// back-end branch in webp_decoder.cpp.

#include <cstdint>
#include <cstddef>
#include <vector>

#include "../domain/media_error.h"

namespace vianigram { namespace media { namespace infrastructure {

// Minimal Result<T,E> for native callers. Mirrors the shape used elsewhere in
// the bounded context without introducing a third-party dependency. Kept here
// (not promoted to a domain header) because no other callers need it yet.
template <typename T, typename E>
struct Result {
    bool   IsOk;
    T      Value;
    E      Error;

    Result() : IsOk(false) {}

    static Result<T, E> Ok(const T& v) {
        Result<T, E> r;
        r.IsOk = true;
        r.Value = v;
        return r;
    }
    static Result<T, E> Fail(const E& e) {
        Result<T, E> r;
        r.IsOk = false;
        r.Error = e;
        return r;
    }
};

struct WebpDecodeResult {
    int                  width;
    int                  height;
    int                  channels;   // 3 (RGB) or 4 (RGBA)
    std::vector<uint8_t> pixels;     // row-major, channels * width * height bytes

    WebpDecodeResult() : width(0), height(0), channels(0) {}
};

class WebpDecoder {
public:
    WebpDecoder();
    ~WebpDecoder();

    // Decode WebP-encoded bytes. Caps input at 16 MiB (DoS protection) and
    // image dimensions at 4096x4096 (max_image_dimensions_policy).
    //
    // On success: returns a populated WebpDecodeResult. The pixel buffer is
    // owned by the result and freed when the result is destroyed.
    //
    // On failure: returns a MediaError describing the first failure point.
    // No pixels are allocated.
    Result<WebpDecodeResult, domain::MediaError>
        Decode(const uint8_t* data, std::size_t length);
};

// DoS cap exposed for reuse in WinMD shim and tests. 16 MiB matches the
// historical stub limit and far exceeds any realistic Telegram sticker
// (typical sticker is <80 KiB; profile photo <300 KiB).
static const std::size_t kWebpMaxInputBytes = 16u * 1024u * 1024u;

// Maximum accepted output dimension on either axis. Mirrors
// max_image_dimensions_policy in 04-media.md §13. Sticker/photo content
// from Telegram never exceeds 4096; anything larger is treated as adversarial.
static const int kWebpMaxDimension = 4096;

}}} // namespace vianigram::media::infrastructure
