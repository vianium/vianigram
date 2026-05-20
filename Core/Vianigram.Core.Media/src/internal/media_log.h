// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

#pragma once
// Debug logging macro for Vianigram.Core.Media.
//
// Pattern mirrors Vianigram.Core.MTProto/src/internal/mtproto_log.h. Wave-1
// logging-format sweep (2026-04-28) standardized the emitted line shape to
// "[HH:MM:SS.fff Media] <fmt>\n" — UTC wall-clock with milliseconds,
// hierarchical component name without the legacy "Vianigram." prefix.
// On phone runs native OutputDebugStringW is not reliably relayed to the
// Visual Studio Output window over USB, while managed EarlyLog/Debug.WriteLine
// is. Keep this native trace opt-in even for Debug builds so media decode
// hot paths do not pay timestamp/format/conversion costs unless a native
// debugger session explicitly defines VIANIGRAM_ENABLE_NATIVE_TRACE.
// In release builds it always compiles to a no-op.
//
// Privacy note: do NOT log raw decoded bytes (audio PCM, image RGBA), only
// shapes (lengths, dimensions, sample rates). Image / audio content can leak
// thumbnails of conversations across debug captures.

#include <cstdint>
#include <windows.h>
#include <cstdio>

#if defined(_DEBUG) && defined(VIANIGRAM_ENABLE_NATIVE_TRACE)
// Anonymous helper — get UTC HH:MM:SS.fff via Win32 GetSystemTime.
static inline void _vg_media_log_timestamp(char* buf, size_t bufsize) {
    SYSTEMTIME st;
    ::GetSystemTime(&st);
    _snprintf_s(buf, bufsize, _TRUNCATE, "%02u:%02u:%02u.%03u",
        (unsigned)st.wHour, (unsigned)st.wMinute, (unsigned)st.wSecond, (unsigned)st.wMilliseconds);
}

#define MEDIA_DEBUG_LOG(fmt, ...) do { \
    char _media_ts[16]; _vg_media_log_timestamp(_media_ts, sizeof(_media_ts)); \
    char _media_dbg[512]; \
    _snprintf_s(_media_dbg, sizeof(_media_dbg), _TRUNCATE, "[%s Media] " fmt "\n", _media_ts, ##__VA_ARGS__); \
    int _media_wlen = MultiByteToWideChar(CP_ACP, 0, _media_dbg, -1, NULL, 0); \
    if (_media_wlen > 0 && _media_wlen < 1024) { \
        wchar_t _media_wbuf[1024]; \
        MultiByteToWideChar(CP_ACP, 0, _media_dbg, -1, _media_wbuf, _media_wlen); \
        ::OutputDebugStringW(_media_wbuf); \
    } \
} while(0)
#else
#define MEDIA_DEBUG_LOG(fmt, ...) ((void)0)
#endif
