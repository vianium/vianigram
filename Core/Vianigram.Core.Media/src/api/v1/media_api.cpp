// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// WinRT shim for Vianigram.Core.Media.
//
// Bridges projected ref classes to the native infrastructure adapters. The
// implementation deliberately constructs a fresh adapter per call — the stubs
// are stateless and the bilinear scaler holds no state worth caching at this
// layer; the application-level LRU cache owns that responsibility.

#include "media_api.h"

#include "../../infrastructure/opus_decoder_stub.h"
#include "../../infrastructure/webp_decoder_stub.h"
#include "../../infrastructure/webp_decoder.h"
#include "../../infrastructure/bilinear_image_scaler.h"
#include "../../infrastructure/video_thumbnail_extractor_stub.h"
#include "../../internal/media_validation.h"
#include "../../internal/media_log.h"

#include <ppltasks.h>
#include <collection.h>
#include <vector>
#include <string>
#include <cstring>

using namespace concurrency;
using namespace Platform;
using namespace Windows::Foundation;
using namespace Windows::Foundation::Collections;

namespace Vianigram { namespace Media {

namespace {

Platform::String^ AsciiToPlatformString(const std::string& s) {
    if (s.empty()) return ref new Platform::String(L"");
    std::wstring w;
    w.reserve(s.size());
    for (size_t i = 0; i < s.size(); i++) {
        w.push_back(static_cast<wchar_t>(static_cast<unsigned char>(s[i])));
    }
    return ref new Platform::String(w.c_str());
}

// Copy a Platform::Array<uint8> into a std::vector for use by native code.
// Handles null and empty arrays cleanly. Uses std::memcpy on the contiguous
// Platform::Array storage rather than a per-byte loop on every decode call.
std::vector<uint8_t> CopyArrayToVector(const Platform::Array<uint8>^ src) {
    std::vector<uint8_t> v;
    if (src == nullptr) return v;
    const unsigned len = src->Length;
    if (len == 0) return v;
    v.resize(len);
    std::memcpy(&v[0], src->Data, len);
    return v;
}

// Wrap a Result-like failure into a MediaDecodeResult. Always returns the
// same shape so the managed side has uniform error handling.
MediaDecodeResult^ MakeFailure(const wchar_t* message) {
    auto r = ref new MediaDecodeResult();
    r->Success      = false;
    r->ErrorMessage = ref new Platform::String(message);
    r->DecodedBytes = ref new Platform::Array<uint8>(0);
    r->Width        = 0;
    r->Height       = 0;
    r->SampleRate   = 0;
    r->Channels     = 0;
    return r;
}

// Materialise a vector of bytes into a Platform::Array. Uses std::memcpy on
// the contiguous Platform::Array storage rather than a per-byte loop on every
// decode/scale result.
Platform::Array<uint8>^ ToPlatformArray(const std::vector<uint8_t>& v) {
    const unsigned n = (unsigned)v.size();
    auto arr = ref new Platform::Array<uint8>(n);
    if (n > 0) std::memcpy(arr->Data, &v[0], n);
    return arr;
}

// Materialise a vector of bytes into a WinRT IBuffer. Used by the v2 WebP
// decode path so managed callers can drop the bytes straight into a
// SoftwareBitmap or BitmapSource without a per-byte copy through a managed
// array. The buffer takes a copy on construction (capacity == length).
Windows::Storage::Streams::IBuffer^ ToIBuffer(const std::vector<uint8_t>& v) {
    using namespace Windows::Storage::Streams;
    const unsigned n = (unsigned)v.size();
    auto writer = ref new DataWriter();
    if (n > 0) {
        auto arr = ref new Platform::Array<uint8>(n);
        std::memcpy(arr->Data, &v[0], n);
        writer->WriteBytes(arr);
    }
    return writer->DetachBuffer();
}

WebpDecodeOutput^ MakeWebpFailure(const wchar_t* message) {
    auto out = ref new WebpDecodeOutput();
    out->Success      = false;
    out->ErrorMessage = ref new Platform::String(message);
    out->Width        = 0;
    out->Height       = 0;
    out->Channels     = 0;
    out->PixelBuffer  = ToIBuffer(std::vector<uint8_t>());
    return out;
}

// Materialise INT16 PCM samples as a byte array (LE on every platform we
// support; v120_wp81 targets are all little-endian).
Platform::Array<uint8>^ Int16PcmToPlatformArray(const std::vector<int16_t>& pcm) {
    const unsigned n = (unsigned)(pcm.size() * sizeof(int16_t));
    auto arr = ref new Platform::Array<uint8>(n);
    if (n > 0) std::memcpy(arr->Data, &pcm[0], n);
    return arr;
}

} // anonymous namespace

// ---------------------------------------------------------------------
// AudioDecoder::DecodeOpusAsync
// ---------------------------------------------------------------------
IAsyncOperation<MediaDecodeResult^>^
AudioDecoder::DecodeOpusAsync(const Platform::Array<uint8>^ encoded) {
    // Snapshot input on the calling thread; the lambda runs on a worker.
    auto bytes = CopyArrayToVector(encoded);
    return create_async([bytes]() -> MediaDecodeResult^ {
        if (bytes.empty()) {
            MEDIA_DEBUG_LOG("AudioDecoder::DecodeOpusAsync rejected: empty input");
            return MakeFailure(L"Empty input");
        }
        vianigram::media::infrastructure::OpusDecoderStub decoder;
        std::vector<int16_t> pcm;
        int sampleRate = 0;
        int channels   = 0;
        if (!decoder.DecodeOpus(&bytes[0], bytes.size(), pcm, sampleRate, channels)) {
            return MakeFailure(L"Opus decode failed");
        }
        auto r = ref new MediaDecodeResult();
        r->Success      = true;
        r->ErrorMessage = ref new Platform::String(L"");
        r->DecodedBytes = Int16PcmToPlatformArray(pcm);
        r->Width        = 0;
        r->Height       = 0;
        r->SampleRate   = sampleRate;
        r->Channels     = channels;
        return r;
    });
}

// ---------------------------------------------------------------------
// ImageDecoder::DecodeWebpAsync
// ---------------------------------------------------------------------
IAsyncOperation<MediaDecodeResult^>^
ImageDecoder::DecodeWebpAsync(const Platform::Array<uint8>^ encoded) {
    const std::size_t inputLength =
        encoded == nullptr ? 0u : static_cast<std::size_t>(encoded->Length);
    if (inputLength > vianigram::media::infrastructure::kWebpMaxInputBytes) {
        return create_async([]() -> MediaDecodeResult^ {
            return MakeFailure(L"WebP decode failed: input exceeds 16 MiB cap");
        });
    }

    auto bytes = CopyArrayToVector(encoded);
    return create_async([bytes]() -> MediaDecodeResult^ {
        if (bytes.empty()) {
            MEDIA_DEBUG_LOG("ImageDecoder::DecodeWebpAsync rejected: empty input");
            return MakeFailure(L"Empty input");
        }
        vianigram::media::infrastructure::WebpDecoderStub decoder;
        std::vector<uint8_t> rgb;
        int width  = 0;
        int height = 0;
        if (!decoder.DecodeWebp(&bytes[0], bytes.size(), rgb, width, height)) {
            return MakeFailure(L"WebP decode failed");
        }
        auto r = ref new MediaDecodeResult();
        r->Success      = true;
        r->ErrorMessage = ref new Platform::String(L"");
        r->DecodedBytes = ToPlatformArray(rgb);
        r->Width        = width;
        r->Height       = height;
        r->SampleRate   = 0;
        r->Channels     = 0;
        return r;
    });
}

// ---------------------------------------------------------------------
// ImageDecoder::DecodeWebpV2Async
//
// Backed by infrastructure::WebpDecoder, which currently delegates to the
// stub (1x1 placeholder) and will swap to a libwebp-backed implementation
// once the codec is vendored. Output shape mirrors libwebp's natural
// contract (width / height / channels / packed pixel buffer) so callers do
// not need a second migration when the back-end lands.
// ---------------------------------------------------------------------
IAsyncOperation<WebpDecodeOutput^>^
ImageDecoder::DecodeWebpV2Async(const Platform::Array<uint8>^ encoded) {
    const std::size_t inputLength =
        encoded == nullptr ? 0u : static_cast<std::size_t>(encoded->Length);
    if (inputLength > vianigram::media::infrastructure::kWebpMaxInputBytes) {
        return create_async([]() -> WebpDecodeOutput^ {
            return MakeWebpFailure(L"WebP decode failed: input exceeds 16 MiB cap");
        });
    }

    auto bytes = CopyArrayToVector(encoded);
    return create_async([bytes]() -> WebpDecodeOutput^ {
        auto out = MakeWebpFailure(L"");

        if (bytes.empty()) {
            MEDIA_DEBUG_LOG("ImageDecoder::DecodeWebpV2Async rejected: empty input");
            out->ErrorMessage = ref new Platform::String(L"Empty input");
            return out;
        }

        vianigram::media::infrastructure::WebpDecoder decoder;
        auto r = decoder.Decode(&bytes[0], bytes.size());
        if (!r.IsOk) {
            // Translate the structured MediaError into a string for the
            // managed caller. The shape kept here mirrors MediaDecodeResult
            // so error sites remain log-grep-friendly.
            std::string m = "WebP decode failed: ";
            m += r.Error.Message;
            out->ErrorMessage = AsciiToPlatformString(m);
            return out;
        }
        out->Success      = true;
        out->ErrorMessage = ref new Platform::String(L"");
        out->Width        = r.Value.width;
        out->Height       = r.Value.height;
        out->Channels     = r.Value.channels;
        out->PixelBuffer  = ToIBuffer(r.Value.pixels);
        return out;
    });
}

// ---------------------------------------------------------------------
// ImageScaler::ScaleAsync
// ---------------------------------------------------------------------
IAsyncOperation<MediaDecodeResult^>^
ImageScaler::ScaleAsync(const Platform::Array<uint8>^ rgb,
                        int inW, int inH,
                        int outW, int outH)
{
    auto bytes = CopyArrayToVector(rgb);
    return create_async([bytes, inW, inH, outW, outH]() -> MediaDecodeResult^ {
        if (bytes.empty()) {
            MEDIA_DEBUG_LOG("ImageScaler::ScaleAsync rejected: empty input");
            return MakeFailure(L"Empty input");
        }
        // Cheap argument check before invoking the scaler — keeps the error
        // message specific even for callers that pass nonsense dimensions.
        if (inW <= 0 || inH <= 0 || outW <= 0 || outH <= 0) {
            return MakeFailure(L"Invalid dimensions");
        }
        const size_t expectedIn = (size_t)inW * (size_t)inH * 3u;
        if (bytes.size() != expectedIn) {
            return MakeFailure(L"Input byte count does not match inW*inH*3");
        }

        vianigram::media::infrastructure::BilinearImageScaler scaler;
        std::vector<uint8_t> out;
        if (!scaler.Scale(&bytes[0], inW, inH, outW, outH, out)) {
            return MakeFailure(L"Scale failed");
        }
        auto r = ref new MediaDecodeResult();
        r->Success      = true;
        r->ErrorMessage = ref new Platform::String(L"");
        r->DecodedBytes = ToPlatformArray(out);
        r->Width        = outW;
        r->Height       = outH;
        r->SampleRate   = 0;
        r->Channels     = 0;
        return r;
    });
}

// ---------------------------------------------------------------------
// VideoThumbnail::ExtractAsync
// ---------------------------------------------------------------------
IAsyncOperation<MediaDecodeResult^>^
VideoThumbnail::ExtractAsync(const Platform::Array<uint8>^ mp4Bytes)
{
    auto bytes = CopyArrayToVector(mp4Bytes);
    return create_async([bytes]() -> MediaDecodeResult^ {
        if (bytes.empty()) {
            MEDIA_DEBUG_LOG("VideoThumbnail::ExtractAsync rejected: empty input");
            return MakeFailure(L"Empty input");
        }
        vianigram::media::infrastructure::VideoThumbnailExtractorStub extractor;
        std::vector<uint8_t> rgb;
        int width  = 0;
        int height = 0;
        if (!extractor.ExtractFirstKeyframe(&bytes[0], bytes.size(), rgb, width, height)) {
            return MakeFailure(L"Video thumbnail extraction failed");
        }
        auto r = ref new MediaDecodeResult();
        r->Success      = true;
        r->ErrorMessage = ref new Platform::String(L"");
        r->DecodedBytes = ToPlatformArray(rgb);
        r->Width        = width;
        r->Height       = height;
        r->SampleRate   = 0;
        r->Channels     = 0;
        return r;
    });
}

// ---------------------------------------------------------------------
// MediaSelfTest::RunAll
// ---------------------------------------------------------------------
IVector<MediaSelfTestResult^>^ MediaSelfTest::RunAll() {
    auto results = vianigram::media::internal_tests::MediaValidation::RunAll();

    auto vec = ref new Platform::Collections::Vector<MediaSelfTestResult^>();
    for (size_t i = 0; i < results.size(); i++) {
        auto row = ref new MediaSelfTestResult();
        row->Name   = AsciiToPlatformString(results[i].name);
        row->Passed = results[i].passed;
        row->Detail = AsciiToPlatformString(results[i].detail);
        vec->Append(row);
    }
    return vec;
}

}} // namespace Vianigram::Media
