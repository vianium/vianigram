// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;

namespace Vianigram.Notifications.Domain.ValueObjects
{
    /// <summary>
    /// Per-peer (or global) mute rule. Carries:
    ///   * <see cref="PeerKey"/>     — the addressable target. <see cref="Global"/>
    ///                                 when this rule is the user's global default.
    ///   * <see cref="MuteUntil"/>   — null = not muted; <see cref="DateTime.MaxValue"/>
    ///                                 = muted forever; otherwise the UTC timestamp
    ///                                 when mute lifts.
    ///   * <see cref="Sound"/>       — relative path / asset name for the toast sound;
    ///                                 empty string means "default".
    ///   * <see cref="ShowPreviews"/> — whether the toast body is rendered (vs.
    ///                                  generic "New message").
    ///   * <see cref="IsMutedForever"/> — convenience flag (<see cref="MuteUntil"/>
    ///                                    == <see cref="DateTime.MaxValue"/>).
    ///
    /// Immutable. Every mutation produces a new instance via
    /// <see cref="With"/>.
    /// </summary>
    public sealed class MuteRule
    {
        /// <summary>Sentinel peer key identifying the global / default rule.</summary>
        public const string Global = "*";

        private readonly string _peerKey;
        private readonly DateTime? _muteUntil;
        private readonly string _sound;
        private readonly bool _showPreviews;

        public MuteRule(string peerKey, DateTime? muteUntil, string sound, bool showPreviews)
        {
            if (string.IsNullOrEmpty(peerKey)) throw new ArgumentException("peerKey required", "peerKey");
            _peerKey = peerKey;
            _muteUntil = muteUntil;
            _sound = sound ?? string.Empty;
            _showPreviews = showPreviews;
        }

        public string PeerKey { get { return _peerKey; } }
        public DateTime? MuteUntil { get { return _muteUntil; } }
        public string Sound { get { return _sound; } }
        public bool ShowPreviews { get { return _showPreviews; } }

        public bool IsMutedForever
        {
            get { return _muteUntil.HasValue && _muteUntil.Value == DateTime.MaxValue; }
        }

        /// <summary>Default un-muted rule (sound empty, previews enabled).</summary>
        public static MuteRule DefaultFor(string peerKey)
        {
            return new MuteRule(peerKey ?? Global, null, string.Empty, true);
        }

        /// <summary>True if mute is currently active at the supplied UTC instant.</summary>
        public bool IsMutedAt(DateTime utcNow)
        {
            if (!_muteUntil.HasValue) return false;
            if (_muteUntil.Value == DateTime.MaxValue) return true;
            return utcNow < _muteUntil.Value;
        }

        public MuteRule With(DateTime? muteUntil = null, string sound = null, bool? showPreviews = null)
        {
            return new MuteRule(
                _peerKey,
                muteUntil ?? _muteUntil,
                sound ?? _sound,
                showPreviews ?? _showPreviews);
        }

        /// <summary>Construct a rule muted until a specific UTC instant.</summary>
        public static MuteRule MutedUntil(string peerKey, DateTime untilUtc)
        {
            return new MuteRule(peerKey, untilUtc, string.Empty, true);
        }

        /// <summary>Construct a rule muted forever.</summary>
        public static MuteRule MutedForever(string peerKey)
        {
            return new MuteRule(peerKey, DateTime.MaxValue, string.Empty, true);
        }

        public override string ToString()
        {
            string until = !_muteUntil.HasValue
                ? "active"
                : (_muteUntil.Value == DateTime.MaxValue ? "forever" : _muteUntil.Value.ToString("o"));
            return "mute(" + _peerKey + ", until=" + until + ", preview=" + _showPreviews + ")";
        }
    }
}
