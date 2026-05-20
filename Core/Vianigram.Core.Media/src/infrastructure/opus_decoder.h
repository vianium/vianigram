// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once

#include <cstdint>
#include <cstddef>

namespace vianigram { namespace media { namespace infrastructure {

// Frame-level Opus decoder adapter contract. The implementation is kept
// behind this native boundary so voice notes and voice calls can share one
// codec surface without exposing third-party headers to managed contexts.
//
// Lifecycle: Init() once per stream, Decode() N times, Destroy() at end.
// Reset() re-initialises codec state without reallocating, for example
// after a seek or after packet loss that exceeded PLC tolerance.
//
// Thread-safety: one instance per logical stream.
class OpusDecoder {
public:
    OpusDecoder();
    ~OpusDecoder();

    // Valid sample rates are 8000, 12000, 16000, 24000, 48000 Hz.
    // Channels must be 1 or 2. Returns 0 on success, negative on failure.
    int Init(int sampleRate, int channels);

    // Decode one Opus packet into INT16 PCM. pcmCapacityFrames is measured
    // in PCM frames (samples per channel). Returns frames written, or a
    // negative error code.
    int Decode(const uint8_t* opusFrame,
               size_t         frameLen,
               int16_t*       pcmOut,
               int            pcmCapacityFrames);

    void Reset();
    void Destroy();

private:
    void* m_state;
    int   m_sampleRate;
    int   m_channels;

    OpusDecoder(const OpusDecoder&);
    OpusDecoder& operator=(const OpusDecoder&);
};

}}} // namespace vianigram::media::infrastructure
