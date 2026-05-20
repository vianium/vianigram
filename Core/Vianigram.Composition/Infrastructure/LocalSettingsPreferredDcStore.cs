// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// SPDX-FileCopyrightText: 2026 Angel Careaga <hello@angelcareaga.com>

// LocalSettingsPreferredDcStore.cs — Vianigram.Composition.Infrastructure
//
// Trivial IPreferredDcStore implementation backed by Windows.Storage
// LocalSettings. Picks a single key ("HomeDcId") and stores it as an int.
//
// We deliberately don't use the bigger IAuthKeyStore plumbing because:
//   1. Boot needs the home DC BEFORE the storage layer (sqlite, blobs) is
//      ready — LocalSettings is available immediately at App.OnLaunched.
//   2. The value is one int; a full async/store ceremony would be overkill.
//
// All methods are exception-safe: a degraded LocalSettings (e.g. unit tests
// without a CoreApplication context) returns 0 on read and silently no-ops
// on write. Callers downstream already treat 0 as "no preference recorded".

using Vianigram.Account.Ports.Outbound;
using Windows.Storage;

namespace Vianigram.Composition.Infrastructure
{
    public sealed class LocalSettingsPreferredDcStore : IPreferredDcStore
    {
        private const string DcKey = "HomeDcId";
        private const string LoginDcHintKey = "LoginDcHint";
        private const string UserIdKey = "AuthorisedUserId";

        public int GetHomeDcId()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return 0;
                object raw;
                if (!settings.Values.TryGetValue(DcKey, out raw)) return 0;
                if (raw is int)
                {
                    int i = (int)raw;
                    if (i > 0) return i;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        public void SetHomeDcId(int dcId)
        {
            if (dcId <= 0) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                settings.Values[DcKey] = dcId;
            }
            catch
            {
                // LocalSettings unavailable — degraded mode acceptable; the
                // user just pays the migration cost again next launch.
            }
        }

        public int GetLoginDcHint()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return 0;
                object raw;
                if (!settings.Values.TryGetValue(LoginDcHintKey, out raw)) return 0;
                if (raw is int)
                {
                    int i = (int)raw;
                    if (i > 0) return i;
                }
                return 0;
            }
            catch
            {
                return 0;
            }
        }

        public void SetLoginDcHint(int dcId)
        {
            if (dcId <= 0) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                settings.Values[LoginDcHintKey] = dcId;
            }
            catch
            {
            }
        }

        public long GetUserId()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return 0L;
                object raw;
                if (!settings.Values.TryGetValue(UserIdKey, out raw)) return 0L;
                if (raw is long)
                {
                    long v = (long)raw;
                    if (v > 0L) return v;
                }
                if (raw is int)
                {
                    int i = (int)raw;
                    if (i > 0) return (long)i;
                }
                return 0L;
            }
            catch
            {
                return 0L;
            }
        }

        public void SetUserId(long userId)
        {
            if (userId <= 0L) return;
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                settings.Values[UserIdKey] = userId;
            }
            catch
            {
            }
        }

        public void Clear()
        {
            try
            {
                var settings = ApplicationData.Current.LocalSettings;
                if (settings == null) return;
                if (settings.Values.ContainsKey(DcKey)) settings.Values.Remove(DcKey);
                if (settings.Values.ContainsKey(LoginDcHintKey)) settings.Values.Remove(LoginDcHintKey);
                if (settings.Values.ContainsKey(UserIdKey)) settings.Values.Remove(UserIdKey);
            }
            catch
            {
            }
        }
    }
}
