// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// Public WinRT surface for Vianigram.Core.Media (v1).
//
// Exposed via the .winmd produced by this project; consumed from C# via the
// standard CX projection. Future revisions land under api/v2/ (see
// docs/native-port/04-media.md §10).
//
// API shape rationale:
//   * One `MediaDecodeResult` shared by all four entry points so the managed
//     side has a single result type to box into MVVM models.
//   * Per-modality static `*Async` methods rather than a single fan-in
//     dispatcher: each modality has different inputs (Opus bytes, WebP
//     bytes, RGB + dimensions, MP4 bytes) and a unified signature would
//     turn into an opaque blob that's hard to reason about from XAML.
//   * `MediaSelfTest` mirrors `Vianigram::Crypto::CryptoSelfTest` so the
//     SmokeTests project can run all native self-tests through one pattern.

#include <cstdint>

namespace Vianigram { namespace Media {

// Unified result row for every MediaDecoder entry point.
//
// Field rules:
//   * Success       — true iff the decode completed without error.
//   * ErrorMessage  — populated when Success == false. ASCII; suitable for
//                     in-app diagnostic surfaces. Never localised here.
//   * DecodedBytes  — image: packed RGB888 (Width * Height * 3 bytes).
//                     audio: little-endian INT16 PCM (samples * channels * 2).
//   * Width/Height  — image entry points only. Zero for audio.
//   * SampleRate    — audio entry points only. Zero for image.
//   * Channels      — audio entry points only. Zero for image.
public ref class MediaDecodeResult sealed {
public:
    property bool                     Success;
    property Platform::String^        ErrorMessage;
    property Platform::Array<uint8>^  DecodedBytes;
    property int                      Width;
    property int                      Height;
    property int                      SampleRate;
    property int                      Channels;
};

// Voice-note Opus decoder.
public ref class AudioDecoder sealed {
public:
    static Windows::Foundation::IAsyncOperation<MediaDecodeResult^>^
        DecodeOpusAsync(const Platform::Array<uint8>^ encoded);
};

// Richer WebP decode output with a Channels field and an IBuffer payload.
// Added in advance of the libwebp-backed back-end so callers can migrate to
// the wider contract without a second projection change. See
// docs/native-port/04-media.md §3 ("WebP decode") and §10 ("Public API
// surface (WinMD)") for rationale.
public ref class WebpDecodeOutput sealed {
public:
    property bool                                       Success;
    property Platform::String^                          ErrorMessage;
    property int                                        Width;
    property int                                        Height;
    property int                                        Channels;       // 3 (RGB) or 4 (RGBA)
    property Windows::Storage::Streams::IBuffer^        PixelBuffer;    // row-major, channels * width * height bytes
};

// Sticker / image WebP decoder.
public ref class ImageDecoder sealed {
public:
    // Legacy entry point; returns RGB888 (Width*Height*3 bytes) packed in the
    // shared MediaDecodeResult.DecodedBytes field. Kept for source-level
    // compatibility with existing callers; new callers should prefer
    // DecodeWebpV2Async, which exposes the channel count + IBuffer pixel data.
    static Windows::Foundation::IAsyncOperation<MediaDecodeResult^>^
        DecodeWebpAsync(const Platform::Array<uint8>^ encoded);

    // Backed by infrastructure::WebpDecoder, which currently delegates to the
    // stub (1x1 placeholder) and will swap to a libwebp-backed implementation
    // once the codec is vendored. The shape is the libwebp-friendly one
    // (width/height/channels + pixel buffer), so no second migration is
    // required when that swap lands.
    static Windows::Foundation::IAsyncOperation<WebpDecodeOutput^>^
        DecodeWebpV2Async(const Platform::Array<uint8>^ encoded);
};

// Bilinear image scaler. Inputs and outputs are RGB888 packed.
public ref class ImageScaler sealed {
public:
    static Windows::Foundation::IAsyncOperation<MediaDecodeResult^>^
        ScaleAsync(const Platform::Array<uint8>^ rgb,
                   int inW, int inH,
                   int outW, int outH);
};

// MP4 first-keyframe extractor (currently a stub; a WinRT-backed impl will
// replace it).
public ref class VideoThumbnail sealed {
public:
    static Windows::Foundation::IAsyncOperation<MediaDecodeResult^>^
        ExtractAsync(const Platform::Array<uint8>^ mp4Bytes);
};

// One row of the self-test report. `Passed == false` carries `Detail`
// describing the first byte of mismatch (or other failure cause).
public ref class MediaSelfTestResult sealed {
public:
    property Platform::String^ Name;
    property bool              Passed;
    property Platform::String^ Detail;
};

// Self-test entry point — mirrors Vianigram::Crypto::CryptoSelfTest.
public ref class MediaSelfTest sealed {
public:
    static Windows::Foundation::Collections::IVector<MediaSelfTestResult^>^ RunAll();
};

}} // namespace Vianigram::Media
