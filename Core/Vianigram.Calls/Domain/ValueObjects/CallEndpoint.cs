// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;

namespace Vianigram.Calls.Domain.ValueObjects
{
    /// <summary>
    /// One reachable media-plane endpoint reported by the server with the
    /// established <c>phoneCall</c>. Telegram returns these in
    /// <c>phoneCall.connections</c> + <c>alternative_connections</c>; the
    /// native VoIP runtime probes them in order and selects the lowest-
    /// latency reachable relay.
    ///
    /// Mirrors TL <c>phoneConnection</c>:
    /// <code>
    /// phoneConnection#9cc123c7  id:long ip:string ipv6:string port:int peer_tag:bytes = PhoneConnection;
    /// </code>
    ///
    /// <para>WebRTC connections (<c>phoneConnectionWebrtc</c>) are preserved
    /// so the native media plane can select the negotiated tgcalls/WebRTC
    /// route when Telegram requires modern signaling.</para>
    /// </summary>
    public enum CallEndpointKind
    {
        Reflector = 0,
        WebRtc = 1
    }

    public struct CallEndpoint : IEquatable<CallEndpoint>
    {
        private readonly CallEndpointKind _kind;
        private readonly long _id;
        private readonly string _ip;
        private readonly string _ipv6;
        private readonly int _port;
        private readonly byte[] _peerTag;
        private readonly bool _tcp;
        private readonly bool _stun;
        private readonly bool _turn;
        private readonly string _username;
        private readonly string _password;
        private readonly long _reflectorId;

        public CallEndpoint(long id, string ip, string ipv6, int port, byte[] peerTag)
            : this(CallEndpointKind.Reflector, id, ip, ipv6, port, peerTag, false, false, false, string.Empty, string.Empty, id)
        {
        }

        public CallEndpoint(
            CallEndpointKind kind,
            long id,
            string ip,
            string ipv6,
            int port,
            byte[] peerTag,
            bool tcp,
            bool stun,
            bool turn,
            string username,
            string password,
            long reflectorId)
        {
            if (port < 0 || port > 65535) throw new ArgumentOutOfRangeException("port");
            _kind = kind;
            _id = id;
            _ip = ip ?? string.Empty;
            _ipv6 = ipv6 ?? string.Empty;
            _port = port;
            _peerTag = peerTag ?? new byte[0];
            _tcp = tcp;
            _stun = stun;
            _turn = turn;
            _username = username ?? string.Empty;
            _password = password ?? string.Empty;
            _reflectorId = reflectorId;
        }

        public CallEndpointKind Kind { get { return _kind; } }
        public long Id { get { return _id; } }
        public string Ip { get { return _ip; } }
        public string Ipv6 { get { return _ipv6; } }
        public int Port { get { return _port; } }
        public byte[] PeerTag { get { return _peerTag; } }
        public bool Tcp { get { return _tcp; } }
        public bool Stun { get { return _stun; } }
        public bool Turn { get { return _turn; } }
        public string Username { get { return _username; } }
        public string Password { get { return _password; } }
        public long ReflectorId { get { return _reflectorId; } }

        public bool Equals(CallEndpoint other)
        {
            if (_kind != other._kind) return false;
            if (_id != other._id) return false;
            if (_port != other._port) return false;
            if (_tcp != other._tcp) return false;
            if (_stun != other._stun) return false;
            if (_turn != other._turn) return false;
            if (_reflectorId != other._reflectorId) return false;
            if (!string.Equals(_ip, other._ip, StringComparison.Ordinal)) return false;
            if (!string.Equals(_ipv6, other._ipv6, StringComparison.Ordinal)) return false;
            if (!string.Equals(_username, other._username, StringComparison.Ordinal)) return false;
            if (!string.Equals(_password, other._password, StringComparison.Ordinal)) return false;
            byte[] a = _peerTag;
            byte[] b = other._peerTag;
            if (a == null && b == null) return true;
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        public override bool Equals(object obj)
        {
            return obj is CallEndpoint && Equals((CallEndpoint)obj);
        }

        public override int GetHashCode()
        {
            int h = _kind.GetHashCode();
            h = (h * 397) ^ _id.GetHashCode();
            h = (h * 397) ^ _port.GetHashCode();
            h = (h * 397) ^ (_ip == null ? 0 : _ip.GetHashCode());
            h = (h * 397) ^ _tcp.GetHashCode();
            h = (h * 397) ^ _stun.GetHashCode();
            h = (h * 397) ^ _turn.GetHashCode();
            return h;
        }

        public override string ToString()
        {
            string addr = string.IsNullOrEmpty(_ip) ? _ipv6 : _ip;
            return "endpoint:" + _kind
                   + ":" + _id.ToString("x16", CultureInfo.InvariantCulture)
                   + "@" + addr + ":" + _port.ToString(CultureInfo.InvariantCulture);
        }

        public static bool operator ==(CallEndpoint a, CallEndpoint b) { return a.Equals(b); }
        public static bool operator !=(CallEndpoint a, CallEndpoint b) { return !a.Equals(b); }
    }
}
