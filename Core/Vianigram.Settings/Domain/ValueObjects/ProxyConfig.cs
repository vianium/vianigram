// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Persistable representation of a Telegram MTProxy descriptor.
    ///
    /// Immutable; mutation produces a new instance via the <see cref="With"/>
    /// helpers. Validation lives in the constructor — callers cannot
    /// construct an invalid <see cref="ProxyConfig"/> (the parser surfaces
    /// errors as parse failures rather than partially-constructed objects).
    ///
    /// JSON-serialized as a single composite value through
    /// <c>IPreferencesStore</c> under <c>PreferenceKeys.ProxyMtProto</c>.
    /// The serializer lives in
    /// <c>Vianigram.Settings.Infrastructure.ProxyConfigCodec</c>.
    ///
    /// Disabled state ⇔ <see cref="Disabled"/>. The settings UI uses an
    /// "Enabled" toggle that flips between Disabled and a stored
    /// host+port+secret triple — we keep the last configured triple even
    /// when disabled so the toggle re-arms previous values without a
    /// re-type. Callers that want to fully forget the proxy should pass
    /// <see cref="Disabled"/> directly.
    /// </summary>
    public sealed class ProxyConfig : IEquatable<ProxyConfig>
    {
        // ----- field-level invariants -------------------------------------
        // Enabled = false  → host may still be present (preview); secret may be empty array
        // Enabled = true   → host non-empty; port in (0,65535]; secret == 16 bytes; for FakeTls SNI non-empty

        private readonly bool             _enabled;
        private readonly string           _host;
        private readonly int              _port;
        private readonly byte[]           _secret;          // 16 bytes when meaningful
        private readonly ProxySecretMode  _mode;
        private readonly string           _fakeTlsDomain;
        private readonly string           _label;

        public bool             Enabled        { get { return _enabled; } }
        public string           Host           { get { return _host; } }
        public int              Port           { get { return _port; } }
        public byte[]           Secret         { get { return CloneSecret(); } }
        public ProxySecretMode  Mode           { get { return _mode; } }
        public string           FakeTlsDomain  { get { return _fakeTlsDomain; } }
        public string           Label          { get { return _label; } }

        // ----- well-known singletons --------------------------------------

        /// <summary>
        /// The empty/disabled descriptor. The runtime treats this as a
        /// signal to dial direct.
        /// </summary>
        public static readonly ProxyConfig Disabled =
            new ProxyConfig(false, string.Empty, 0, EmptySecret, ProxySecretMode.Legacy, string.Empty, string.Empty);

        // ----- construction ----------------------------------------------

        public ProxyConfig(
            bool enabled,
            string host,
            int port,
            byte[] secret,
            ProxySecretMode mode,
            string fakeTlsDomain,
            string label)
        {
            // Defensive: never accept null arrays / strings — the JSON
            // codec drops nulls back to empty so we mirror that here.
            host = host ?? string.Empty;
            secret = secret ?? EmptySecret;
            fakeTlsDomain = fakeTlsDomain ?? string.Empty;
            label = label ?? string.Empty;

            if (enabled)
            {
                if (host.Length == 0)
                    throw new ArgumentException("Enabled proxy requires host", "host");
                if (port <= 0 || port > 65535)
                    throw new ArgumentOutOfRangeException("port", "must be in (0, 65535]");
                if (secret.Length != 16)
                    throw new ArgumentException("MTProxy secret must be 16 bytes", "secret");
                if (mode == ProxySecretMode.FakeTls && fakeTlsDomain.Length == 0)
                    throw new ArgumentException("FakeTls mode requires fakeTlsDomain", "fakeTlsDomain");
            }

            _enabled       = enabled;
            _host          = host;
            _port          = port;
            _secret        = CopySecret(secret);
            _mode          = mode;
            _fakeTlsDomain = fakeTlsDomain;
            _label         = label;
        }

        // ----- with-helpers -----------------------------------------------

        public ProxyConfig WithEnabled(bool enabled)
        {
            return new ProxyConfig(enabled, _host, _port, _secret, _mode, _fakeTlsDomain, _label);
        }

        public ProxyConfig WithLabel(string label)
        {
            return new ProxyConfig(_enabled, _host, _port, _secret, _mode, _fakeTlsDomain, label ?? string.Empty);
        }

        // ----- equality ---------------------------------------------------

        public bool Equals(ProxyConfig other)
        {
            if (other == null) return false;
            if (_enabled != other._enabled) return false;
            if (!string.Equals(_host, other._host, StringComparison.Ordinal)) return false;
            if (_port != other._port) return false;
            if (_mode != other._mode) return false;
            if (!string.Equals(_fakeTlsDomain, other._fakeTlsDomain, StringComparison.OrdinalIgnoreCase)) return false;
            if (!string.Equals(_label, other._label, StringComparison.Ordinal)) return false;
            return SecretsEqual(_secret, other._secret);
        }

        public override bool Equals(object obj) { return Equals(obj as ProxyConfig); }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = 17;
                h = h * 31 + _enabled.GetHashCode();
                h = h * 31 + (_host != null ? _host.GetHashCode() : 0);
                h = h * 31 + _port;
                h = h * 31 + (int)_mode;
                h = h * 31 + (_fakeTlsDomain != null ? _fakeTlsDomain.GetHashCode() : 0);
                // secret bytes intentionally excluded — equality is decided
                // by SecretsEqual; GetHashCode is best-effort.
                return h;
            }
        }

        public override string ToString()
        {
            if (!_enabled) return "ProxyConfig(disabled)";
            return "ProxyConfig(" + _host + ":" + _port + " mode=" + _mode + ")";
        }

        // ----- helpers ----------------------------------------------------

        private static readonly byte[] EmptySecret = new byte[0];

        private byte[] CloneSecret()
        {
            if (_secret == null || _secret.Length == 0) return EmptySecret;
            byte[] copy = new byte[_secret.Length];
            Array.Copy(_secret, copy, _secret.Length);
            return copy;
        }

        private static byte[] CopySecret(byte[] src)
        {
            if (src == null || src.Length == 0) return EmptySecret;
            byte[] copy = new byte[src.Length];
            Array.Copy(src, copy, src.Length);
            return copy;
        }

        private static bool SecretsEqual(byte[] a, byte[] b)
        {
            if (a == null) a = EmptySecret;
            if (b == null) b = EmptySecret;
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
