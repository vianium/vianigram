// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// media_validation — Vianigram.Core.Media self-tests.
//
// Pattern mirrors Vianigram.Core.Crypto/src/internal/crypto_validation.h: each
// test runs in isolation, returns a structured TestResult so failures point at
// the exact primitive. The WinMD shim (api/v1/media_api.cpp) re-exports
// RunAll() through the MediaSelfTest projected ref class.
//
// Tests:
//   1. BilinearScale_2x2_to_4x4               — known-result trivial scale
//   2. BilinearScale_aspect_preserve          — dimension math for 100x50→50x25
//   3. OpusDecoder_StubReturnsExpectedSize    — interface contract for stub

#include <string>
#include <vector>

namespace vianigram { namespace media { namespace internal_tests {

struct TestResult {
    std::string name;
    bool        passed;
    std::string detail;   // empty on pass; mismatch info on fail
};

class MediaValidation {
public:
    // Runs every test. Never throws.
    static std::vector<TestResult> RunAll();

    // True iff every test passed.
    static bool AllPass();
};

}}} // namespace vianigram::media::internal_tests
