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
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Kernel.Logging;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Ports.Outbound;
using Windows.Networking;
using Windows.Networking.Sockets;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
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
                    config.Mode == ProxySecretMode.Legacy
                        ? Vianium.MtProxy.Api.V1.ProtocolMarker.Intermediate
                        : Vianium.MtProxy.Api.V1.ProtocolMarker.PaddedIntermediate,
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

                if (config.Mode == ProxySecretMode.FakeTls)
                {
                    return await ProbeFakeTlsAsync(
                        socket,
                        config,
                        hs.InitPacket,
                        sw,
                        ct).ConfigureAwait(false);
                }
                else
                {
                    string writeError = await WriteBytesAsync(
                        socket.OutputStream,
                        hs.InitPacket,
                        ConnectTimeoutMs,
                        ct).ConfigureAwait(false);
                    if (writeError != null)
                    {
                        sw.Stop();
                        return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, writeError);
                    }
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

        private async Task<ProxyProbeResult> ProbeFakeTlsAsync(
            StreamSocket socket,
            ProxyConfig config,
            byte[] initPacket,
            Stopwatch sw,
            CancellationToken ct)
        {
            string error;
            byte[] hello;
            byte[] helloRandom;
            if (!BuildFakeTlsClientHello(config, out hello, out helloRandom, out error))
            {
                sw.Stop();
                return ProxyProbeResult.Misconfigured(error ?? "FakeTls hello build failed");
            }

            error = await WriteBytesAsync(socket.OutputStream, hello, ConnectTimeoutMs, ct).ConfigureAwait(false);
            if (error != null)
            {
                sw.Stop();
                return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, error);
            }

            try
            {
                using (DataReader reader = new DataReader(socket.InputStream))
                {
                    reader.InputStreamOptions = InputStreamOptions.Partial;
                    bool verified = await ReadAndVerifyFakeTlsServerHelloAsync(
                        reader,
                        config.Secret,
                        helloRandom,
                        ct).ConfigureAwait(false);
                    try { reader.DetachStream(); } catch { }
                    if (!verified)
                    {
                        sw.Stop();
                        return ProxyProbeResult.Rejected(sw.ElapsedMilliseconds, "FakeTls server hello rejected");
                    }
                }
            }
            catch (TimeoutException)
            {
                sw.Stop();
                return ProxyProbeResult.Timeout(sw.ElapsedMilliseconds);
            }
            catch (EndOfStreamException)
            {
                sw.Stop();
                return ProxyProbeResult.Rejected(sw.ElapsedMilliseconds, "proxy closed during FakeTls hello");
            }
            catch (Exception ex)
            {
                sw.Stop();
                return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, "FakeTls read failed: " + ex.GetType().Name);
            }

            byte[] wrappedInit = WrapFakeTlsApplicationData(initPacket, true);
            error = await WriteBytesAsync(socket.OutputStream, wrappedInit, ConnectTimeoutMs, ct).ConfigureAwait(false);
            sw.Stop();
            if (error != null)
            {
                return ProxyProbeResult.NetworkError(sw.ElapsedMilliseconds, error);
            }

            return ProxyProbeResult.Ok(sw.ElapsedMilliseconds, "FakeTls hello verified");
        }

        private static async Task<string> WriteBytesAsync(
            IOutputStream output,
            byte[] bytes,
            int timeoutMs,
            CancellationToken ct)
        {
            IBuffer buf = CryptographicBuffer.CreateFromByteArray(bytes);
            Task<uint> write = output.WriteAsync(buf).AsTask(ct);
            Task writeTimeout = Task.Delay(timeoutMs, ct);
            Task writeWinner = await Task.WhenAny(write, writeTimeout).ConfigureAwait(false);
            if (writeWinner != write)
            {
                return "write timeout";
            }
            if (write.IsFaulted)
            {
                string ex = write.Exception != null ? write.Exception.GetBaseException().GetType().Name : "Unknown";
                return "write failed: " + ex;
            }
            await write.ConfigureAwait(false);
            return null;
        }

        private static bool BuildFakeTlsClientHello(
            ProxyConfig config,
            out byte[] hello,
            out byte[] helloRandom,
            out string error)
        {
            hello = null;
            helloRandom = null;
            error = null;

            string domain = config.FakeTlsDomain ?? string.Empty;
            if (domain.Length == 0 || domain.Length > 182)
            {
                error = "FakeTls SNI length invalid";
                return false;
            }
            for (int i = 0; i < domain.Length; i++)
            {
                if (domain[i] <= 0 || domain[i] > 0x7F)
                {
                    error = "FakeTls SNI must be ASCII";
                    return false;
                }
            }

            var bytes = new List<byte>(320);
            bytes.Add(0x16);
            bytes.Add(0x03);
            bytes.Add(0x01);
            int recordLenPos = bytes.Count;
            AppendBe16(bytes, 0);

            int handshakeStart = bytes.Count;
            bytes.Add(0x01);
            int handshakeLenPos = bytes.Count;
            AppendBe24(bytes, 0);
            bytes.Add(0x03);
            bytes.Add(0x03);

            int randomPos = bytes.Count;
            for (int i = 0; i < 32; i++) bytes.Add(0);

            bytes.Add(0x20);
            byte[] session = GenerateRandomBytes(32);
            bytes.AddRange(session);

            byte[] ciphers = new byte[]
            {
                0x13,0x01, 0x13,0x02, 0x13,0x03,
                0xC0,0x2B, 0xC0,0x2F, 0xC0,0x2C, 0xC0,0x30,
                0xCC,0xA9, 0xCC,0xA8,
                0x00,0x9C, 0x00,0x9D, 0x00,0x2F, 0x00,0x35
            };
            AppendBe16(bytes, ciphers.Length);
            bytes.AddRange(ciphers);

            bytes.Add(0x01);
            bytes.Add(0x00);

            int extensionsLenPos = bytes.Count;
            AppendBe16(bytes, 0);
            int extensionsStart = bytes.Count;

            AppendBe16(bytes, 0x0000);
            AppendBe16(bytes, 5 + domain.Length);
            AppendBe16(bytes, 3 + domain.Length);
            bytes.Add(0x00);
            AppendBe16(bytes, domain.Length);
            for (int i = 0; i < domain.Length; i++) bytes.Add((byte)domain[i]);

            byte[] supportedGroups = new byte[] { 0x00,0x1D, 0x00,0x17, 0x00,0x18, 0x00,0x19 };
            AppendBe16(bytes, 0x000A);
            AppendBe16(bytes, 2 + supportedGroups.Length);
            AppendBe16(bytes, supportedGroups.Length);
            bytes.AddRange(supportedGroups);

            byte[] pointFormats = new byte[] { 0x01, 0x00 };
            AppendBe16(bytes, 0x000B);
            AppendBe16(bytes, pointFormats.Length);
            bytes.AddRange(pointFormats);

            byte[] sigAlgs = new byte[]
            {
                0x04,0x03, 0x08,0x04, 0x04,0x01, 0x05,0x03,
                0x08,0x05, 0x05,0x01, 0x08,0x06, 0x06,0x01
            };
            AppendBe16(bytes, 0x000D);
            AppendBe16(bytes, 2 + sigAlgs.Length);
            AppendBe16(bytes, sigAlgs.Length);
            bytes.AddRange(sigAlgs);

            byte[] versions = new byte[] { 0x04, 0x03,0x04, 0x03,0x03 };
            AppendBe16(bytes, 0x002B);
            AppendBe16(bytes, versions.Length);
            bytes.AddRange(versions);

            byte[] alpn = new byte[]
            {
                0x00,0x0C, 0x02,(byte)'h',(byte)'2',
                0x08,(byte)'h',(byte)'t',(byte)'t',(byte)'p',(byte)'/',(byte)'1',(byte)'.',(byte)'1'
            };
            AppendBe16(bytes, 0x0010);
            AppendBe16(bytes, alpn.Length);
            bytes.AddRange(alpn);

            int extensionsLen = bytes.Count - extensionsStart;
            int handshakeLen = bytes.Count - handshakeStart - 4;
            int recordLen = bytes.Count - 5;
            PatchBe16(bytes, extensionsLenPos, extensionsLen);
            PatchBe24(bytes, handshakeLenPos, handshakeLen);
            PatchBe16(bytes, recordLenPos, recordLen);

            byte[] raw = bytes.ToArray();
            byte[] mac = HmacSha256(config.Secret, raw);
            System.Buffer.BlockCopy(mac, 0, raw, randomPos, 32);

            uint now = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            uint tail = ReadLe32(raw, randomPos + 28);
            StoreLe32(raw, randomPos + 28, tail ^ now);

            hello = raw;
            helloRandom = new byte[32];
            System.Buffer.BlockCopy(raw, randomPos, helloRandom, 0, 32);
            return true;
        }

        private static async Task<bool> ReadAndVerifyFakeTlsServerHelloAsync(
            DataReader reader,
            byte[] secret,
            byte[] helloRandom,
            CancellationToken ct)
        {
            var response = new List<byte>(1024);

            byte[] header = await ReadExactAsync(reader, 5, ReadTimeoutMs, ct).ConfigureAwait(false);
            if (header[0] != 0x16 || header[1] != 0x03 || header[2] != 0x03)
                return false;
            response.AddRange(header);

            int len1 = (header[3] << 8) | header[4];
            if (len1 <= 0 || len1 > 16384) return false;
            response.AddRange(await ReadExactAsync(reader, (uint)len1, ReadTimeoutMs, ct).ConfigureAwait(false));

            byte[] prefix2 = await ReadExactAsync(reader, 9, ReadTimeoutMs, ct).ConfigureAwait(false);
            byte[] expectedPrefix2 = new byte[] { 0x14,0x03,0x03,0x00,0x01,0x01,0x17,0x03,0x03 };
            if (!BytesEqual(prefix2, expectedPrefix2)) return false;
            response.AddRange(prefix2);

            byte[] len2buf = await ReadExactAsync(reader, 2, ReadTimeoutMs, ct).ConfigureAwait(false);
            response.AddRange(len2buf);
            int len2 = (len2buf[0] << 8) | len2buf[1];
            if (len2 <= 0 || len2 > 16384) return false;
            response.AddRange(await ReadExactAsync(reader, (uint)len2, ReadTimeoutMs, ct).ConfigureAwait(false));

            if (response.Count < 43) return false;
            byte[] responseBytes = response.ToArray();
            byte[] serverRandom = new byte[32];
            System.Buffer.BlockCopy(responseBytes, 11, serverRandom, 0, 32);
            Array.Clear(responseBytes, 11, 32);

            byte[] macInput = new byte[helloRandom.Length + responseBytes.Length];
            System.Buffer.BlockCopy(helloRandom, 0, macInput, 0, helloRandom.Length);
            System.Buffer.BlockCopy(responseBytes, 0, macInput, helloRandom.Length, responseBytes.Length);

            byte[] expected = HmacSha256(secret, macInput);
            return BytesEqual(serverRandom, expected);
        }

        private static async Task<byte[]> ReadExactAsync(
            DataReader reader,
            uint count,
            int timeoutMs,
            CancellationToken ct)
        {
            byte[] output = new byte[(int)count];
            uint offset = 0;
            while (offset < count)
            {
                Task<uint> load = reader.LoadAsync(count - offset).AsTask(ct);
                Task timeout = Task.Delay(timeoutMs, ct);
                Task winner = await Task.WhenAny(load, timeout).ConfigureAwait(false);
                if (winner != load) throw new TimeoutException();
                if (load.IsFaulted)
                {
                    throw load.Exception != null ? load.Exception.GetBaseException() : new IOException("read failed");
                }
                uint loaded = await load.ConfigureAwait(false);
                if (loaded == 0) throw new EndOfStreamException();
                byte[] chunk = new byte[loaded];
                reader.ReadBytes(chunk);
                System.Buffer.BlockCopy(chunk, 0, output, (int)offset, (int)loaded);
                offset += loaded;
            }
            return output;
        }

        private static byte[] WrapFakeTlsApplicationData(byte[] payload, bool firstPacket)
        {
            int extra = firstPacket ? 6 : 0;
            byte[] wire = new byte[extra + 5 + payload.Length];
            int offset = 0;
            if (firstPacket)
            {
                wire[offset++] = 0x14;
                wire[offset++] = 0x03;
                wire[offset++] = 0x03;
                wire[offset++] = 0x00;
                wire[offset++] = 0x01;
                wire[offset++] = 0x01;
            }
            wire[offset++] = 0x17;
            wire[offset++] = 0x03;
            wire[offset++] = 0x03;
            wire[offset++] = (byte)((payload.Length >> 8) & 0xFF);
            wire[offset++] = (byte)(payload.Length & 0xFF);
            System.Buffer.BlockCopy(payload, 0, wire, offset, payload.Length);
            return wire;
        }

        private static byte[] GenerateRandomBytes(uint length)
        {
            IBuffer buf = CryptographicBuffer.GenerateRandom(length);
            byte[] bytes;
            CryptographicBuffer.CopyToByteArray(buf, out bytes);
            return bytes;
        }

        private static byte[] HmacSha256(byte[] key, byte[] data)
        {
            MacAlgorithmProvider provider = MacAlgorithmProvider.OpenAlgorithm(MacAlgorithmNames.HmacSha256);
            CryptographicKey hmacKey = provider.CreateKey(CryptographicBuffer.CreateFromByteArray(key));
            IBuffer mac = CryptographicEngine.Sign(hmacKey, CryptographicBuffer.CreateFromByteArray(data));
            byte[] bytes;
            CryptographicBuffer.CopyToByteArray(mac, out bytes);
            return bytes;
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static void AppendBe16(List<byte> bytes, int value)
        {
            bytes.Add((byte)((value >> 8) & 0xFF));
            bytes.Add((byte)(value & 0xFF));
        }

        private static void AppendBe24(List<byte> bytes, int value)
        {
            bytes.Add((byte)((value >> 16) & 0xFF));
            bytes.Add((byte)((value >> 8) & 0xFF));
            bytes.Add((byte)(value & 0xFF));
        }

        private static void PatchBe16(List<byte> bytes, int offset, int value)
        {
            bytes[offset] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 1] = (byte)(value & 0xFF);
        }

        private static void PatchBe24(List<byte> bytes, int offset, int value)
        {
            bytes[offset] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)(value & 0xFF);
        }

        private static uint ReadLe32(byte[] bytes, int offset)
        {
            return (uint)(
                bytes[offset] |
                (bytes[offset + 1] << 8) |
                (bytes[offset + 2] << 16) |
                (bytes[offset + 3] << 24));
        }

        private static void StoreLe32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value & 0xFF);
            bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
            bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
            bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
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
