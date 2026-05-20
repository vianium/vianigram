// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once

#include <cstdint>
#include <cstddef>

namespace vianigram { namespace media { namespace infrastructure {

// Frame-level Opus encoder adapter contract. Voice messages use this
// directly; voice calls share the equivalent encoder that ships in the
// sibling vianium-voip repo (Vianium.VoIP), which owns the call packet
// pump and its own capture/playback path.
class OpusEncoder {
public:
    enum Application {
        ApplicationVoip = 0,
        ApplicationAudio = 1,
        ApplicationLowDelay = 2
    };

    OpusEncoder();
    ~OpusEncoder();

    int Init(int sampleRate, int channels, int bitrateBps, Application app);

    int Encode(const int16_t* pcmIn,
               int            pcmFrames,
               uint8_t*       outOpus,
               size_t         outCapacity);

    void Reset();
    void Destroy();

private:
    void* m_state;
    int   m_sampleRate;
    int   m_channels;

    OpusEncoder(const OpusEncoder&);
    OpusEncoder& operator=(const OpusEncoder&);
};

}}} // namespace vianigram::media::infrastructure
