// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// media_validation — self-test harness.
//
// Three tests:
//   1. BilinearScale_2x2_to_4x4
//      A 2x2 RGB image of pure white must upscale to 4x4 and remain pure
//      white in every channel. Catches sign / weight bugs that would tint
//      the upscaled output.
//   2. BilinearScale_aspect_preserve
//      A 100x50 image scaled to 50x25 must produce exactly that many output
//      bytes (50*25*3 = 3750). Verifies dimension math without needing a
//      pixel-accurate ground truth image.
//   3. OpusDecoder_StubReturnsExpectedSize
//      Feeding the stub 64 bytes of synthetic input must produce non-empty
//      INT16 PCM at 16 kHz mono. Validates the interface contract; the real
//      decoder will swap this test for the RFC 6716 Annex A.5 vectors.

#include "media_validation.h"
#include "../infrastructure/bilinear_image_scaler.h"
#include "../infrastructure/opus_decoder_stub.h"

#include <cstdio>
#include <cstdint>
#include <cstring>

namespace vianigram { namespace media { namespace internal_tests {

namespace {

// Format a "first diff at byte N" detail like crypto_validation does.
std::string MismatchDetail(const uint8_t* expected, const uint8_t* actual, size_t len) {
    size_t firstDiff = (size_t)-1;
    for (size_t i = 0; i < len; i++) {
        if (expected[i] != actual[i]) { firstDiff = i; break; }
    }
    char buf[64];
    std::string out = "first diff at byte ";
    _snprintf_s(buf, sizeof(buf), _TRUNCATE, "%u", (unsigned)firstDiff);
    out.append(buf);
    out.append(" of ");
    _snprintf_s(buf, sizeof(buf), _TRUNCATE, "%u", (unsigned)len);
    out.append(buf);
    return out;
}

// =========================================================================
// 1. BilinearScale_2x2_to_4x4 — pure-white in, pure-white out.
// =========================================================================
TestResult TestBilinearScale2x2to4x4() {
    TestResult r; r.name = "BilinearScale_2x2_to_4x4 (white preserved)"; r.passed = false;

    // 2x2 white RGB888 = 12 bytes of 0xFF.
    uint8_t src[12];
    for (int i = 0; i < 12; i++) src[i] = 0xFF;

    infrastructure::BilinearImageScaler scaler;
    std::vector<uint8_t> dst;
    if (!scaler.Scale(src, 2, 2, 4, 4, dst)) {
        r.detail = "Scale returned false";
        return r;
    }
    if (dst.size() != 4u * 4u * 3u) {
        char buf[96];
        _snprintf_s(buf, sizeof(buf), _TRUNCATE,
                    "expected %u bytes, got %u",
                    (unsigned)(4 * 4 * 3), (unsigned)dst.size());
        r.detail = buf;
        return r;
    }

    // Every output byte must be 0xFF (white through bilinear of all-white).
    uint8_t expected[48];
    for (int i = 0; i < 48; i++) expected[i] = 0xFF;
    for (size_t i = 0; i < 48; i++) {
        if (dst[i] != 0xFF) {
            r.detail = MismatchDetail(expected, &dst[0], 48);
            return r;
        }
    }
    r.passed = true;
    return r;
}

// =========================================================================
// 2. BilinearScale_aspect_preserve — output dimension math.
// =========================================================================
TestResult TestBilinearScaleAspectPreserve() {
    TestResult r; r.name = "BilinearScale_aspect_preserve (100x50 -> 50x25 byte count)"; r.passed = false;

    // 100x50 RGB888 = 15000 bytes. Fill with a shallow horizontal gradient so
    // the scaler does real work (catches a no-op identity bug).
    const int inW = 100;
    const int inH = 50;
    std::vector<uint8_t> src((size_t)inW * (size_t)inH * 3u);
    for (int y = 0; y < inH; ++y) {
        for (int x = 0; x < inW; ++x) {
            uint8_t v = (uint8_t)((x * 255) / (inW - 1));
            size_t o = ((size_t)y * (size_t)inW + (size_t)x) * 3u;
            src[o + 0] = v;
            src[o + 1] = v;
            src[o + 2] = v;
        }
    }

    infrastructure::BilinearImageScaler scaler;
    std::vector<uint8_t> dst;
    if (!scaler.Scale(&src[0], inW, inH, 50, 25, dst)) {
        r.detail = "Scale returned false";
        return r;
    }
    const size_t expected = 50u * 25u * 3u;
    if (dst.size() != expected) {
        char buf[96];
        _snprintf_s(buf, sizeof(buf), _TRUNCATE,
                    "expected %u bytes, got %u",
                    (unsigned)expected, (unsigned)dst.size());
        r.detail = buf;
        return r;
    }
    // Sanity floor: first column must be near-black, last column near-white.
    if (dst[0] > 16) {
        r.detail = "left column not black after gradient downscale";
        return r;
    }
    const size_t lastIdx = (50u * 25u - 1u) * 3u;
    if (dst[lastIdx] < 240) {
        r.detail = "right column not white after gradient downscale";
        return r;
    }
    r.passed = true;
    return r;
}

// =========================================================================
// 3. OpusDecoder_StubReturnsExpectedSize — contract verification for stub.
// =========================================================================
TestResult TestOpusStubReturnsExpectedSize() {
    TestResult r; r.name = "OpusDecoder_StubReturnsExpectedSize (16 kHz mono)"; r.passed = false;

    uint8_t encoded[64];
    for (int i = 0; i < 64; i++) encoded[i] = (uint8_t)(i * 3 + 1);

    infrastructure::OpusDecoderStub decoder;
    std::vector<int16_t> pcm;
    int sampleRate = 0;
    int channels   = 0;
    if (!decoder.DecodeOpus(encoded, sizeof(encoded), pcm, sampleRate, channels)) {
        r.detail = "DecodeOpus returned false";
        return r;
    }
    if (sampleRate != 16000) {
        char buf[64];
        _snprintf_s(buf, sizeof(buf), _TRUNCATE,
                    "expected sample_rate=16000, got %d", sampleRate);
        r.detail = buf;
        return r;
    }
    if (channels != 1) {
        char buf[64];
        _snprintf_s(buf, sizeof(buf), _TRUNCATE,
                    "expected channels=1, got %d", channels);
        r.detail = buf;
        return r;
    }
    if (pcm.empty()) {
        r.detail = "stub produced zero samples";
        return r;
    }
    // Stub maps 64 input bytes to 64 * 256 = 16384 samples.
    if (pcm.size() != 16384u) {
        char buf[96];
        _snprintf_s(buf, sizeof(buf), _TRUNCATE,
                    "expected 16384 samples for 64 input bytes, got %u",
                    (unsigned)pcm.size());
        r.detail = buf;
        return r;
    }
    r.passed = true;
    return r;
}

} // anonymous namespace

std::vector<TestResult> MediaValidation::RunAll() {
    std::vector<TestResult> out;
    out.push_back(TestBilinearScale2x2to4x4());
    out.push_back(TestBilinearScaleAspectPreserve());
    out.push_back(TestOpusStubReturnsExpectedSize());
    return out;
}

bool MediaValidation::AllPass() {
    auto results = RunAll();
    for (size_t i = 0; i < results.size(); i++) {
        if (!results[i].passed) return false;
    }
    return true;
}

}}} // namespace vianigram::media::internal_tests
