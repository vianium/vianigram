// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// opus_decoder_stub — see header for rationale.
//
// Contract:
//   * Accept any non-null, non-empty, ≤ 4 MB Opus payload.
//   * Return silence at 16 kHz mono. Sample count is derived from the input
//     size assuming a conservative 2 kbps Opus stream → ~256 samples per
//     input byte at 16-bit PCM. We cap at 2 minutes (1.92 M samples) so the
//     stub cannot be coerced into multi-megabyte allocations.
//   * Optionally logs through MEDIA_DEBUG_LOG when native tracing is enabled.
//
// This file MUST NOT be linked into a release build that ships voice-note
// playback to users. The integration smoke test asserts the stub is gated
// behind a Debug-only feature flag once the real decoder lands.

#include "opus_decoder_stub.h"
#include "../internal/media_log.h"

#include <cstring>

namespace vianigram { namespace media { namespace infrastructure {

OpusDecoderStub::OpusDecoderStub() {}
OpusDecoderStub::~OpusDecoderStub() {}

bool OpusDecoderStub::DecodeOpus(const uint8_t* in, size_t inLen,
                                 std::vector<int16_t>& outPcm,
                                 int& outSampleRate,
                                 int& outChannels)
{
    MEDIA_DEBUG_LOG("Opus decoder STUB - libopus not yet integrated (in=%u bytes)",
                    (unsigned)inLen);

    if (in == nullptr || inLen == 0) {
        return false;
    }
    // DoS cap: refuse anything larger than 4 MB encoded — a 60-second voice
    // note at 32 kbps is ~240 KB, and Telegram caps voice notes at one hour
    // which would still be ~14 MB. We reject 4 MB+ here; the production
    // adapter will lift this cap once the real decoder enforces RFC 6716
    // packet limits.
    const size_t kMaxInput = 4u * 1024u * 1024u;
    if (inLen > kMaxInput) {
        return false;
    }

    // Derive a plausible sample count: ~256 samples per encoded byte at
    // 16 kbps (1 byte ≈ 0.5 ms PCM at 16 kHz mono). Clamp to 2 minutes.
    size_t samples = inLen * 256;
    const size_t kMaxSamples = 16000u * 120u; // 1.92 M @ 16 kHz × 120 s
    if (samples > kMaxSamples) samples = kMaxSamples;

    outPcm.assign(samples, (int16_t)0);
    outSampleRate = 16000;
    outChannels   = 1;
    return true;
}

}}} // namespace vianigram::media::infrastructure
