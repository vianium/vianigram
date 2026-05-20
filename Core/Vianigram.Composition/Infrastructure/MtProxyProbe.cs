// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// MtProxyProbe.cs
//
// Live MTProxy obfuscated-handshake probe. Powers the "Test"
// button on the proxy settings page. Performs a full 64-byte init
// exchange against a fresh socket (i.e. NOT through the active
// runtime), so an in-progress MTProto channel is not disturbed.
//
// Pipeline:
//   1. Validate config locally. Misconfigured -> early return.
//   2. Build the 16-byte secret payload + optional SNI back into the
//      hex form that Vianium.MtProxy's MtProxySecret.Parse accepts:
//        - Legacy:  32 hex chars
//        - Secure:  "dd" + 32 hex chars
//        - FakeTls: "ee" + 32 hex chars + ASCII-SNI bytes hex
//   3. Generate 58 random bytes via CryptographicBuffer.GenerateRandom,
//      retrying up to 16 times until HandshakeBuilder.IsValidRandomness
//      passes (forbidden first-word filter).
//   4. HandshakeBuilder.Build(...) gives a 64-byte InitPacket plus the
//      four AES-CTR materials. We only use InitPacket here; the codec
//      isn't needed because we don't read past the first response byte.
//   5. Open a fresh StreamSocket to proxy_host:proxy_port (5-second
//      connect timeout).
//   6. Write the 64-byte init packet.
//   7. LoadAsync up to 16 bytes with a 5-second read timeout.
//        - bytes received -> Reachable (proxy is alive and responsive)
//        - 0 bytes loaded -> Rejected (server closed the socket — likely
//          a wrong secret on a strict proxy, or the proxy refuses
//          non-MTProto traffic)
//        - timeout         -> Timeout
//        - throw           -> NetworkError
//
// Note: even Reachable is NOT a proof that the secret is correct —
// some proxies happily echo bytes back regardless of the handshake.
// True correctness is only confirmed by Telegram's DH handshake on
// the next channel open via the new descriptor.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class MtProxyProbe : IProxyProbe
    {
        private const int ConnectTimeoutMs = 5000;
        private const int ReadTimeoutMs    = 5000;
        private const int ProbeReadBytes   = 16;
        // Default DC for the probe — the actual DC routing only matters
        // for the proxy's upstream selection, which won't affect whether
        // the proxy ACKs the handshake. DC#2 (Telegram's home DC) is
        // accepted by every public MTProxy server.
        private const int ProbeDcId = 2;

        private readonly IComponentLogger _log;

        public MtProxyProbe(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException("logger");
            _log = new TimestampedLogger(logger, "MtProxy.Probe");
        }

        public async Task<ProxyProbeResult> ProbeAsync(ProxyConfig config, CancellationToken ct)
        {
            if (config == null)
            {
                return ProxyProbeResult.Misconfigured("config required");
            }
            if (!config.Enabled)
            {
                // Allow probing a disabled-but-staged descriptor by
                // promoting it to enabled for the validation path. The
                // probe never touches the runtime, so this is safe.
                config = config.WithEnabled(true);
            }
            if (string.IsNullOrEmpty(config.Host) || config.Port <= 0 || config.Port > 65535)
            {
                return ProxyProbeResult.Misconfigured("invalid host/port");
            }
            if (config.Secret == null || config.Secret.Length != 16)
            {
                return ProxyProbeResult.Misconfigured("secret must be 16 bytes");
            }
            if (config.Mode == ProxySecretMode.FakeTls && string.IsNullOrEmpty(config.FakeTlsDomain))
            {
                return ProxyProbeResult.Misconfigured("FakeTls requires SNI");
            }

            // 1. Construct the hex form the Vianium.MtProxy WinRT
            //    parser accepts.
            string hex = EncodeForMtProxyParser(config.Mode, config.Secret, config.FakeTlsDomain);
            Vianium.MtProxy.Api.V1.MtProxySecret secret;
            try
            {
                secret = Vianium.MtProxy.Api.V1.MtProxySecret.TryParse(hex);
            }
            catch (Exception ex)
            {
                _log.Warn("MtProxySecret.TryParse threw: " + ex.Message);
                return ProxyProbeResult.Misconfigured("secret parse error");
            }
            if (secret == null)
            {
                return ProxyProbeResult.Misconfigured("secret unrecognised by Vianium.MtProxy");
            }

            // 2. Build the 64-byte init packet. Re-draw randomness up to
            //    16 times — the forbidden-first-word filter is rare to
            //    hit but the spec requires we handle it.
            Vianium.MtProxy.Api.V1.HandshakeOutput hs = null;
            for (int i = 0; i < 16 && hs == null; i++)
            {
                var randomness = CryptographicBuffer.GenerateRandom(58);
                byte[] randomnessBytes;
                CryptographicBuffer.CopyToByteArray(randomness, out randomnessBytes);
                if (!Vianium.MtProxy.Api.V1.HandshakeBuilder.IsValidRandomness(randomnessBytes))
                {
                    continue;
                }
                hs = Vianium.MtProxy.Api.V1.HandshakeBuilder.Build(
                    secret,
                    Vianium.MtProxy.Api.V1.ProtocolMarker.Intermediate,
                    ProbeDcId,
                    randomnessBytes);
            }
            if (hs == null || hs.InitPacket == null || hs.InitPacket.Length != 64)
            {
                return ProxyProbeResult.Misconfigured("handshake build failed");
            }

            // 3. Connect + write + read with a deadline.
            var sw = Stopwatch.StartNew();
            StreamSocket socket = null;
            try
            {
                socket = new StreamSocket();
                HostName host = new HostName(config.Host);

                Task connect = socket.ConnectAsync(host, config.Port.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .AsTask(ct);
                Task connectTimeout = Task.Delay(ConnectTimeoutMs, ct);
                Task connectWinner = await Task.WhenAny(connect, connectTimeout).ConfigureAwait(false);
                if (connectWinner != connect)
                {
                    sw.Stop();
                    return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, "connect timeout");
                }
                if (connect.IsFaulted)
                {
                    sw.Stop();
                    string ex = connect.Exception != null ? connect.Exception.GetBaseException().GetType().Name : "Unknown";
                    return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, "connect failed: " + ex);
                }

                // Write init packet.
                IBuffer initBuf = CryptographicBuffer.CreateFromByteArray(hs.InitPacket);
                Task write = socket.OutputStream.WriteAsync(initBuf).AsTask(ct);
                Task writeTimeout = Task.Delay(ConnectTimeoutMs, ct);
                Task writeWinner = await Task.WhenAny(write, writeTimeout).ConfigureAwait(false);
                if (writeWinner != write)
                {
                    sw.Stop();
                    return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, "write timeout");
                }
                if (write.IsFaulted)
                {
                    sw.Stop();
                    string ex = write.Exception != null ? write.Exception.GetBaseException().GetType().Name : "Unknown";
                    return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, "write failed: " + ex);
                }

                // Read first response bytes with deadline.
                DataReader reader = new DataReader(socket.InputStream);
                reader.InputStreamOptions = InputStreamOptions.Partial;
                Task<uint> read = reader.LoadAsync(ProbeReadBytes).AsTask(ct);
                Task readTimeout = Task.Delay(ReadTimeoutMs, ct);
                Task readWinner = await Task.WhenAny(read, readTimeout).ConfigureAwait(false);
                if (readWinner != read)
                {
                    sw.Stop();
                    try { reader.DetachStream(); } catch { }
                    return ProxyProbeResult.Timeout(sw.ElapsedMilliseconds);
                }
                if (read.IsFaulted)
                {
                    sw.Stop();
                    try { reader.DetachStream(); } catch { }
                    string ex = read.Exception != null ? read.Exception.GetBaseException().GetType().Name : "Unknown";
                    return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, "read failed: " + ex);
                }

                uint loaded = await read.ConfigureAwait(false);
                sw.Stop();
                try { reader.DetachStream(); } catch { }

                if (loaded == 0)
                {
                    return ProxyProbeResult.Rejected(sw.ElapsedMilliseconds, "proxy closed without response");
                }
                return ProxyProbeResult.Ok(sw.ElapsedMilliseconds, loaded + " bytes received");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _log.Warn("Probe threw: " + ex.GetType().Name + ": " + ex.Message);
                return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, ex.GetType().Name);
            }
            finally
            {
                if (socket != null)
                {
                    try { socket.Dispose(); } catch { }
                }
            }
        }

        // ---------------------------------------------------------------
        // EncodeForMtProxyParser
        //
        // Vianium.MtProxy.MtProxySecret.TryParse accepts hex with the
        // canonical mode prefix:
        //   Legacy  -> 32 hex chars (just the secret bytes)
        //   Secure  -> "dd" + 32 hex chars
        //   FakeTls -> "ee" + 32 hex chars + ASCII SNI byte hex
        // (it also accepts url-safe base64, but emitting hex makes the
        // round-trip deterministic).
        // ---------------------------------------------------------------
        private static string EncodeForMtProxyParser(ProxySecretMode mode, byte[] secret, string sni)
        {
            var sb = new System.Text.StringBuilder(64);
            if (mode == ProxySecretMode.Secure) sb.Append("dd");
            else if (mode == ProxySecretMode.FakeTls) sb.Append("ee");

            for (int i = 0; i < secret.Length; i++)
            {
                AppendHex(sb, secret[i]);
            }
            if (mode == ProxySecretMode.FakeTls && !string.IsNullOrEmpty(sni))
            {
                for (int i = 0; i < sni.Length; i++)
                {
                    char c = sni[i];
                    if (c > 0x7F) continue;   // SNI is ASCII; skip out-of-range chars
                    AppendHex(sb, (byte)c);
                }
            }
            return sb.ToString();
        }

        private static void AppendHex(System.Text.StringBuilder sb, byte b)
        {
            sb.Append(HexNibble((b >> 4) & 0x0F));
            sb.Append(HexNibble(b        & 0x0F));
        }

        private static char HexNibble(int n)
        {
            return n < 10 ? (char)('0' + n) : (char)('a' + (n - 10));
        }
    }
}
