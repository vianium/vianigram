// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// opus_decoder_stub — placeholder for the Opus voice-note decoder.
//
// This adapter satisfies the IAudioDecoder port without linking libopus. It
// returns silence (zero PCM samples) at 16 kHz mono with a length proportional
// to the encoded input size. Native trace logging is opt-in; managed smoke
// diagnostics provide the visible device signal.
//
// Integration plan (tracked in docs/native-port/04-media.md §2):
//   1. Vendor libopus subset (decoder-only): celt/, silk/, opus/ from the
//      reference repo at a pinned commit.
//   2. Replace this class with opus_decoder_adapter.{h,cpp} wrapping
//      OpusDecoder* and delegating DecodeOpus to opus_decode().
//   3. Add the RFC 6716 Annex A.5 test vectors to media_validation.cpp.
//   4. Wire feature flag so the stub stays available in case a CVE forces
//      an emergency rollback.

#include "../ports/i_audio_decoder.h"

namespace vianigram { namespace media { namespace infrastructure {

class OpusDecoderStub : public ports::IAudioDecoder {
public:
    OpusDecoderStub();
    virtual ~OpusDecoderStub();

    virtual bool DecodeOpus(const uint8_t* in, size_t inLen,
                            std::vector<int16_t>& outPcm,
                            int& outSampleRate,
                            int& outChannels) override;
};

}}} // namespace vianigram::media::infrastructure
