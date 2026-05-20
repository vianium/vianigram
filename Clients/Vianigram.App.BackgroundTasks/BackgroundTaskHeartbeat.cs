// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// BackgroundTaskHeartbeat.cs — Vianigram.App.BackgroundTasks
//
// Foreground / background coordination primitive. Two cooperating
// processes (UI shell and a spawned background task) share access to
// the same Telegram session: same auth_key, same DC, same socket.
// Letting both open an MtProtoChannel at once is harmful — Telegram
// flags concurrent encryption with the same auth_key as suspicious
// (potential session-hijack), and the second connection's frames may
// trip "incorrect_server_salt" + retry storms.
//
// We solve this with a heartbeat the foreground writes every 5 s into
// LocalSettings (a key/value store both processes can read). When a
// background task wakes up:
//   - It reads the heartbeat timestamp.
//   - If the heartbeat is "fresh" (younger than HeartbeatStaleAfter,
//     default 10 s) the foreground is alive and owns the socket — the
//     background task does nothing this tick.
//   - If the heartbeat is missing or stale, the background task takes
//     ownership and proceeds to open its own channel.
//
// The shared LocalSettings container is per-package, accessible from
// any process the package launches (background tasks included). No
// extra wiring needed.

using System;
using Windows.Storage;

namespace Vianigram.App.BackgroundTasks
{
    public sealed class BackgroundTaskHeartbeat
    {
        // 5 s write cadence on the foreground side; 10 s tolerance lets
        // a single missed write slip without the background task
        // mistakenly stealing the socket. WinRT public types can't
        // expose fields → kept private; if a future caller needs to
        // tune this it should land as a configuration property.
        private static readonly TimeSpan HeartbeatStaleAfter = TimeSpan.FromSeconds(10);

        private const string SettingsKey = "vianigram.foreground_heartbeat_ticks_utc";

        /// <summary>
        /// Foreground call: stamp the current UTC ticks into LocalSettings.
        /// Cheap (single int64 write); safe to call once per second.
        /// </summary>
        public void Touch()
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[SettingsKey] = DateTime.UtcNow.Ticks;
            }
            catch
            {
                // LocalSettings can throw under exotic disk-full conditions;
                // a missed heartbeat just risks a redundant background
                // wakeup, never data loss.
            }
        }

        /// <summary>
        /// Background call: returns true when the foreground app appears
        /// to be alive and owning the MtProto socket.
        /// </summary>
        public bool IsForegroundAlive()
        {
            try
            {
                object raw;
                if (!ApplicationData.Current.LocalSettings.Values.TryGetValue(SettingsKey, out raw))
                {
                    return false;
                }
                if (raw == null) return false;

                long ticks;
                try { ticks = Convert.ToInt64(raw); }
                catch { return false; }
                if (ticks <= 0L) return false;

                DateTime last = new DateTime(ticks, DateTimeKind.Utc);
                TimeSpan age = DateTime.UtcNow - last;
                if (age < TimeSpan.Zero) return true; // clock skew — assume alive
                return age <= HeartbeatStaleAfter;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Foreground call on suspend / logout: clear the stamp so a
        /// background wakeup on the same boot doesn't see a stale value.
        /// </summary>
        public void ClearForBackgroundHandover()
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values.Remove(SettingsKey);
            }
            catch { }
        }
    }
}
