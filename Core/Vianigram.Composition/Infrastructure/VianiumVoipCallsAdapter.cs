// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vianigram.Calls.Domain;
using Vianigram.Calls.Domain.ValueObjects;
using Vianigram.Calls.Ports.Outbound;
using Vianigram.Kernel.Logging;
using Vianigram.Kernel.Result;
using NativeVoip = Vianium.VoIP;

namespace Vianigram.Composition.Infrastructure
{
    /// <summary>
    /// Anti-corruption adapter between the managed Calls signaling context
    /// and the native VianiumVoIP runtime. Calls sees only its own
    /// outbound ports; the native WinMD projection stays inside Composition.
    /// </summary>
    public sealed class VianiumVoipCallsAdapter :
        ICallCryptoVault,
        ICallCryptoCapabilityPort,
        IVoipMediaPort,
        IVoipMediaCapabilityPort,
        IVoipPlaybackSourcePort
    {
        private const string DefaultReason =
            "VianiumVoIP media plane cannot start calls in this build (Opus capture/playback and AEC are not enabled)";
        private static readonly TimeSpan MediaActivationTimeout = TimeSpan.FromSeconds(12);
        private static readonly TimeSpan MediaActivationPollInterval = TimeSpan.FromMilliseconds(250);

        private readonly NativeVoip.VoipRuntime _runtime;
        private readonly IMtProtoRpcPort _rpc;
        private readonly IComponentLogger _log;
        private readonly SemaphoreSlim _dhLock;
        private readonly Dictionary<long, bool> _callDirection;
        private readonly object _callDirectionLock;
        private TelegramDhConfig _dhConfig;
        private int _dhVersion;

        public event EventHandler<CallMediaEventArgs> MediaEvent;
        public event EventHandler<CallSignalingDataProducedEventArgs> SignalingDataProduced;
        public event EventHandler<CallMediaStateChangedEventArgs> MediaStateChanged;

        public VianiumVoipCallsAdapter(NativeVoip.VoipRuntime runtime, IMtProtoRpcPort rpc, ILogger logger)
        {
            if (runtime == null) throw new ArgumentNullException("runtime");
            if (rpc == null) throw new ArgumentNullException("rpc");
            if (logger == null) throw new ArgumentNullException("logger");
            _runtime = runtime;
            _rpc = rpc;
            _log = new TimestampedLogger(logger, "Composition.VoipCallsAdapter");
            _dhLock = new SemaphoreSlim(1, 1);
            _callDirection = new Dictionary<long, bool>();
            _callDirectionLock = new object();
            _runtime.SignalingDataProduced += OnNativeSignalingDataProduced;

            string reason = UnavailableReason;
            if (!string.IsNullOrEmpty(reason))
                _log.Warn("VoIP calls disabled: " + reason);
        }

        public bool CanExchangeCallKeys
        {
            get
            {
                NativeVoip.VoipCapabilityResult c = ReadCapability();
                return c != null && c.CanExchangeCallKeys;
            }
        }

        public bool CanStartCalls
        {
            get
            {
                NativeVoip.VoipCapabilityResult c = ReadCapability();
                return c != null && c.CanStartMedia;
            }
        }

        public string UnavailableReason
        {
            get
            {
                NativeVoip.VoipCapabilityResult c = ReadCapability();
                if (c == null) return DefaultReason;
                if (c.CanExchangeCallKeys && c.CanStartMedia) return string.Empty;
                return string.IsNullOrEmpty(c.Reason) ? DefaultReason : c.Reason;
            }
        }

        public async Task<Result<byte[], CallError>> CreateOutgoingGAHashAsync(int randomId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TelegramDhConfig dh = await GetDhConfigAsync(ct).ConfigureAwait(false);
            if (dh == null)
                return Result<byte[], CallError>.Fail(CallError.ProtocolError("messages.getDhConfig unavailable"));

            NativeVoip.VoipDhMaterialResult native = await _runtime
                .CreateOutgoingDhAsync(randomId, dh.G, dh.P)
                .AsTask(ct)
                .ConfigureAwait(false);
            if (native == null || !native.Success)
                return Result<byte[], CallError>.Fail(ProjectCryptoError(native, "native outgoing call DH failed"));

            return Result<byte[], CallError>.Ok(native.PublicHash ?? new byte[0]);
        }

        public Result<Unit, CallError> BindOutgoingCall(int randomId, CallId callId)
        {
            NativeVoip.VoipOperationResult native = _runtime.BindOutgoingCall(randomId, callId.Value);
            if (native == null || !native.Success)
                return Result<Unit, CallError>.Fail(ProjectCryptoError(native, "native outgoing call bind failed"));
            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        public Result<Unit, CallError> RegisterIncomingGAHash(CallId callId, byte[] gAHash)
        {
            NativeVoip.VoipOperationResult native = _runtime.RegisterIncomingGAHash(callId.Value, gAHash ?? new byte[0]);
            if (native == null || !native.Success)
                return Result<Unit, CallError>.Fail(ProjectCryptoError(native, "native incoming g_a_hash registration failed"));
            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        public async Task<Result<byte[], CallError>> CreateIncomingGBAsync(CallId callId, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            TelegramDhConfig dh = await GetDhConfigAsync(ct).ConfigureAwait(false);
            if (dh == null)
                return Result<byte[], CallError>.Fail(CallError.ProtocolError("messages.getDhConfig unavailable"));

            NativeVoip.VoipDhMaterialResult native = await _runtime
                .CreateIncomingDhAsync(callId.Value, dh.G, dh.P)
                .AsTask(ct)
                .ConfigureAwait(false);
            if (native == null || !native.Success)
                return Result<byte[], CallError>.Fail(ProjectCryptoError(native, "native incoming call DH failed"));

            return Result<byte[], CallError>.Ok(native.PublicValue ?? new byte[0]);
        }

        public async Task<Result<ConfirmCallMaterial, CallError>> AcceptPeerGBAsync(CallId callId, byte[] gB, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NativeVoip.VoipDhMaterialResult native = await _runtime
                .AcceptPeerGBAsync(callId.Value, gB ?? new byte[0])
                .AsTask(ct)
                .ConfigureAwait(false);
            if (native == null || !native.Success)
                return Result<ConfirmCallMaterial, CallError>.Fail(ProjectCryptoError(native, "native peer g_b acceptance failed"));

            return Result<ConfirmCallMaterial, CallError>.Ok(
                new ConfirmCallMaterial(native.PublicValue ?? new byte[0], native.KeyFingerprint));
        }

        public async Task<Result<Unit, CallError>> ConfirmPeerGAOrBAsync(CallId callId, byte[] gAOrB, long expectedFingerprint, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NativeVoip.VoipOperationResult native = await _runtime
                .ConfirmPeerGAOrBAsync(callId.Value, gAOrB ?? new byte[0], expectedFingerprint)
                .AsTask(ct)
                .ConfigureAwait(false);
            if (native == null || !native.Success)
                return Result<Unit, CallError>.Fail(ProjectCryptoError(native, "native peer g_a/g_b confirmation failed"));
            return Result<Unit, CallError>.Ok(Unit.Value);
        }

        public long GetLocalFingerprint(CallId callId)
        {
            return _runtime.GetLocalFingerprint(callId.Value);
        }

        public CallKeyHandle GetSharedKeyHandle(CallId callId)
        {
            string handle = _runtime.GetKeyHandle(callId.Value);
            return string.IsNullOrEmpty(handle) ? CallKeyHandle.Empty : new CallKeyHandle(handle);
        }

        public void Drop(CallId callId)
        {
            _runtime.DropCall(callId.Value);
            lock (_callDirectionLock)
            {
                _callDirection.Remove(callId.Value);
            }
        }

        public async Task<Result<Unit, CallError>> StartAsync(CallId id, CallKeyHandle keyHandle, IList<CallEndpoint> endpoints, CancellationToken ct)
        {
            return await StartAsync(new CallStartContext(
                id,
                0L,
                false,
                false,
                CallProtocol.Default,
                0L,
                keyHandle,
                endpoints,
                string.Empty), ct).ConfigureAwait(false);
        }

        public async Task<Result<Unit, CallError>> StartAsync(CallStartContext context, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (context == null)
                return Result<Unit, CallError>.Fail(CallError.MediaPlaneFailed("missing VoIP call start context"));
            if (!CanStartCalls)
                return Result<Unit, CallError>.Fail(CallError.MediaPlaneFailed(UnavailableReason));

            // Remember per-call direction so ReceiveSignalingDataAsync can run
            // the tgcalls 2.x signaling diagnostic decrypt (needs is_outgoing).
            lock (_callDirectionLock)
            {
                _callDirection[context.Id.Value] = context.IsInitiator;
            }

            string handle = context.KeyHandle == null ? string.Empty : context.KeyHandle.Value;
            IList<NativeVoip.VoipEndpointInfo> projected = ProjectEndpoints(context.Endpoints);
            NativeVoip.VoipCallStartDescriptor descriptor = ProjectStartDescriptor(context, handle, projected);
            RaiseMediaState(context.Id, CallMediaStateKind.Connecting, "native media start requested");
            _log.Info("StartMediaAsync begin id=" + context.Id
                + " direction=" + (context.IsInitiator ? "outgoing" : "incoming")
                + " video=" + context.IsVideo
                + " protocol=" + context.Protocol
                + " callConfig=" + (string.IsNullOrEmpty(context.CallConfigJson) ? "empty" : (context.CallConfigJson.Length + "B"))
                + " endpoints=" + DescribeEndpoints(context.Endpoints)
                + " keyHandle=" + (string.IsNullOrEmpty(handle) ? "empty" : "present"));
            if (HasWebRtcEndpoints(context.Endpoints) && IsClassicOnly(context.Protocol))
            {
                _log.Info("StartMediaAsync WebRTC endpoints present; local protocol is classic-only, trying reflector compatibility path");
            }
            NativeVoip.VoipOperationResult native = await _runtime
                .StartCallAsync(descriptor)
                .AsTask(ct)
                .ConfigureAwait(false);
            if (native == null || !native.Success)
            {
                RaiseMediaState(context.Id, CallMediaStateKind.Failed, native == null ? "native failure" : native.ErrorMessage);
                _log.Warn("StartMediaAsync native failed id=" + context.Id
                    + " code=" + (native == null ? 0 : native.ErrorCode)
                    + " message=" + (native == null ? string.Empty : native.ErrorMessage));
            }
            else
            {
                NativeVoip.VoipMediaStatsResult active =
                    await WaitForNativeMediaActiveAsync(context.Id, ct).ConfigureAwait(false);
                if (active == null)
                {
                    NativeVoip.VoipMediaStatsResult last = SafeReadStats();
                    string detail = "native media did not receive remote audio within "
                        + ((int)MediaActivationTimeout.TotalSeconds)
                        + "s; " + DescribeNativeStats(last);
                    RaiseMediaState(context.Id, CallMediaStateKind.Failed, detail);
                    _log.Warn("StartMediaAsync media activation timeout id=" + context.Id + " " + detail);
                    return Result<Unit, CallError>.Fail(CallError.MediaPlaneFailed(detail));
                }

                string connectedDetail = "native media connected; " + DescribeNativeStats(active);
                RaiseMediaState(context.Id, CallMediaStateKind.Connected, connectedDetail);
                RaiseMediaEvent(MediaEventKind.Connected, connectedDetail);
            }
            return ProjectMediaResult(native, "native VoIP start failed");
        }

        public async Task<Result<Unit, CallError>> ReceiveSignalingDataAsync(CallId id, byte[] data, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (data == null || data.Length == 0)
                return Result<Unit, CallError>.Fail(CallError.MediaPlaneFailed("empty VoIP signaling payload"));

            _log.Info("ReceiveSignalingDataAsync begin id=" + id + " " + DescribeSignalingPayload(data));
            TryLogTgcallsSignalingDiagnostic(id, data);
            NativeVoip.VoipOperationResult native = await _runtime
                .ReceiveSignalingDataAsync(id.Value, data)
                .AsTask(ct)
                .ConfigureAwait(false);
            if (native == null || !native.Success)
            {
                _log.Warn("ReceiveSignalingDataAsync native failed id=" + id
                    + " code=" + (native == null ? 0 : native.ErrorCode)
                    + " message=" + (native == null ? string.Empty : native.ErrorMessage));
            }
            return ProjectMediaResult(native, "native VoIP signaling receive failed");
        }

        public async Task<Result<Unit, CallError>> StopAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NativeVoip.VoipOperationResult native = await _runtime.StopMediaAsync();
            return ProjectMediaResult(native, "native VoIP stop failed");
        }

        public async Task<Result<Unit, CallError>> MuteAsync(bool mute, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NativeVoip.VoipOperationResult native = await _runtime.SetMutedAsync(mute);
            return ProjectMediaResult(native, "native VoIP mute failed");
        }

        public Task<Result<Unit, CallError>> SetMutedAsync(CallId id, bool muted, CancellationToken ct)
        {
            return MuteAsync(muted, ct);
        }

        public async Task<Result<Unit, CallError>> SetSpeakerAsync(CallId id, bool on, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            NativeVoip.VoipOperationResult native = await _runtime.SetSpeakerAsync(on).AsTask(ct).ConfigureAwait(false);
            return ProjectMediaResult(native, "native VoIP speaker route failed");
        }

        public Task<Result<Unit, CallError>> FlipCameraAsync(CallId id, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(Result<Unit, CallError>.Fail(
                CallError.MediaPlaneFailed("video capture is unavailable in the current VianiumVoIP runtime")));
        }

        public Task<CallStats> GetStatsAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(ProjectStats(_runtime.GetMediaStats()));
        }

        public object CreatePlaybackSource(CallId id)
        {
            return null;
        }

        private async Task<NativeVoip.VoipMediaStatsResult> WaitForNativeMediaActiveAsync(CallId id, CancellationToken ct)
        {
            DateTime deadline = DateTime.UtcNow.Add(MediaActivationTimeout);
            int sample = 0;
            while (DateTime.UtcNow < deadline)
            {
                ct.ThrowIfCancellationRequested();
                NativeVoip.VoipMediaStatsResult stats = SafeReadStats();
                if (stats != null
                    && string.Equals(stats.State, "active", StringComparison.OrdinalIgnoreCase)
                    && stats.PacketsReceived > 0)
                {
                    _log.Info("StartMediaAsync media active id=" + id + " " + DescribeNativeStats(stats));
                    return stats;
                }

                if ((sample % 4) == 0)
                {
                    _log.Info("StartMediaAsync waiting for media id=" + id + " " + DescribeNativeStats(stats));
                }
                sample++;
                await Task.Delay(MediaActivationPollInterval, ct).ConfigureAwait(false);
            }
            return null;
        }

        private NativeVoip.VoipMediaStatsResult SafeReadStats()
        {
            try { return _runtime.GetMediaStats(); }
            catch (Exception ex)
            {
                _log.Warn("native media stats read failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static string DescribeNativeStats(NativeVoip.VoipMediaStatsResult stats)
        {
            if (stats == null) return "stats=unavailable";
            return "state=" + (stats.State ?? string.Empty)
                + " endpoint=" + (stats.EndpointIp ?? string.Empty) + ":" + stats.EndpointPort
                + " txPackets=" + stats.PacketsSent
                + " rxPackets=" + stats.PacketsReceived
                + " txBytes=" + stats.BytesSent
                + " rxBytes=" + stats.BytesReceived
                + " rtt=" + stats.RttMs + "ms"
                + " underruns=" + stats.Underruns
                + " outLevel=" + stats.OutboundLevel.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " inLevel=" + stats.InboundLevel.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
        }

        private void RaiseMediaEvent(MediaEventKind kind, string detail)
        {
            EventHandler<CallMediaEventArgs> handler = MediaEvent;
            if (handler == null) return;
            handler(this, new CallMediaEventArgs(kind, ProjectStats(_runtime.GetMediaStats()), detail, DateTime.UtcNow));
        }

        private NativeVoip.VoipCapabilityResult ReadCapability()
        {
            try
            {
                return _runtime.GetCapability();
            }
            catch (Exception ex)
            {
                _log.Warn("native VoIP capability probe failed: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private static IList<NativeVoip.VoipEndpointInfo> ProjectEndpoints(IList<CallEndpoint> endpoints)
        {
            var projected = new List<NativeVoip.VoipEndpointInfo>();
            if (endpoints == null) return projected;
            for (int i = 0; i < endpoints.Count; i++)
            {
                CallEndpoint e = endpoints[i];
                projected.Add(new NativeVoip.VoipEndpointInfo
                {
                    Id = e.Id,
                    Ip = e.Ip ?? string.Empty,
                    Ipv6 = e.Ipv6 ?? string.Empty,
                    Port = e.Port,
                    PeerTag = e.PeerTag ?? new byte[0],
                    Kind = e.Kind == CallEndpointKind.WebRtc ? "webrtc" : "reflector",
                    Tcp = e.Tcp,
                    Stun = e.Stun,
                    Turn = e.Turn,
                    Username = e.Username ?? string.Empty,
                    Password = e.Password ?? string.Empty,
                    ReflectorId = e.ReflectorId
                });
            }
            return projected;
        }

        private static NativeVoip.VoipCallStartDescriptor ProjectStartDescriptor(
            CallStartContext context,
            string keyHandle,
            IList<NativeVoip.VoipEndpointInfo> endpoints)
        {
            CallProtocol protocol = context.Protocol;
            return new NativeVoip.VoipCallStartDescriptor
            {
                CallId = context.Id.Value,
                AccessHash = context.AccessHash,
                IsInitiator = context.IsInitiator,
                IsVideo = context.IsVideo,
                UdpP2p = protocol.UdpP2p,
                UdpReflector = protocol.UdpReflector,
                MinLayer = protocol.MinLayer,
                MaxLayer = protocol.MaxLayer,
                LibraryVersions = protocol.LibraryVersions ?? new string[0],
                KeyFingerprint = context.KeyFingerprint,
                KeyHandle = keyHandle ?? string.Empty,
                Endpoints = endpoints,
                CallConfigJson = context.CallConfigJson ?? string.Empty
            };
        }

        private static string DescribeEndpoints(IList<CallEndpoint> endpoints)
        {
            if (endpoints == null || endpoints.Count == 0) return "none";
            var parts = new List<string>(endpoints.Count);
            for (int i = 0; i < endpoints.Count; i++)
            {
                CallEndpoint e = endpoints[i];
                string address = string.IsNullOrEmpty(e.Ip) ? e.Ipv6 : e.Ip;
                byte[] tag = e.PeerTag;
                parts.Add("#" + i
                    + " " + e.Kind
                    + " id=0x" + ((ulong)e.Id).ToString("x16")
                    + " " + (string.IsNullOrEmpty(address) ? "<no-address>" : address)
                    + ":" + e.Port
                    + " tcp=" + e.Tcp
                    + " stun=" + e.Stun
                    + " turn=" + e.Turn
                    + " tag=" + (tag == null ? 0 : tag.Length) + "B");
            }
            return string.Join("; ", parts.ToArray());
        }

        private static bool HasWebRtcEndpoints(IList<CallEndpoint> endpoints)
        {
            if (endpoints == null) return false;
            for (int i = 0; i < endpoints.Count; i++)
            {
                if (endpoints[i] != null && endpoints[i].Kind == CallEndpointKind.WebRtc)
                    return true;
            }
            return false;
        }

        private static bool IsClassicOnly(CallProtocol protocol)
        {
            string[] versions = protocol.LibraryVersions ?? new string[0];
            if (versions.Length == 0) return true;
            for (int i = 0; i < versions.Length; i++)
            {
                string version = versions[i] ?? string.Empty;
                if (!string.Equals(version, "2.4.4", StringComparison.Ordinal))
                    return false;
            }
            return true;
        }

        private void TryLogTgcallsSignalingDiagnostic(CallId id, byte[] dataBytes)
        {
            try
            {
                // Tgcalls 2.x signaling envelope is at minimum 16B msg_key +
                // 16B AES-CTR aligned plaintext = 32B. Packets shorter than
                // that are typically tgcalls ACK / keepalive frames that
                // are NOT encrypted with the signaling key — attempting to
                // decrypt them just produces "payload too short" log spam
                // (we observed dozens of 26B packets per call). Skip the
                // diagnostic for these and let the native side route them.
                if (dataBytes == null || dataBytes.Length < 32)
                    return;

                bool isInitiator;
                lock (_callDirectionLock)
                {
                    if (!_callDirection.TryGetValue(id.Value, out isInitiator))
                        return;
                }

                byte[] sharedKeyBytes = _runtime.GetSharedKeyDiagnosticBytes(id.Value);
                if (sharedKeyBytes == null || sharedKeyBytes.Length != 256)
                    return;

                var msgs = NativeVoip.TgcallsSignalingPipeline.DecryptAndParse(
                    sharedKeyBytes, isInitiator, dataBytes);
                if (msgs == null || msgs.Count == 0)
                {
                    // Fall back to single-record AES diagnostic so we still get
                    // *something* logged when framing is unfamiliar.
                    NativeVoip.TgcallsSignalingDiagnosticResult diag =
                        NativeVoip.TgcallsSignalingDiagnostics.Decrypt(
                            sharedKeyBytes, isInitiator, dataBytes);
                    if (diag != null && diag.Success)
                    {
                        _log.Info("tgcalls signaling decrypted (no frames) id=" + id
                            + " seq=" + diag.Seq
                            + " len=" + diag.PlaintextLength
                            + " hex=" + diag.PlaintextHex);
                    }
                    else if (diag != null)
                    {
                        _log.Info("tgcalls signaling decrypt failed id=" + id
                            + " err=" + (string.IsNullOrEmpty(diag.Error) ? "(unknown)" : diag.Error)
                            + " hex=" + diag.PlaintextHex);
                    }
                    return;
                }

                foreach (var m in msgs)
                {
                    string json = m.JsonContent ?? string.Empty;
                    if (json.Length > 400) json = json.Substring(0, 400) + "...";
                    _log.Info("tgcalls msg id=" + id
                        + " type=" + (string.IsNullOrEmpty(m.TypeName) ? "(unknown)" : m.TypeName)
                        + " content=" + json);

                    if (string.Equals(m.TypeName, "Candidates", StringComparison.Ordinal)
                        && m.CandidateSdpStrings != null)
                    {
                        foreach (var s in m.CandidateSdpStrings)
                        {
                            _log.Info("  ICE candidate: " + s);
                        }
                    }
                    else if (string.Equals(m.TypeName, "InitialSetup", StringComparison.Ordinal))
                    {
                        _log.Info("  DTLS fingerprint hash=" + (m.FingerprintHash ?? string.Empty)
                            + " setup=" + (m.FingerprintSetup ?? string.Empty)
                            + " fp=" + (m.FingerprintHex ?? string.Empty));
                        _log.Info("  ICE ufrag=" + (m.Ufrag ?? string.Empty)
                            + " pwd=" + (m.Pwd ?? string.Empty));
                    }
                    else if (string.Equals(m.TypeName, "MediaState", StringComparison.Ordinal)
                        || string.Equals(m.TypeName, "RemoteMediaState", StringComparison.Ordinal))
                    {
                        _log.Info("  MediaState muted=" + m.IsMuted
                            + " video=" + (m.VideoState ?? string.Empty)
                            + " screencast=" + (m.ScreencastState ?? string.Empty)
                            + " rotation=" + m.VideoRotation
                            + " lowBattery=" + m.LowBattery);
                    }
                    else if (string.Equals(m.TypeName, "Ping", StringComparison.Ordinal)
                        || string.Equals(m.TypeName, "Pong", StringComparison.Ordinal))
                    {
                        _log.Info("  pingId=" + m.PingId);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Warn("tgcalls signaling decrypt threw: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static string DescribeSignalingPayload(byte[] data)
        {
            if (data == null || data.Length == 0) return "data=0B shape=empty";
            string shape = "binary";
            byte first = data[0];
            if (first == (byte)'{' || first == (byte)'[')
                shape = "json";
            else if (first >= 0x20 && first <= 0x7e)
                shape = "text";

            int take = data.Length < 8 ? data.Length : 8;
            char[] hex = new char[take * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < take; i++)
            {
                hex[i * 2] = digits[data[i] >> 4];
                hex[i * 2 + 1] = digits[data[i] & 0x0f];
            }

            return "data=" + data.Length + "B shape=" + shape + " first=" + new string(hex);
        }

        private void RaiseMediaState(CallId id, CallMediaStateKind state, string detail)
        {
            EventHandler<CallMediaStateChangedEventArgs> handler = MediaStateChanged;
            if (handler == null) return;
            handler(this, new CallMediaStateChangedEventArgs(id, state, detail, DateTime.UtcNow));
        }

        private void OnNativeSignalingDataProduced(object sender, NativeVoip.VoipSignalingDataProducedEventArgs args)
        {
            if (args == null) return;
            byte[] data = args.Data ?? new byte[0];
            if (data.Length == 0)
            {
                _log.Warn("native signaling ignored: empty payload callId=" + args.CallId);
                return;
            }

            EventHandler<CallSignalingDataProducedEventArgs> handler = SignalingDataProduced;
            if (handler == null) return;

            var id = new CallId(args.CallId);
            _log.Info("native signaling produced id=" + id + " data=" + data.Length + "B");
            handler(this, new CallSignalingDataProducedEventArgs(id, data, DateTime.UtcNow));
        }

        private async Task<TelegramDhConfig> GetDhConfigAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (_dhConfig != null && _dhConfig.P != null && _dhConfig.P.Length == 256)
                return _dhConfig;

            await _dhLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_dhConfig != null && _dhConfig.P != null && _dhConfig.P.Length == 256)
                    return _dhConfig;

                byte[] req = TelegramDhConfigCodec.EncodeGetDhConfig(_dhVersion, 256);
                var rpc = await _rpc.CallAsync(req, ct).ConfigureAwait(false);
                if (rpc.IsFail)
                {
                    _log.Warn("messages.getDhConfig failed for calls: " + rpc.Error);
                    return null;
                }

                TelegramDhConfig dh = TelegramDhConfigCodec.DecodeDhConfig(rpc.Value);
                if (dh.NotModified)
                {
                    return _dhConfig;
                }

                _dhConfig = dh;
                _dhVersion = dh.Version;
                _log.Info("messages.getDhConfig cached for calls: g=" + dh.G
                    + " p=" + (dh.P == null ? 0 : dh.P.Length) + "B v=" + dh.Version);
                return _dhConfig;
            }
            catch (Exception ex)
            {
                _log.Warn("messages.getDhConfig decode failed for calls: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
            finally
            {
                _dhLock.Release();
            }
        }

        private static CallError ProjectCryptoError(NativeVoip.VoipOperationResult native, string fallback)
        {
            if (native != null && native.Success)
                return null;

            string message = native == null || string.IsNullOrEmpty(native.ErrorMessage)
                ? fallback
                : native.ErrorMessage;

            if (native != null && native.ErrorCode == 4)
                return CallError.FingerprintMismatch(message);

            return CallError.ProtocolError(message);
        }

        private static CallError ProjectCryptoError(NativeVoip.VoipDhMaterialResult native, string fallback)
        {
            if (native != null && native.Success)
                return null;

            string message = native == null || string.IsNullOrEmpty(native.ErrorMessage)
                ? fallback
                : native.ErrorMessage;

            if (native != null && native.ErrorCode == 4)
                return CallError.FingerprintMismatch(message);

            return CallError.ProtocolError(message);
        }

        private static Result<Unit, CallError> ProjectMediaResult(NativeVoip.VoipOperationResult native, string fallback)
        {
            if (native != null && native.Success)
                return Result<Unit, CallError>.Ok(Unit.Value);

            string message = native == null || string.IsNullOrEmpty(native.ErrorMessage)
                ? fallback
                : native.ErrorMessage;
            return Result<Unit, CallError>.Fail(CallError.MediaPlaneFailed(message));
        }

        private static CallStats ProjectStats(NativeVoip.VoipMediaStatsResult native)
        {
            if (native == null) return CallStats.Empty;
            return new CallStats
            {
                OutboundLevel = native.OutboundLevel,
                InboundLevel = native.InboundLevel,
                PacketLossPercent = native.PacketLossPercent,
                RttMs = native.RttMs,
                BitrateBps = native.BitrateBps,
                Underruns = native.Underruns
            };
        }
    }
}
