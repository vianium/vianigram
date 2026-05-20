// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using Vianigram.Settings.Domain.ValueObjects;

namespace Vianigram.Settings.Infrastructure
{
    /// <summary>
    /// Static catalog of well-known <see cref="PreferenceKey"/> instances. One
    /// reference per key keeps consumers honest — string typos turn into
    /// compile errors instead of phantom missing settings.
    ///
    /// V1 surface mirrors the inventory in
    /// <c>docs/managed-architecture/11-settings.md §3</c>. The strings used
    /// here are also the storage keys passed to <c>IPreferencesStore</c>.
    /// </summary>
    public static class PreferenceKeys
    {
        // ---- Appearance --------------------------------------------------------
        public static readonly PreferenceKey Theme =
            new PreferenceKey("appearance.theme_mode", typeof(Theme));

        public static readonly PreferenceKey MessageTextSize =
            new PreferenceKey("appearance.font_size", typeof(MessageTextSize));

        public static readonly PreferenceKey EmojiSize =
            new PreferenceKey("appearance.emoji_size", typeof(EmojiSize));

        public static readonly PreferenceKey ChatBackground =
            new PreferenceKey("appearance.chat_background", typeof(ChatBackground));

        public static readonly PreferenceKey ColorAccentHex =
            new PreferenceKey("appearance.color_accent", typeof(string));

        // ---- Chat ---------------------------------------------------------------
        public static readonly PreferenceKey SendOnEnter =
            new PreferenceKey("chat.send_on_enter", typeof(bool));

        public static readonly PreferenceKey ShowTypingIndicator =
            new PreferenceKey("chat.show_typing_indicator_to_others", typeof(bool));

        public static readonly PreferenceKey SendReadReceipts =
            new PreferenceKey("chat.read_receipts_to_others", typeof(bool));

        public static readonly PreferenceKey AutoPlayAnimatedStickers =
            new PreferenceKey("chat.auto_play_animated_stickers", typeof(bool));

        public static readonly PreferenceKey AutoPlayVideos =
            new PreferenceKey("chat.auto_play_videos", typeof(bool));

        public static readonly PreferenceKey LargeEmoji =
            new PreferenceKey("chat.large_emoji", typeof(bool));

        public static readonly PreferenceKey MarkdownInInput =
            new PreferenceKey("chat.markdown_in_input", typeof(bool));

        // ---- Language -----------------------------------------------------------
        public static readonly PreferenceKey LanguagePack =
            new PreferenceKey("language.pack", typeof(LanguagePack));

        // ---- Network / data -----------------------------------------------------
        public static readonly PreferenceKey UseLessData =
            new PreferenceKey("network.use_less_data", typeof(bool));

        /// <summary>
        /// Composite MTProxy descriptor (host, port, 16-byte secret,
        /// mode, optional fake-TLS SNI). The wire format is fully
        /// versioned (<c>v1|...</c>) so the codec can evolve without
        /// breaking already-persisted users. Settable through
        /// <see cref="Vianigram.Settings.Ports.Inbound.ISettingsApi"/>'s
        /// proxy facade; persisted under this key.
        /// </summary>
        public static readonly PreferenceKey ProxyMtProto =
            new PreferenceKey("network.proxy.mtproto", typeof(ProxyConfig));

        public const string AutoDownloadPhotosWifi = "data.autoDownload.photos.wifi";
        public const string AutoDownloadPhotosCellular = "data.autoDownload.photos.cellular";
        public const string AutoDownloadPhotosRoaming = "data.autoDownload.photos.roaming";

        public const string AutoDownloadVideosWifi = "data.autoDownload.videos.wifi";
        public const string AutoDownloadVideosCellular = "data.autoDownload.videos.cellular";
        public const string AutoDownloadVideosRoaming = "data.autoDownload.videos.roaming";

        public const string AutoDownloadVoiceWifi = "data.autoDownload.voice.wifi";
        public const string AutoDownloadVoiceCellular = "data.autoDownload.voice.cellular";
        public const string AutoDownloadVoiceRoaming = "data.autoDownload.voice.roaming";

        /// <summary>
        /// Composite policy key per network — a single JSON blob covering
        /// photos / videos / voice / docs / max-size at once. Consumers prefer
        /// this composite read over the per-flag scalars above.
        /// </summary>
        public static readonly PreferenceKey DataUsageWiFi =
            new PreferenceKey("network.auto_download.wifi", typeof(DataUsagePolicy));

        public static readonly PreferenceKey DataUsageCellular =
            new PreferenceKey("network.auto_download.cellular", typeof(DataUsagePolicy));

        public static readonly PreferenceKey DataUsageRoaming =
            new PreferenceKey("network.auto_download.roaming", typeof(DataUsagePolicy));

        // ---- Notifications (settings owns the user-facing flag; the Notifications
        //                    context resolves the runtime mute rule via outbound ACL) ------
        public static readonly PreferenceKey NotificationsPreviewInLockscreen =
            new PreferenceKey("notifications.preview_in_lockscreen", typeof(bool));

        public static readonly PreferenceKey NotificationsInAppSound =
            new PreferenceKey("notifications.in_app_sound", typeof(bool));

        public static readonly PreferenceKey NotificationsInAppVibrate =
            new PreferenceKey("notifications.in_app_vibrate", typeof(bool));

        // ---- Privacy (read-only by Settings; Privacy context owns writes) -------
        public static readonly PreferenceKey PasscodeEnabled =
            new PreferenceKey("privacy.passcode_enabled", typeof(bool));

        public static readonly PreferenceKey PasscodeLockAfterSeconds =
            new PreferenceKey("privacy.passcode_lock_after_seconds", typeof(int));

        public static readonly PreferenceKey PasscodeBiometric =
            new PreferenceKey("privacy.passcode_biometric", typeof(bool));

        // ---- Storage / cache ----------------------------------------------------
        public static readonly PreferenceKey StorageCacheMaxMb =
            new PreferenceKey("storage.cache_max_mb", typeof(int));

        public static readonly PreferenceKey KeepMediaDays =
            new PreferenceKey("storage.keep_media_days", typeof(int));

        // ---- Voice / VoIP -------------------------------------------------------
        public static readonly PreferenceKey UseProximitySpeaker =
            new PreferenceKey("voice.use_proximity_speaker", typeof(bool));

        // ---- Diagnostics --------------------------------------------------------
        public static readonly PreferenceKey SendCrashReports =
            new PreferenceKey("diagnostic.send_crash_reports", typeof(bool));

        public static readonly PreferenceKey VerboseLogging =
            new PreferenceKey("diagnostic.verbose_logging", typeof(bool));

        // ---- Schema -------------------------------------------------------------
        /// <summary>
        /// Internal slot recording the current schema version. Persisted as
        /// <c>major*1000 + minor</c> (e.g. <c>1001</c> = v1.1) so cold-start
        /// migrations have a single int to compare against.
        /// </summary>
        public const string SchemaVersionKey = "__schema_version__";

        // ---- helpers ------------------------------------------------------------

        /// <summary>
        /// Resolve the per-network <see cref="PreferenceKey"/> for the
        /// composite <see cref="DataUsagePolicy"/> blob.
        /// </summary>
        public static PreferenceKey DataUsageFor(NetworkKind network)
        {
            switch (network)
            {
                case NetworkKind.WiFi: return DataUsageWiFi;
                case NetworkKind.Cellular: return DataUsageCellular;
                case NetworkKind.Roaming: return DataUsageRoaming;
                default: return DataUsageWiFi;
            }
        }
    }
}
