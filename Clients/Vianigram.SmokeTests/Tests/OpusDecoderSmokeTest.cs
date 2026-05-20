// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// OpusDecoderSmokeTest — exercises the AudioDecoder.DecodeOpusAsync path.
//
// Current status: the native side is OpusDecoderStub (returns silence sized
// from the encoded input). The real libopus adapter will replace it
// (see Core/Vianigram.Core.Media/src/infrastructure/opus_decoder.h for the
// forward-declared API surface and the vendor marker).
//
// Until libopus lands, this smoke test verifies that:
//   1. DecodeOpusAsync rejects empty input cleanly.
//   2. DecodeOpusAsync accepts a small synthetic payload and returns a
//      non-empty INT16 PCM buffer at 16 kHz mono — matching the stub
//      contract documented in opus_decoder_stub.cpp.
//   3. The PCM is silence (all zeros) so callers know the stub is still
//      live; the failure mode here is "we forgot to swap in the real
//      adapter after vendoring libopus" — that is exactly what we want
//      this test to surface.
//
// When libopus is vendored, this test is rewritten to:
//   * Embed a known-good 20 ms 48 kHz mono Opus packet (silence frame).
//   * Assert decode produces 960 INT16 samples (= 20 ms * 48 kHz).
//   * Assert encode of 960 zero samples produces a non-trivial payload.
// At that point the silence-output assertion below MUST be inverted (real
// decoder produces silence too for a silent frame, so the test stays valid;
// the new test just additionally asserts the sample count exactly).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.SmokeTests.Tests
{
    public static class OpusDecoderSmokeTest
    {
        private const string Suite = "Media";
        private static readonly object CacheGate = new object();
        private static List<TestEntry> cachedEntries;

        public static async Task<List<TestEntry>> RunAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var cached = TryGetCachedEntries();
            if (cached != null)
                return cached;

            var entries = new List<TestEntry>();
            var rejectEmpty = RunRejectEmptyAsync(ct);
            var decodeStub = RunDecodeStubReturnsSilenceAsync(ct);

            entries.Add(await rejectEmpty.ConfigureAwait(false));
            entries.Add(await decodeStub.ConfigureAwait(false));

            CacheIfSuccessful(entries);
            return entries;
        }

        private static List<TestEntry> TryGetCachedEntries()
        {
            lock (CacheGate)
            {
                return cachedEntries == null
                    ? null
                    : DeterministicSmokeCache.Clone(cachedEntries);
            }
        }

        private static void CacheIfSuccessful(List<TestEntry> entries)
        {
            if (!DeterministicSmokeCache.CanCache(entries))
                return;

            lock (CacheGate)
            {
                if (cachedEntries == null)
                    cachedEntries = DeterministicSmokeCache.Clone(entries);
            }
        }

        // -------------------------------------------------------------
        // Test 1: empty input â†’ Success=false, ErrorMessage non-empty.
        // -------------------------------------------------------------
        private static async Task<TestEntry> RunRejectEmptyAsync(CancellationToken ct)
        {
            var entry = new TestEntry
            {
                Suite = Suite,
                Name = "AudioDecoder.DecodeOpusAsync rejects empty input",
                Passed = false,
                Detail = ""
            };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ct.ThrowIfCancellationRequested();
                var result = await global::Vianigram.Media.AudioDecoder
                    .DecodeOpusAsync(new byte[0])
                    .AsTask(ct)
                    .ConfigureAwait(false);

                if (result == null)
                {
                    entry.Detail = "DecodeOpusAsync returned null.";
                    return entry;
                }
                if (result.Success)
                {
                    entry.Detail = "Empty input was accepted; expected Success=false.";
                    return entry;
                }
                if (string.IsNullOrEmpty(result.ErrorMessage))
                {
                    entry.Detail = "Failure path returned no ErrorMessage.";
                    return entry;
                }

                entry.Passed = true;
                entry.Detail = "Rejected with: " + result.ErrorMessage;
            }
            catch (OperationCanceledException)
            {
                entry.Detail = "Cancelled.";
            }
            catch (Exception ex)
            {
                entry.Detail = "Exception: " + ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                entry.Elapsed = stopwatch.Elapsed;
            }
            return entry;
        }

        // -------------------------------------------------------------
        // Test 2: small synthetic payload â†’ stub returns sized silence.
        //   * Passes when the stub is wired (sample rate 16000,
        //     channels 1, all-zero PCM).
        //   * Will need an update once libopus lands and the real adapter
        //     decodes a real Opus packet.
        // -------------------------------------------------------------
        private static async Task<TestEntry> RunDecodeStubReturnsSilenceAsync(CancellationToken ct)
        {
            var entry = new TestEntry
            {
                Suite = Suite,
                Name = "AudioDecoder.DecodeOpusAsync stub returns sized silence",
                Passed = false,
                Detail = ""
            };
            var stopwatch = Stopwatch.StartNew();

            try
            {
                ct.ThrowIfCancellationRequested();

                // One byte is enough to verify the stub's proportional sizing
                // contract without forcing a large byte[] through the WinRT
                // projection on every smoke run.
                var fakeOpus = new byte[1];
                for (int i = 0; i < fakeOpus.Length; i++)
                {
                    fakeOpus[i] = (byte)(0x60 + (i & 0x0F));
                }

                var result = await global::Vianigram.Media.AudioDecoder
                    .DecodeOpusAsync(fakeOpus)
                    .AsTask(ct)
                    .ConfigureAwait(false);

                if (result == null)
                {
                    entry.Detail = "DecodeOpusAsync returned null.";
                    return entry;
                }
                if (!result.Success)
                {
                    entry.Detail = "Decode failed: "
                        + (string.IsNullOrEmpty(result.ErrorMessage) ? "<no message>" : result.ErrorMessage);
                    return entry;
                }
                if (result.SampleRate != 16000)
                {
                    entry.Detail = "Unexpected SampleRate=" + result.SampleRate + " (expected 16000).";
                    return entry;
                }
                if (result.Channels != 1)
                {
                    entry.Detail = "Unexpected Channels=" + result.Channels + " (expected 1).";
                    return entry;
                }
                if (result.DecodedBytes == null || result.DecodedBytes.Length == 0)
                {
                    entry.Detail = "DecodedBytes was empty.";
                    return entry;
                }
                if ((result.DecodedBytes.Length & 1) != 0)
                {
                    entry.Detail = "DecodedBytes length " + result.DecodedBytes.Length
                        + " is not a multiple of 2 (INT16 samples).";
                    return entry;
                }

                // Stub contract: 256 samples per encoded byte, INT16 â†’ 512 bytes per byte.
                int expectedBytes = fakeOpus.Length * 256 * 2;
                if (result.DecodedBytes.Length != expectedBytes)
                {
                    entry.Detail = "DecodedBytes length " + result.DecodedBytes.Length
                        + " != expected stub size " + expectedBytes
                        + " — stub contract may have changed (or libopus is now wired; "
                        + "rewrite this test to use a real Opus vector).";
                    return entry;
                }

                // Verify silence — every byte should be 0 (stub fills with int16 0).
                int firstNonZero = -1;
                for (int i = 0; i < result.DecodedBytes.Length; i++)
                {
                    if (result.DecodedBytes[i] != 0)
                    {
                        firstNonZero = i;
                        break;
                    }
                }
                if (firstNonZero >= 0)
                {
                    entry.Detail = "Stub PCM had a non-zero byte at offset " + firstNonZero
                        + " — stub contract changed or real codec is live "
                        + "(in which case rewrite this assertion).";
                    return entry;
                }

                entry.Passed = true;
                entry.Detail = "Stub returned " + (result.DecodedBytes.Length / 2)
                    + " INT16 silence samples at 16 kHz mono "
                    + "(libopus vendoring still pending — see opus_decoder.h).";
            }
            catch (OperationCanceledException)
            {
                entry.Detail = "Cancelled.";
            }
            catch (Exception ex)
            {
                entry.Detail = "Exception: " + ex.GetType().Name + ": " + ex.Message;
            }
            finally
            {
                stopwatch.Stop();
                entry.Elapsed = stopwatch.Elapsed;
            }
            return entry;
        }
    }
}
