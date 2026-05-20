// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

using System;
using System.Globalization;
using Windows.Storage;

namespace Vianigram.App
{
    /// <summary>
    /// Time-bomb gate for the Vianigram alpha. Each release pins an
    /// <see cref="ExpiresAtUtc"/> three months past the build date so the
    /// blast radius of any shipped bug stays bounded — once the date
    /// passes, the app refuses to navigate to the shell and instead
    /// shows <c>ExpiredPage</c> with a Telegram link to the next build.
    ///
    /// Two complementary checks (mirrors the VianiumMusic implementation):
    ///
    ///   1. **Hard date** — <c>DateTime.UtcNow &gt; ExpiresAtUtc</c>.
    ///      The honest path. If the user's clock is correct, this fires
    ///      on the cutoff day and never recovers.
    ///
    ///   2. **Monotonic high-water mark** — every successful launch
    ///      records the maximum <c>UtcNow</c> ever seen into
    ///      <c>LocalSettings["vg.app.last_seen_utc"]</c>. On subsequent
    ///      launches we take <c>max(stored, now)</c>; if that maximum
    ///      ever exceeded <see cref="ExpiresAtUtc"/> the gate stays
    ///      closed forever, even if the user later rolls the device
    ///      clock backwards.
    ///
    /// This is **not** a tamper-proof DRM mechanism — a determined user
    /// can clear app data and reset the clock to bypass it. The intent
    /// is to nudge legitimate alpha testers toward the next build before
    /// stale clients accumulate dormant in the wild reporting outdated
    /// MTProto layer ctors and fixed bugs. For a paid app this would
    /// need a server-side check; the alpha doesn't need that.
    ///
    /// **Updating per release**: change <see cref="ExpiresAtUtc"/> to
    /// the new build date + three months and ship. The high-water mark
    /// from the previous run will be inside the new window because the
    /// new window starts later than where the old window closed.
    /// </summary>
    internal static class BuildExpiry
    {
        // -----------------------------------------------------------------
        // Tunable per release
        // -----------------------------------------------------------------

        /// <summary>
        /// Vianigram alpha cutoff: 2026-07-29 23:59:59 UTC.
        ///
        /// Build date 2026-04-29 + three months = 2026-07-29. End-of-day
        /// UTC so the last day is wholly usable for users in any
        /// timezone — a UTC-12 user (Hawaii) on the morning of the
        /// 29th locally is already on the 29th UTC, just at hour 12;
        /// a UTC+14 user (Kiribati) on the evening of the 29th locally
        /// is already past the cutoff on UTC+1, but the cutoff at
        /// 23:59:59 UTC means most local timezones get the full day.
        /// </summary>
        public static readonly DateTime ExpiresAtUtc =
            new DateTime(2026, 7, 29, 23, 59, 59, DateTimeKind.Utc);

        // -----------------------------------------------------------------
        // Persistence keys
        // -----------------------------------------------------------------

        /// <summary>
        /// LocalSettings key used to persist the rolling
        /// <see cref="DateTime.UtcNow"/> high-water mark across launches.
        /// Format: ISO 8601 round-trippable (<c>"o"</c>) UTC string.
        /// Defensive: a malformed value is ignored on read.
        /// </summary>
        public const string MonotonicSettingKey = "vg.app.last_seen_utc";

        // -----------------------------------------------------------------
        // Public API
        // -----------------------------------------------------------------

        /// <summary>
        /// Returns true when the build is past its expiration date.
        /// Side effect: bumps the persisted high-water mark to
        /// <c>max(stored, UtcNow)</c> so a future clock-rollback cannot
        /// step around an already-expired build.
        ///
        /// Never throws — any persistence failure degrades to the bare
        /// <c>UtcNow &gt; ExpiresAtUtc</c> check.
        /// </summary>
        public static bool IsExpired()
        {
            var now = DateTime.UtcNow;

            // Bare hard-time check first — if the device clock is
            // honest, this is the entire story.
            if (now > ExpiresAtUtc)
            {
                BumpHighWaterMark(now);
                return true;
            }

            // Monotonic guard — load the stored high-water mark, take
            // the max with `now`, and check if that max ever crossed.
            DateTime stored;
            if (TryReadHighWaterMark(out stored))
            {
                if (stored > ExpiresAtUtc)
                {
                    // Previous launch already saw a clock value past
                    // the cutoff. Even if the user has rolled the clock
                    // back to before the cutoff, we keep the gate
                    // closed.
                    return true;
                }

                // Persist max(stored, now) so a future rollback cannot
                // step backward past this watermark.
                if (stored > now) BumpHighWaterMark(stored);
                else              BumpHighWaterMark(now);
            }
            else
            {
                // First launch (or unreadable storage). Seed with `now`.
                BumpHighWaterMark(now);
            }

            return false;
        }

        /// <summary>
        /// Friendly local-time string for the expiration date — used by
        /// <c>ExpiredPage</c> and any future about-screen to surface the
        /// cutoff to the user without forcing them to know UTC.
        /// </summary>
        public static string GetLocalExpirationString(CultureInfo culture)
        {
            try
            {
                var local = ExpiresAtUtc.ToLocalTime();
                return local.ToString("D", culture ?? CultureInfo.CurrentCulture);
            }
            catch
            {
                return ExpiresAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        // -----------------------------------------------------------------
        // Internals
        // -----------------------------------------------------------------

        private static bool TryReadHighWaterMark(out DateTime value)
        {
            value = DateTime.MinValue;
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                object raw;
                if (!values.TryGetValue(MonotonicSettingKey, out raw)) return false;
                var s = raw as string;
                if (string.IsNullOrEmpty(s)) return false;

                DateTime parsed;
                if (DateTime.TryParse(
                        s,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                        out parsed))
                {
                    value = DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
                    return true;
                }
            }
            catch
            {
                // Defensive — LocalSettings access can fail under
                // certain edge cases (low storage, corrupted package).
                // The gate degrades to hard-time-only mode.
            }
            return false;
        }

        private static void BumpHighWaterMark(DateTime value)
        {
            try
            {
                var values = ApplicationData.Current.LocalSettings.Values;
                values[MonotonicSettingKey] =
                    value.ToString("o", CultureInfo.InvariantCulture);
            }
            catch
            {
                // Same defensive stance — failure to persist degrades
                // gracefully; the next launch will re-seed.
            }
        }
    }
}
