// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProtoHandshakeSmokeTest - live auth-key availability acceptance test.
//
// The expensive auth-key material is cached in-process by LiveAuthKeyCache
// so later live suites can reuse the same DC key instead of repeating the
// full Diffie-Hellman handshake. This mirrors production behavior: auth keys
// are long-lived per DC and should be persisted securely rather than
// regenerated for every channel.

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Vianigram.SmokeTests.Tests
{
    public static class MtProtoHandshakeSmokeTest
    {
        private const int MaxServerTimeOffsetSeconds = 300;
        private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(45);

        public static async Task<TestEntry> RunAsync(CancellationToken ct)
        {
            var entry = new TestEntry
            {
                Suite = "Live",
                Name = "MTProto auth key availability for test DC #2 (" +
                    LiveAuthKeyCache.TestDcHost + ":" +
                    LiveAuthKeyCache.TestDcPort + ")"
            };
            var stopwatch = Stopwatch.StartNew();

            using (var cts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                cts.CancelAfter(HandshakeTimeout);

                LiveAuthKeyMaterial key = null;
                try
                {
                    LiveSmokeTestSupport.Diag(
                        "Handshake RunAsync entry target=" +
                        LiveAuthKeyCache.TestDcHost + ":" +
                        LiveAuthKeyCache.TestDcPort);

                    key = await LiveAuthKeyCache.GetOrCreateAsync(
                        stopwatch,
                        cts.Token,
                        "Handshake").ConfigureAwait(false);

                    int keyLen = key.AuthKeyBytes == null ? 0 : key.AuthKeyBytes.Length;
                    if (keyLen != 256)
                    {
                        entry.Passed = false;
                        entry.Detail = "AuthKey length wrong: expected 256, got " +
                            keyLen.ToString(CultureInfo.InvariantCulture);
                        return entry;
                    }

                    if (key.AuthKeyId == 0)
                    {
                        entry.Passed = false;
                        entry.Detail = "AuthKeyId is zero (degenerate SHA1 prefix).";
                        return entry;
                    }

                    long offsetAbs = Math.Abs(key.ServerTimeOffset);
                    if (offsetAbs > MaxServerTimeOffsetSeconds)
                    {
                        entry.Passed = false;
                        entry.Detail = "ServerTimeOffset unreasonable: " +
                            key.ServerTimeOffset.ToString(CultureInfo.InvariantCulture) +
                            "s (limit +/-" +
                            MaxServerTimeOffsetSeconds.ToString(CultureInfo.InvariantCulture) +
                            "s).";
                        return entry;
                    }

                    entry.Passed = true;
                    entry.Detail = "auth_key_id=0x" +
                        key.AuthKeyId.ToString("x16", CultureInfo.InvariantCulture) +
                        ", time_offset=" +
                        key.ServerTimeOffset.ToString(CultureInfo.InvariantCulture) +
                        "s, source=" + (key.Source ?? "unknown");
                    LiveSmokeTestSupport.Diag("Handshake success " + entry.Detail);
                    return entry;
                }
                catch (TimeoutException ex)
                {
                    entry.Passed = false;
                    entry.Detail = ex.Message;
                    LiveSmokeTestSupport.Diag("Handshake timeout: " + ex.Message);
                    return entry;
                }
                catch (OperationCanceledException)
                {
                    entry.Passed = false;
                    entry.Detail = ct.IsCancellationRequested
                        ? "Cancelled by caller."
                        : "Timed out after " +
                          ((long)HandshakeTimeout.TotalSeconds)
                              .ToString(CultureInfo.InvariantCulture) + "s.";
                    LiveSmokeTestSupport.Diag("Handshake canceled: " + entry.Detail);
                    return entry;
                }
                catch (Exception ex)
                {
                    entry.Passed = false;
                    entry.Detail = "Exception: " + ex.GetType().Name + ": " + ex.Message;
                    LiveSmokeTestSupport.Diag("Handshake exception: " +
                        ex.GetType().Name + ": " + ex.Message);
                    return entry;
                }
                finally
                {
                    if (key != null)
                        key.Clear();
                    stopwatch.Stop();
                    entry.Elapsed = stopwatch.Elapsed;
                }
            }
        }
    }
}
