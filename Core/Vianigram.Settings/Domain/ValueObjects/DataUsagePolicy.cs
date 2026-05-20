// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Settings.Domain.ValueObjects
{
    /// <summary>
    /// Auto-download toggles for one network classification (<see cref="Network"/>).
    /// Carries three independent flags — photos / videos / voice — matching the
    /// Telegram <c>account.saveAutoDownloadSettings</c> shape (subset; full
    /// shape covers documents and music too — V1 keeps the prompt's reduced
    /// matrix and exposes <see cref="DownloadDocuments"/> as a future-proofing
    /// flag defaulted to false).
    ///
    /// Immutable; mutation produces a new instance via <see cref="With"/>.
    /// </summary>
    public sealed class DataUsagePolicy : IEquatable<DataUsagePolicy>
    {
        private readonly NetworkKind _network;
        private readonly bool _autoDownloadPhotos;
        private readonly bool _autoDownloadVideos;
        private readonly bool _autoDownloadVoice;
        private readonly bool _autoDownloadDocuments;
        private readonly long _maxFileSizeBytes;

        public DataUsagePolicy(
            NetworkKind network,
            bool autoDownloadPhotos,
            bool autoDownloadVideos,
            bool autoDownloadVoice,
            bool autoDownloadDocuments,
            long maxFileSizeBytes)
        {
            if (maxFileSizeBytes < 0) throw new ArgumentOutOfRangeException("maxFileSizeBytes", "must be non-negative");
            _network = network;
            _autoDownloadPhotos = autoDownloadPhotos;
            _autoDownloadVideos = autoDownloadVideos;
            _autoDownloadVoice = autoDownloadVoice;
            _autoDownloadDocuments = autoDownloadDocuments;
            _maxFileSizeBytes = maxFileSizeBytes;
        }

        public NetworkKind Network { get { return _network; } }
        public bool AutoDownloadPhotos { get { return _autoDownloadPhotos; } }
        public bool AutoDownloadVideos { get { return _autoDownloadVideos; } }
        public bool AutoDownloadVoice { get { return _autoDownloadVoice; } }
        public bool AutoDownloadDocuments { get { return _autoDownloadDocuments; } }
        public long MaxFileSizeBytes { get { return _maxFileSizeBytes; } }

        // ---- well-known defaults (mirror Telegram-Android UserConfig + DataSettingsActivity) -------

        /// <summary>WiFi default: photos+videos+voice on, 50 MB cap.</summary>
        public static DataUsagePolicy DefaultWiFi
        {
            get
            {
                return new DataUsagePolicy(
                    NetworkKind.WiFi,
                    autoDownloadPhotos: true,
                    autoDownloadVideos: true,
                    autoDownloadVoice: true,
                    autoDownloadDocuments: true,
                    maxFileSizeBytes: 50L * 1024 * 1024);
            }
        }

        /// <summary>Cellular default: photos+voice on; videos off; 5 MB cap.</summary>
        public static DataUsagePolicy DefaultCellular
        {
            get
            {
                return new DataUsagePolicy(
                    NetworkKind.Cellular,
                    autoDownloadPhotos: true,
                    autoDownloadVideos: false,
                    autoDownloadVoice: true,
                    autoDownloadDocuments: false,
                    maxFileSizeBytes: 5L * 1024 * 1024);
            }
        }

        /// <summary>Roaming default: nothing auto-downloads.</summary>
        public static DataUsagePolicy DefaultRoaming
        {
            get
            {
                return new DataUsagePolicy(
                    NetworkKind.Roaming,
                    autoDownloadPhotos: false,
                    autoDownloadVideos: false,
                    autoDownloadVoice: false,
                    autoDownloadDocuments: false,
                    maxFileSizeBytes: 0);
            }
        }

        public DataUsagePolicy With(
            bool? autoDownloadPhotos = null,
            bool? autoDownloadVideos = null,
            bool? autoDownloadVoice = null,
            bool? autoDownloadDocuments = null,
            long? maxFileSizeBytes = null)
        {
            return new DataUsagePolicy(
                _network,
                autoDownloadPhotos ?? _autoDownloadPhotos,
                autoDownloadVideos ?? _autoDownloadVideos,
                autoDownloadVoice ?? _autoDownloadVoice,
                autoDownloadDocuments ?? _autoDownloadDocuments,
                maxFileSizeBytes ?? _maxFileSizeBytes);
        }

        public DataUsagePolicy ForNetwork(NetworkKind network)
        {
            return new DataUsagePolicy(network, _autoDownloadPhotos, _autoDownloadVideos, _autoDownloadVoice, _autoDownloadDocuments, _maxFileSizeBytes);
        }

        public bool Equals(DataUsagePolicy other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null) return false;
            return _network == other._network
                && _autoDownloadPhotos == other._autoDownloadPhotos
                && _autoDownloadVideos == other._autoDownloadVideos
                && _autoDownloadVoice == other._autoDownloadVoice
                && _autoDownloadDocuments == other._autoDownloadDocuments
                && _maxFileSizeBytes == other._maxFileSizeBytes;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as DataUsagePolicy);
        }

        public override int GetHashCode()
        {
            int h = (int)_network;
            h = (h * 397) ^ (_autoDownloadPhotos ? 1 : 0);
            h = (h * 397) ^ (_autoDownloadVideos ? 1 : 0);
            h = (h * 397) ^ (_autoDownloadVoice ? 1 : 0);
            h = (h * 397) ^ (_autoDownloadDocuments ? 1 : 0);
            h = (h * 397) ^ _maxFileSizeBytes.GetHashCode();
            return h;
        }

        public override string ToString()
        {
            return "data(" + _network
                + ", photos=" + _autoDownloadPhotos
                + ", videos=" + _autoDownloadVideos
                + ", voice=" + _autoDownloadVoice
                + ", docs=" + _autoDownloadDocuments
                + ", max=" + _maxFileSizeBytes + "B)";
        }
    }
}
