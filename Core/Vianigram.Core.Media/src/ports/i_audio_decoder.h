// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// IAudioDecoder — outbound port for audio decoders.
//
// The domain depends on this abstract interface; concrete adapters live in
// src/infrastructure/ (opus_decoder_stub.cpp is the current adapter; a future
// opus_decoder_adapter.cpp will be backed by vendored libopus).
//
// Contract:
//   * `in` / `inLen` describe the encoded payload. For Telegram voice notes
//     this is concatenated 20 ms Opus frames at 16 kHz mono, ~16-32 kbps.
//   * On success: `outPcm` is filled with INT16 little-endian PCM samples,
//     `outSampleRate` and `outChannels` describe the format.
//   * On failure: returns false; out parameters are left unspecified. Callers
//     log via MEDIA_DEBUG_LOG and surface the error through MediaDecodeResult.
//
// Implementations MUST be:
//   * Stateless across calls (or document state explicitly). Voice notes are
//     short (≤ 60 s) so re-entrancy via a fresh decoder per call is fine.
//   * Bounded in memory: ≤ 100 KB scratch (libopus state + working buffer).
//   * Defensive: reject `inLen == 0`, `inLen > 4 MB` (DoS cap), and any
//     packet that exceeds RFC 6716 size limits.

#include <cstdint>
#include <vector>

namespace vianigram { namespace media { namespace ports {

class IAudioDecoder {
public:
    virtual ~IAudioDecoder() {}

    // Decode a complete voice-note payload (concatenated Opus packets).
    // Returns true on success and fills outPcm / outSampleRate / outChannels.
    // Returns false on any error; out parameters are not guaranteed valid.
    virtual bool DecodeOpus(const uint8_t* in, size_t inLen,
                            std::vector<int16_t>& outPcm,
                            int& outSampleRate,
                            int& outChannels) = 0;
};

}}} // namespace vianigram::media::ports
