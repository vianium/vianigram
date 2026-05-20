// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// Capability negotiation envelope that ships with every Telegram call
    /// signalling RPC. Mirrors TL <c>phoneCallProtocol</c>:
    /// <code>
    /// phoneCallProtocol#fc878fc8
    ///     flags:#
    ///     udp_p2p:flags.0?true
    ///     udp_reflector:flags.1?true
    ///     min_layer:int
    ///     max_layer:int
    ///     library_versions:Vector&lt;string&gt;
    ///     = PhoneCallProtocol;
    /// </code>
    ///
    /// <para>The exchange is symmetric: the local side asserts what it
    /// supports, the remote side does the same, and both pick the
    /// intersection. Library versions are Telegram wire-compatibility
    /// tokens; the implementation remains the Vianigram-owned native
    /// <c>VianiumVoIP</c> module.</para>
    /// </summary>
    public struct CallProtocol : IEquatable<CallProtocol>
    {
        public const int MinSupportedLayer = 92;
        public const int MaxSupportedLayer = 92;

        private readonly bool _udpP2p;
        private readonly bool _udpReflector;
        private readonly int _minLayer;
        private readonly int _maxLayer;
        private readonly string[] _libraryVersions;

        public CallProtocol(bool udpP2p, bool udpReflector, int minLayer, int maxLayer, string[] libraryVersions)
        {
            if (minLayer < 0) throw new ArgumentException("minLayer must be non-negative", "minLayer");
            if (maxLayer < minLayer) throw new ArgumentException("maxLayer must be >= minLayer", "maxLayer");
            _udpP2p = udpP2p;
            _udpReflector = udpReflector;
            _minLayer = minLayer;
            _maxLayer = maxLayer;
            _libraryVersions = libraryVersions ?? new string[0];
        }

        /// <summary>Default protocol used when the host has no overrides.</summary>
        public static CallProtocol Default
        {
            get
            {
                return new CallProtocol(
                    /*udpP2p*/ true,
                    /*udpReflector*/ true,
                    MinSupportedLayer,
                    MaxSupportedLayer,
                    // Live device test: advertising 8.0.0+ makes the peer
                    // pick tgcalls 2.x (modern WebRTC) which requires TURN
                    // against port 1400 — that path is architecturally
                    // complete in VianiumVoIP but the server-side TURN
                    // handshake never establishes (likely long-term-
                    // credentials MD5/HMAC mismatch or UDP/1400 filtering).
                    // Advertising ONLY classic libtgvoip versions
                    // (2.4.4 / 5.0.0 / 7.0.0) forces the peer to fall back
                    // to classic libtgvoip protocol, which we support
                    // end-to-end via the classic Reflector path
                    // (peer_tag=16B wrap on ports 595..599).
                    new[]
                    {
                        "2.4.4",
                        "5.0.0",
                        "7.0.0"
                    });
            }
        }

        public bool UdpP2p { get { return _udpP2p; } }
        public bool UdpReflector { get { return _udpReflector; } }
        public int MinLayer { get { return _minLayer; } }
        public int MaxLayer { get { return _maxLayer; } }
        public string[] LibraryVersions { get { return _libraryVersions; } }

        public bool Equals(CallProtocol other)
        {
            if (_udpP2p != other._udpP2p) return false;
            if (_udpReflector != other._udpReflector) return false;
            if (_minLayer != other._minLayer) return false;
            if (_maxLayer != other._maxLayer) return false;
            string[] a = _libraryVersions;
            string[] b = other._libraryVersions;
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal)) return false;
            }
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is CallProtocol && Equals((CallProtocol)obj);
        }

        public override int GetHashCode()
        {
            int h = _udpP2p.GetHashCode();
            h = (h * 397) ^ _udpReflector.GetHashCode();
            h = (h * 397) ^ _minLayer.GetHashCode();
            h = (h * 397) ^ _maxLayer.GetHashCode();
            if (_libraryVersions != null)
            {
                for (int i = 0; i < _libraryVersions.Length; i++)
                {
                    string s = _libraryVersions[i];
                    h = (h * 397) ^ (s == null ? 0 : s.GetHashCode());
                }
            }
            return h;
        }

        public override string ToString()
        {
            string libs = string.Empty;
            if (_libraryVersions != null && _libraryVersions.Length > 0)
            {
                libs = string.Join(",", _libraryVersions);
            }
            return "proto[udp_p2p=" + _udpP2p + " udp_reflector=" + _udpReflector
                   + " layer=" + _minLayer + ".." + _maxLayer
                   + " libs=" + (_libraryVersions == null ? 0 : _libraryVersions.Length)
                   + (string.IsNullOrEmpty(libs) ? string.Empty : " [" + libs + "]") + "]";
        }

        public static bool operator ==(CallProtocol a, CallProtocol b) { return a.Equals(b); }
        public static bool operator !=(CallProtocol a, CallProtocol b) { return !a.Equals(b); }
    }
}
