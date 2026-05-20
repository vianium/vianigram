// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Privacy.Domain.ValueObjects
{
    /// <summary>
    /// A single authorized session as returned by
    /// <c>account.getAuthorizations#e320c158</c> — one per device / app the
    /// user has logged into. Maps to TL <c>authorization#ad01d61d</c>.
    ///
    /// <para>Identity is the opaque <see cref="Hash"/> (server-assigned long)
    /// — passed back to <c>account.resetAuthorization#df77f3bc</c> to log the
    /// session out.</para>
    ///
    /// <para>Immutable POCO; the aggregate (<c>PrivacyProfile</c>) holds the
    /// list and the application layer owns mutation.</para>
    /// </summary>
    public sealed class ActiveSession
    {
        private readonly long _hash;
        private readonly string _deviceModel;
        private readonly string _platform;
        private readonly string _systemVersion;
        private readonly string _appName;
        private readonly string _appVersion;
        private readonly DateTime _dateCreated;
        private readonly DateTime _dateActive;
        private readonly string _ip;
        private readonly string _country;
        private readonly string _region;
        private readonly bool _current;

        public ActiveSession(
            long hash,
            string deviceModel,
            string platform,
            string systemVersion,
            string appName,
            string appVersion,
            DateTime dateCreated,
            DateTime dateActive,
            string ip,
            string country,
            string region,
            bool current)
        {
            _hash = hash;
            _deviceModel = deviceModel ?? string.Empty;
            _platform = platform ?? string.Empty;
            _systemVersion = systemVersion ?? string.Empty;
            _appName = appName ?? string.Empty;
            _appVersion = appVersion ?? string.Empty;
            _dateCreated = dateCreated;
            _dateActive = dateActive;
            _ip = ip ?? string.Empty;
            _country = country ?? string.Empty;
            _region = region ?? string.Empty;
            _current = current;
        }

        public long Hash { get { return _hash; } }
        public string DeviceModel { get { return _deviceModel; } }
        public string Platform { get { return _platform; } }
        public string SystemVersion { get { return _systemVersion; } }
        public string AppName { get { return _appName; } }
        public string AppVersion { get { return _appVersion; } }
        public DateTime DateCreated { get { return _dateCreated; } }
        public DateTime DateActive { get { return _dateActive; } }
        public string Ip { get { return _ip; } }
        public string Country { get { return _country; } }
        public string Region { get { return _region; } }
        /// <summary>True if this is the session issuing the current request — never resettable.</summary>
        public bool IsCurrent { get { return _current; } }

        public override string ToString()
        {
            return "ActiveSession(hash=" + _hash + " " + _deviceModel + "/" + _platform + " " + _appName + " " + _appVersion + (_current ? " *current" : "") + ")";
        }
    }
}
