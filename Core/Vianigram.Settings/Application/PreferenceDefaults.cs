// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using Vianigram.Settings.Domain.ValueObjects;
using Vianigram.Settings.Infrastructure;

namespace Vianigram.Settings.Application
{
    /// <summary>
    /// Single source of truth for preference defaults. The application layer
    /// resolves a default whenever the underlying store has no user value for
    /// a key — eliminating per-consumer <c>?? defaultX</c> drift.
    ///
    /// Defaults mirror the inventory in
    /// <c>docs/managed-architecture/11-settings.md §3</c>.
    /// </summary>
    internal static class PreferenceDefaults
    {
        public static object ResolveDefault(PreferenceKey key)
        {
            if (key == null) return null;
            string n = key.Name;

            // Appearance
            if (n == PreferenceKeys.Theme.Name) return Theme.System;
            if (n == PreferenceKeys.MessageTextSize.Name) return MessageTextSize.Default;
            if (n == PreferenceKeys.EmojiSize.Name) return EmojiSize.Default;
            if (n == PreferenceKeys.ChatBackground.Name) return ChatBackground.Default;
            if (n == PreferenceKeys.ColorAccentHex.Name) return "#0088CC";

            // Chat
            if (n == PreferenceKeys.SendOnEnter.Name) return true;
            if (n == PreferenceKeys.ShowTypingIndicator.Name) return true;
            if (n == PreferenceKeys.SendReadReceipts.Name) return true;
            if (n == PreferenceKeys.AutoPlayAnimatedStickers.Name) return true;
            if (n == PreferenceKeys.AutoPlayVideos.Name) return false;
            if (n == PreferenceKeys.LargeEmoji.Name) return true;
            if (n == PreferenceKeys.MarkdownInInput.Name) return true;

            // Language
            if (n == PreferenceKeys.LanguagePack.Name) return LanguagePack.Default;

            // Network
            if (n == PreferenceKeys.UseLessData.Name) return false;
            if (n == PreferenceKeys.DataUsageWiFi.Name) return DataUsagePolicy.DefaultWiFi;
            if (n == PreferenceKeys.DataUsageCellular.Name) return DataUsagePolicy.DefaultCellular;
            if (n == PreferenceKeys.DataUsageRoaming.Name) return DataUsagePolicy.DefaultRoaming;

            // Notifications
            if (n == PreferenceKeys.NotificationsPreviewInLockscreen.Name) return true;
            if (n == PreferenceKeys.NotificationsInAppSound.Name) return true;
            if (n == PreferenceKeys.NotificationsInAppVibrate.Name) return true;

            // Privacy (Settings holds the read-only mirror; Privacy context owns writes)
            if (n == PreferenceKeys.PasscodeEnabled.Name) return false;
            if (n == PreferenceKeys.PasscodeLockAfterSeconds.Name) return 300;
            if (n == PreferenceKeys.PasscodeBiometric.Name) return false;

            // Storage
            if (n == PreferenceKeys.StorageCacheMaxMb.Name) return 300;
            if (n == PreferenceKeys.KeepMediaDays.Name) return 30;

            // Voice
            if (n == PreferenceKeys.UseProximitySpeaker.Name) return true;

            // Diagnostics
            if (n == PreferenceKeys.SendCrashReports.Name) return false;
            if (n == PreferenceKeys.VerboseLogging.Name) return false;

            // Unknown key: best-effort by type.
            return DefaultForType(key.ValueType);
        }

        public static T ResolveDefault<T>(PreferenceKey key)
        {
            object boxed = ResolveDefault(key);
            if (boxed is T) return (T)boxed;
            return default(T);
        }

        private static object DefaultForType(Type t)
        {
            if (t == typeof(bool)) return false;
            if (t == typeof(int)) return 0;
            if (t == typeof(long)) return 0L;
            if (t == typeof(double)) return 0.0;
            if (t == typeof(string)) return string.Empty;
            if (t == typeof(MessageTextSize)) return MessageTextSize.Default;
            if (t == typeof(EmojiSize)) return EmojiSize.Default;
            if (t == typeof(NetworkKind)) return NetworkKind.Unknown;
            if (t == typeof(Theme)) return Theme.System;
            // Reference types (LanguagePack, DataUsagePolicy, ChatBackground)
            // fall through to null — the caller resolves via the well-known
            // catalog entries above when the key is recognized.
            return null;
        }
    }
}
